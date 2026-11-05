using System.Buffers.Binary;

namespace Gw2.Dat.Archive;

public static class Gw2DxtnDecoder
{
    private const int BlockBytes = 16;
    private const float NormalScale = 0.0078125f;
    private const float NormalBias = 127.5f;

    public static uint[] DecodeToBgra(ReadOnlySpan<byte> blocks, int width, int height)
    {
        var pixels = new uint[width * height];
        var blocksWide = (width + 3) / 4;
        var blocksHigh = (height + 3) / 4;
        Span<byte> axisY = stackalloc byte[8];
        Span<byte> axisX = stackalloc byte[4];
        for (var blockY = 0; blockY < blocksHigh; blockY++)
        for (var blockX = 0; blockX < blocksWide; blockX++)
        {
            var block = blocks.Slice((blockY * blocksWide + blockX) * BlockBytes, BlockBytes);
            AlphaPalette(block[0], block[1], axisY);
            GreenPalette(BinaryPrimitives.ReadUInt16LittleEndian(block[8..]), BinaryPrimitives.ReadUInt16LittleEndian(block[10..]), axisX);
            var axisYIndices = BinaryPrimitives.ReadUInt64LittleEndian(block) >> 16;
            var axisXIndices = BinaryPrimitives.ReadUInt32LittleEndian(block[12..]);
            for (var texel = 0; texel < 16; texel++)
            {
                var x = blockX * 4 + (texel & 3);
                var y = blockY * 4 + (texel >> 2);
                if (x >= width || y >= height) continue;
                var green = axisX[(int)(axisXIndices >> (2 * texel)) & 3];
                var alpha = axisY[(int)(axisYIndices >> (3 * texel)) & 7];
                pixels[y * width + x] = 0xFF000000u | (uint)alpha << 16 | (uint)green << 8 | ReconstructZ(green, alpha);
            }
        }

        return pixels;
    }

    private static byte ReconstructZ(byte green, byte alpha)
    {
        var greenAxis = green * NormalScale - 1.0f;
        var alphaAxis = alpha * NormalScale - 1.0f;
        var squared = 1.0f - (greenAxis * greenAxis + alphaAxis * alphaAxis);
        if (0.0f - squared >= 0.0f) squared = 0.0f;
        return (byte)(int)((MathF.Sqrt(squared) + 1.0f) * NormalBias);
    }

    private static void AlphaPalette(int first, int second, Span<byte> palette)
    {
        palette[0] = (byte)first;
        palette[1] = (byte)second;
        if (first <= second)
        {
            palette[2] = (byte)((4 * first + second + 2) / 5);
            palette[3] = (byte)((2 * second + 3 * first + 2) / 5);
            palette[4] = (byte)((3 * second + 2 * first + 2) / 5);
            palette[5] = (byte)((4 * second + first + 2) / 5);
            palette[6] = 0;
            palette[7] = 255;
            return;
        }

        palette[2] = (byte)((second + 6 * first + 3) / 7);
        palette[3] = (byte)((5 * first + 2 * second + 3) / 7);
        palette[4] = (byte)((3 * second + 4 * first + 3) / 7);
        palette[5] = (byte)((4 * second + 3 * first + 3) / 7);
        palette[6] = (byte)((5 * second + 2 * first + 3) / 7);
        palette[7] = (byte)((first + 6 * second + 3) / 7);
    }

    private static void GreenPalette(ushort first, ushort second, Span<byte> palette)
    {
        var firstGreen = Gw2Dxt5Encoder.ExpandSixBits((first >> 5) & 0x3F);
        var secondGreen = Gw2Dxt5Encoder.ExpandSixBits((second >> 5) & 0x3F);
        palette[0] = (byte)firstGreen;
        palette[1] = (byte)secondGreen;
        if (first > second)
        {
            palette[2] = (byte)((2 * firstGreen + secondGreen + 1) / 3);
            palette[3] = (byte)((firstGreen + 2 * secondGreen + 1) / 3);
            return;
        }

        palette[2] = palette[3] = (byte)((firstGreen + secondGreen + 1) >> 1);
    }
}
