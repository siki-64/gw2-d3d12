using System;
using System.Buffers.Binary;
using Gw2.Dat.Archive;

namespace Gw2.Client.Scene;

public sealed class Gw2RgbaImage
{
    private const int BytesPerPixel = 4;
    private const byte FlatNormalChannel = 128;

    private static readonly string[] TwoChannelFormats = ["3DCX", "BC5X"];
    private const string GreenFirstTwoChannelFormat = "3DCX";

    public Gw2RgbaImage(int width, int height)
    {
        Width = width;
        Height = height;
        Pixels = new byte[width * height * BytesPerPixel];
    }

    public int Width { get; }

    public int Height { get; }

    public byte[] Pixels { get; }

    public static Gw2RgbaImage TransparentWhite(int width, int height)
    {
        var image = new Gw2RgbaImage(width, height);
        for (var offset = 0; offset < image.Pixels.Length; offset += BytesPerPixel)
        {
            image.Pixels[offset + 0] = byte.MaxValue;
            image.Pixels[offset + 1] = byte.MaxValue;
            image.Pixels[offset + 2] = byte.MaxValue;
        }

        return image;
    }

    public static Gw2RgbaImage FlatNormals(int width, int height)
    {
        var image = new Gw2RgbaImage(width, height);
        for (var offset = 0; offset < image.Pixels.Length; offset += BytesPerPixel)
        {
            image.Pixels[offset + 0] = FlatNormalChannel;
            image.Pixels[offset + 1] = FlatNormalChannel;
            image.Pixels[offset + 2] = byte.MaxValue;
            image.Pixels[offset + 3] = byte.MaxValue;
        }

        return image;
    }

    public static Gw2RgbaImage FromTexture(Gw2Texture texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        var format = texture.AtexFormat ?? throw new NotSupportedException($"Texture format {texture.Format} is not an ATEX format.");
        var blocks = texture.DecodeGpuBlocks();
        var mip = texture.Mips[0];
        var image = new Gw2RgbaImage(mip.Width, mip.Height);
        var blocksWide = (mip.Width + 3) / 4;
        var blocksHigh = (mip.Height + 3) / 4;
        if (format.Id == Gw2AtexFormat.DxtnId)
        {
            var bgra = Gw2DxtnDecoder.DecodeToBgra(blocks, mip.Width, mip.Height);
            for (var texel = 0; texel < bgra.Length; texel++)
            {
                image.Pixels[texel * BytesPerPixel + 0] = (byte)(bgra[texel] >> 16);
                image.Pixels[texel * BytesPerPixel + 1] = (byte)(bgra[texel] >> 8);
                image.Pixels[texel * BytesPerPixel + 2] = (byte)bgra[texel];
                image.Pixels[texel * BytesPerPixel + 3] = (byte)(bgra[texel] >> 24);
            }

            return image;
        }

        if (Array.IndexOf(TwoChannelFormats, format.FourCc) >= 0)
        {
            DecodeTwoChannel(blocks, image, blocksWide, blocksHigh, format.FourCc == GreenFirstTwoChannelFormat);
            return image;
        }

        if (format.Id == Gw2AtexFormat.DxtaId) blocks = Gw2AtexBlit.DxtaToDxt5(blocks);
        var colorOnly = format.Id is Gw2AtexFormat.Dxt1Id;
        var explicitAlpha = format.Id is Gw2AtexFormat.Dxt2Id or Gw2AtexFormat.Dxt3Id;
        var interpolatedAlpha = format.Id is Gw2AtexFormat.Dxt4Id or Gw2AtexFormat.Dxt5Id or Gw2AtexFormat.DxtaId;
        if (!colorOnly && !explicitAlpha && !interpolatedAlpha)
            throw new NotSupportedException($"Texture format {texture.Format} has no RGBA decode.");

        var blockBytes = colorOnly ? 8 : 16;
        var colors = new uint[4];
        var alphas = new byte[16];
        for (var blockY = 0; blockY < blocksHigh; blockY++)
        for (var blockX = 0; blockX < blocksWide; blockX++)
        {
            var block = blocks.AsSpan((blockY * blocksWide + blockX) * blockBytes, blockBytes);
            var colorBlock = colorOnly ? block : block[8..];
            ReadColorPalette(colorBlock, colors, colorOnly);
            if (explicitAlpha) ReadExplicitAlpha(block, alphas);
            else if (interpolatedAlpha) ReadInterpolatedAlpha(block, alphas);

            var colorIndices = BinaryPrimitives.ReadUInt32LittleEndian(colorBlock[4..]);
            for (var texel = 0; texel < 16; texel++)
            {
                var x = blockX * 4 + (texel & 3);
                var y = blockY * 4 + (texel >> 2);
                if (x >= mip.Width || y >= mip.Height) continue;
                var color = colors[(colorIndices >> (texel * 2)) & 3];
                var offset = (y * mip.Width + x) * BytesPerPixel;
                image.Pixels[offset + 0] = (byte)color;
                image.Pixels[offset + 1] = (byte)(color >> 8);
                image.Pixels[offset + 2] = (byte)(color >> 16);
                image.Pixels[offset + 3] = colorOnly ? (byte)(color >> 24) : alphas[texel];
            }
        }

        return image;
    }

    public static Gw2RgbaImage FromMaskTexture(Gw2Texture texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        const int BlockBytes = 8;
        var blocks = texture.DecodeGpuBlocks();
        var mip = texture.Mips[0];
        var image = new Gw2RgbaImage(mip.Width, mip.Height);
        var blocksWide = (mip.Width + 3) / 4;
        var blocksHigh = (mip.Height + 3) / 4;
        var coverage = new byte[16];
        for (var blockY = 0; blockY < blocksHigh; blockY++)
        for (var blockX = 0; blockX < blocksWide; blockX++)
        {
            var blockOffset = (blockY * blocksWide + blockX) * BlockBytes;
            if (blockOffset + BlockBytes > blocks.Length) continue;
            ReadInterpolatedAlpha(blocks.AsSpan(blockOffset, BlockBytes), coverage);
            for (var texel = 0; texel < 16; texel++)
            {
                var x = blockX * 4 + (texel & 3);
                var y = blockY * 4 + (texel >> 2);
                if (x >= mip.Width || y >= mip.Height) continue;
                var value = coverage[texel];
                image.SetPixel(x, y, Pack(value, value, value, byte.MaxValue));
            }
        }

        return image;
    }

    public void SetPixel(int x, int y, uint rgba) =>
        BinaryPrimitives.WriteUInt32LittleEndian(Pixels.AsSpan((y * Width + x) * BytesPerPixel), rgba);

    public uint GetPixel(int x, int y) =>
        BinaryPrimitives.ReadUInt32LittleEndian(Pixels.AsSpan((y * Width + x) * BytesPerPixel));

    private static void DecodeTwoChannel(byte[] blocks, Gw2RgbaImage image, int blocksWide, int blocksHigh, bool greenFirst)
    {
        const int BlockBytes = 16;
        var red = new byte[16];
        var green = new byte[16];
        for (var blockY = 0; blockY < blocksHigh; blockY++)
        for (var blockX = 0; blockX < blocksWide; blockX++)
        {
            var block = blocks.AsSpan((blockY * blocksWide + blockX) * BlockBytes, BlockBytes);
            ReadInterpolatedAlpha(greenFirst ? block[8..] : block, red);
            ReadInterpolatedAlpha(greenFirst ? block : block[8..], green);
            for (var texel = 0; texel < 16; texel++)
            {
                var x = blockX * 4 + (texel & 3);
                var y = blockY * 4 + (texel >> 2);
                if (x >= image.Width || y >= image.Height) continue;
                var offset = (y * image.Width + x) * BytesPerPixel;
                image.Pixels[offset + 0] = red[texel];
                image.Pixels[offset + 1] = green[texel];
                image.Pixels[offset + 2] = byte.MaxValue;
                image.Pixels[offset + 3] = byte.MaxValue;
            }
        }
    }

    private static void ReadColorPalette(ReadOnlySpan<byte> block, uint[] colors, bool allowTransparent)
    {
        var first = BinaryPrimitives.ReadUInt16LittleEndian(block);
        var second = BinaryPrimitives.ReadUInt16LittleEndian(block[2..]);
        var (r0, g0, b0) = Expand565(first);
        var (r1, g1, b1) = Expand565(second);
        colors[0] = Pack(r0, g0, b0, 255);
        colors[1] = Pack(r1, g1, b1, 255);
        if (first > second || !allowTransparent)
        {
            colors[2] = Pack((2 * r0 + r1) / 3, (2 * g0 + g1) / 3, (2 * b0 + b1) / 3, 255);
            colors[3] = Pack((r0 + 2 * r1) / 3, (g0 + 2 * g1) / 3, (b0 + 2 * b1) / 3, 255);
        }
        else
        {
            colors[2] = Pack((r0 + r1) / 2, (g0 + g1) / 2, (b0 + b1) / 2, 255);
            colors[3] = 0;
        }
    }

    private static void ReadExplicitAlpha(ReadOnlySpan<byte> block, byte[] alphas)
    {
        var bits = BinaryPrimitives.ReadUInt64LittleEndian(block);
        for (var texel = 0; texel < 16; texel++)
            alphas[texel] = (byte)(((bits >> (texel * 4)) & 0xF) * 17);
    }

    private static void ReadInterpolatedAlpha(ReadOnlySpan<byte> block, byte[] alphas)
    {
        int first = block[0];
        int second = block[1];
        Span<int> palette = stackalloc int[8];
        palette[0] = first;
        palette[1] = second;
        if (first > second)
            for (var step = 1; step < 7; step++) palette[step + 1] = ((7 - step) * first + step * second) / 7;
        else
        {
            for (var step = 1; step < 5; step++) palette[step + 1] = ((5 - step) * first + step * second) / 5;
            palette[6] = 0;
            palette[7] = 255;
        }

        var bits = BinaryPrimitives.ReadUInt64LittleEndian(block) >> 16;
        for (var texel = 0; texel < 16; texel++)
            alphas[texel] = (byte)palette[(int)((bits >> (texel * 3)) & 7)];
    }

    private static (int Red, int Green, int Blue) Expand565(ushort color)
    {
        var red = (color >> 11) & 0x1F;
        var green = (color >> 5) & 0x3F;
        var blue = color & 0x1F;
        return ((red << 3) | (red >> 2), (green << 2) | (green >> 4), (blue << 3) | (blue >> 2));
    }

    private static uint Pack(int red, int green, int blue, int alpha) =>
        (uint)red | (uint)green << 8 | (uint)blue << 16 | (uint)alpha << 24;
}
