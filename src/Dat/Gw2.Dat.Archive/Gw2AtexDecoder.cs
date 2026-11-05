using System.Buffers.Binary;

namespace Gw2.Dat.Archive;

public sealed record Gw2AtexFormat(string FourCc, int Id, uint Traits, int BitsPerPixel)
{
    private const uint BlockCompressedTrait = 0x1;
    private const uint ColorPlaneTrait = 0x10;
    private const uint AlphaPlaneTrait = 0x80;
    private const uint DualPlaneTrait = 0x200;

    public const int Dxt1Id = 0x16;
    public const int Dxt2Id = 0x17;
    public const int Dxt3Id = 0x18;
    public const int Dxt4Id = 0x19;
    public const int Dxt5Id = 0x1A;
    public const int DxtaId = 0x1B;
    public const int DxtlId = 0x1C;
    public const int DxtnId = 0x1D;
    public const int ThreeDcxId = 0x1E;
    public const int Bc5xId = 0x1F;
    public const int Bc7Id = 0x20;

    public bool IsBlockCompressed => (Traits & BlockCompressedTrait) != 0;
    public bool HasColorPlane => (Traits & (ColorPlaneTrait | DualPlaneTrait)) != 0;
    public bool HasAlphaPlane => (Traits & (AlphaPlaneTrait | DualPlaneTrait)) != 0;
    public bool HasLuminancePlane => Id == DxtlId;
    public int AlphaWords => HasAlphaPlane ? 2 : 0;
    public int LuminanceWords => HasLuminancePlane ? 2 : 0;
    public int ColorWords => HasColorPlane ? 2 : 0;
    public int BlockBytes => (AlphaWords + LuminanceWords + ColorWords) * 4;
    public int ColorOffset => (AlphaWords + LuminanceWords) * 4;

    private static readonly Gw2AtexFormat[] Known =
    [
        new("UC01", 0x01, 0xB2, 64), new("UC02", 0x02, 0xB2, 32), new("UC03", 0x03, 0xB2, 32), new("UC04", 0x04, 0x12, 32),
        new("UC05", 0x05, 0xB2, 16), new("UC06", 0x06, 0x72, 16), new("UC07", 0x07, 0x12, 24), new("UC08", 0x08, 0x12, 16),
        new("UC09", 0x09, 0x12, 16), new("UC10", 0x0A, 0x12, 32), new("UC11", 0x0C, 0x12, 64), new("UC12", 0x0D, 0x100, 16),
        new("UC13", 0x0E, 0x100, 32), new("UC14", 0x0F, 0x1A4, 16), new("UC15", 0x10, 0x1A4, 8), new("UC16", 0x11, 0x1A4, 8),
        new("UC18", 0x12, 0x104, 8), new("UC19", 0x13, 0xA2, 8), new("UC21", 0x15, 0x400, 16),
        new("DXT1", Dxt1Id, 0x71, 4), new("DXT2", Dxt2Id, 0xB1, 8), new("DXT3", Dxt3Id, 0xB1, 8), new("DXT4", Dxt4Id, 0xB1, 8),
        new("DXT5", Dxt5Id, 0xB1, 8), new("DXTA", DxtaId, 0xA1, 4), new("DXTL", DxtlId, 0x11, 8), new("DXTN", DxtnId, 0x201, 8),
        new("3DCX", ThreeDcxId, 0x201, 8), new("BC5X", Bc5xId, 0x201, 8), new("BC7X", Bc7Id, 0xB1, 8),
    ];

    public static Gw2AtexFormat? Of(string fourCc) => Array.Find(Known, format => format.FourCc == fourCc);
}

public static class Gw2AtexDecoder
{
    private const uint WhiteBlocksFlag = 0x1;
    private const uint ConstantAlpha4Flag = 0x2;
    private const uint ConstantAlpha8Flag = 0x4;
    private const uint SolidColorFlag = 0x8;
    private const uint TerrainBordersFlag = 0x10;
    private const uint PaletteRunsFlag = 0x200;
    private const uint TerrainBorderMask = 0xC0000003;
    private const int TerrainSize = 0x100;
    private const int TerrainBlocksPerRow = 0x40;
    private const ulong WhiteDxt1Block = 0xFFFFFFFFFFFFFFFE;
    private const int PaletteBytes = 32;
    private const int PaletteEntryBytes = 8;

    public static byte[] Decode(Gw2AtexFormat format, Gw2TextureMip mip, int maxOutputBytes)
    {
        var payload = mip.EncodedBytes[8..];
        if (!format.IsBlockCompressed)
        {
            var pixelBytes = format.BitsPerPixel / 8;
            var size = checked(mip.Width * mip.Height * pixelBytes);
            if (size > maxOutputBytes) throw new InvalidDataException("Texture output exceeds limit");
            if (payload.Length < size) throw new InvalidDataException("Truncated texture pixel data");
            return payload[..size].ToArray();
        }

        var blocks = checked(((mip.Width + 3) / 4) * ((mip.Height + 3) / 4));
        if ((long)blocks * format.BlockBytes > maxOutputBytes) throw new InvalidDataException("Texture output exceeds limit");
        var output = new byte[blocks * format.BlockBytes];
        if ((mip.EncodingFlags & PaletteRunsFlag) != 0)
        {
            DecodePaletteRuns(format, payload, blocks, output);
            return output;
        }

        var alphaDone = new bool[blocks];
        var colorDone = new bool[blocks];
        var isTerrain = (mip.EncodingFlags & TerrainBordersFlag) != 0 && mip.Width == TerrainSize && mip.Height == TerrainSize &&
                        format.Id is Gw2AtexFormat.Dxt2Id or Gw2AtexFormat.Dxt3Id;
        if (isTerrain)
            for (var block = 0; block < blocks; block++)
                if (IsTerrainBorder(block))
                {
                    alphaDone[block] = true;
                    colorDone[block] = true;
                }

        var bits = new CodingBits(payload);
        var stride = format.BlockBytes;
        var colorOffset = format.ColorOffset;

        if ((mip.EncodingFlags & WhiteBlocksFlag) != 0 && format.HasColorPlane && !format.HasAlphaPlane && !format.HasLuminancePlane)
            FillRuns(bits, blocks, colorDone, (block, _) =>
            {
                BinaryPrimitives.WriteUInt64LittleEndian(output.AsSpan(block * stride + colorOffset, 8), WhiteDxt1Block);
                colorDone[block] = true;
                alphaDone[block] = true;
            }, false);

        if ((mip.EncodingFlags & ConstantAlpha4Flag) != 0 && format.Id is Gw2AtexFormat.Dxt2Id or Gw2AtexFormat.Dxt3Id)
        {
            var nibble = (ulong)bits.Read(4);
            var value = nibble * 0x1111111111111111UL;
            FillRuns(bits, blocks, colorDone, (block, useValue) =>
            {
                BinaryPrimitives.WriteUInt64LittleEndian(output.AsSpan(block * stride, 8), useValue ? value : 0);
                alphaDone[block] = true;
            }, true);
        }

        if ((mip.EncodingFlags & ConstantAlpha8Flag) != 0 && format.Id is >= Gw2AtexFormat.Dxt4Id and <= Gw2AtexFormat.DxtlId)
        {
            var alpha = (ulong)bits.Read(8);
            var value = alpha | alpha << 8;
            FillRuns(bits, blocks, colorDone, (block, useValue) =>
            {
                BinaryPrimitives.WriteUInt64LittleEndian(output.AsSpan(block * stride, 8), useValue ? value : 0);
                alphaDone[block] = true;
            }, true);
        }

        if ((mip.EncodingFlags & SolidColorFlag) != 0 && format.HasColorPlane)
        {
            var rgb = bits.Read(24) | 0xFF000000u;
            var solid = SolidColorBlock(rgb, format.Id == Gw2AtexFormat.Dxt1Id);
            FillRuns(bits, blocks, colorDone, (block, _) =>
            {
                BinaryPrimitives.WriteUInt64LittleEndian(output.AsSpan(block * stride + colorOffset, 8), solid);
                colorDone[block] = true;
            }, false);
        }

        var position = bits.AlignedByteOffset;
        void Copy(int destination, int count)
        {
            if (position > payload.Length - count) throw new InvalidDataException("Truncated texture block data");
            payload.Span.Slice(position, count).CopyTo(output.AsSpan(destination, count));
            position += count;
        }

        if (format.HasAlphaPlane || format.HasLuminancePlane)
            for (var block = 0; block < blocks; block++)
                if (!alphaDone[block]) Copy(block * stride, 8);
        if (format.HasColorPlane)
            for (var word = 0; word < 2; word++)
                for (var block = 0; block < blocks; block++)
                    if (!colorDone[block]) Copy(block * stride + colorOffset + word * 4, 4);

        if (isTerrain) FillTerrainBorders(output, blocks);
        return output;
    }

    private static bool IsTerrainBorder(int block) =>
        ((1u << (block & 31)) & TerrainBorderMask) != 0 || ((1u << ((block >> 6) & 31)) & TerrainBorderMask) != 0;

    private static void FillRuns(CodingBits bits, int blocks, bool[] done, Action<int, bool> fill, bool hasValueChoice)
    {
        var block = 0;
        while (block < blocks)
        {
            var run = bits.Read(1) == 1 ? 1 : bits.Read(1) == 1 ? 18 : 17 - (int)bits.Read(4);
            var isFilled = bits.Read(1) != 0;
            var useValue = !isFilled || !hasValueChoice || bits.Read(1) != 0;
            while (run > 0 && block < blocks)
            {
                if (!done[block])
                {
                    if (isFilled) fill(block, useValue);
                    run--;
                }

                block++;
            }

            while (block < blocks && done[block]) block++;
        }
    }

    private static void DecodePaletteRuns(Gw2AtexFormat format, ReadOnlyMemory<byte> payload, int blocks, byte[] output)
    {
        var bytes = new PaletteRunBytes(payload);
        var palettes = new byte[2][];
        palettes[0] = bytes.ReadBlock(PaletteBytes);
        var planeCount = format.Id is Gw2AtexFormat.Dxt1Id or Gw2AtexFormat.DxtaId ? 1 : 2;
        palettes[1] = planeCount == 2 ? bytes.ReadBlock(PaletteBytes) : new byte[PaletteBytes];
        var planeBlocks = new[] { 0, planeCount == 2 ? 0 : blocks };
        var planeOffsets = new[] { 0, (format.AlphaWords + format.LuminanceWords) * 4 };
        var stride = format.BlockBytes;

        while (planeBlocks[0] < blocks || planeBlocks[1] < blocks)
        {
            var code = bytes.ReadByte();
            if (code == 0xFF) throw new InvalidDataException("Texture palette run code 0xFF is invalid");
            var length = (code & 8) == 0 ? (code & 7) + 1 : 1 << ((code & 7) + 3);
            var plane = (code >> 6) & 1;
            if (planeBlocks[plane] + length > blocks) throw new InvalidDataException("Texture palette run exceeds block count");
            var isLiteral = (code & 0x80) != 0;
            if (isLiteral && (code & 0x30) != 0) throw new InvalidDataException("Texture literal run has a palette index");
            for (var index = 0; index < length; index++)
            {
                var destination = output.AsSpan(planeBlocks[plane] * stride + planeOffsets[plane], PaletteEntryBytes);
                if (isLiteral) bytes.ReadBlock(PaletteEntryBytes).CopyTo(destination);
                else palettes[plane].AsSpan(((code >> 4) & 3) * PaletteEntryBytes, PaletteEntryBytes).CopyTo(destination);
                planeBlocks[plane]++;
            }
        }
    }

    private static void FillTerrainBorders(byte[] output, int blocks)
    {
        var words = new uint[4];
        for (var block = 0; block < blocks; block++)
        {
            var row = block >> 6;
            var mirrorsColumn = ((1u << (block & 31)) & TerrainBorderMask) != 0;
            var mirrorsRow = ((1u << (row & 31)) & TerrainBorderMask) != 0;
            if (!mirrorsColumn && !mirrorsRow) continue;
            var sourceColumn = mirrorsColumn ? (block & 0x3F) ^ 3 : block & 0x3F;
            var sourceRow = mirrorsRow ? row ^ 3 : row;
            var source = (sourceRow * TerrainBlocksPerRow + sourceColumn) * 16;
            for (var word = 0; word < 4; word++) words[word] = BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(source + word * 4));

            var alpha0 = words[0];
            var alpha1 = words[1];
            var colors = words[2];
            var indices = words[3];
            if (mirrorsColumn)
            {
                alpha1 = alpha0;
                alpha0 = (words[0] >> 8 & 0xF000F0 | words[0] & 0xF000F00) >> 4 | ((words[0] & 0xFFFF000F) << 8 | words[0] & 0xF000F0) << 4;
                indices = ((indices & 0xFF030303) << 4 | indices & 0x0C0C0C0C) << 2 | (indices >> 4 & 0x0C0C0C0C | indices & 0x30303030) >> 2;
            }

            if (mirrorsRow)
            {
                var swappedAlpha0 = alpha1 >> 16 | alpha1 << 16;
                alpha1 = alpha0 >> 16 | alpha0 << 16;
                alpha0 = swappedAlpha0;
                indices = BinaryPrimitives.ReverseEndianness(indices);
            }

            var destination = block * 16;
            BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(destination), alpha0);
            BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(destination + 4), alpha1);
            BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(destination + 8), colors);
            BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(destination + 12), indices);
        }
    }

    public static ulong SolidColorBlock(uint rgb, bool allowsThreeColorMode)
    {
        var low = rgb & 0xFF;
        var green = rgb >> 8 & 0xFF;
        var high = rgb >> 16 & 0xFF;

        static (uint Quantized, uint Residual) Quantize(uint value, int bits)
        {
            var reduced = value - (value >> bits);
            var quantized = reduced >> (8 - bits);
            var expanded = (quantized << (8 - bits)) + (reduced >> bits);
            var nextExpanded = ((quantized + 1) << (8 - bits)) + ((quantized + 1) >> (2 * bits - 8));
            return (quantized, (value - expanded) * 12 / (nextExpanded - expanded));
        }

        static (uint First, uint Second) Round(uint quantized, uint residual) =>
            residual < 2 ? (quantized, quantized)
            : residual <= 5 ? (quantized, quantized + 1)
            : residual <= 9 ? (quantized + 1, quantized)
            : (quantized + 1, quantized + 1);

        var (lowQ, lowResidual) = Quantize(low, 5);
        var (greenQ, greenResidual) = Quantize(green, 6);
        var (highQ, highResidual) = Quantize(high, 5);
        var (lowFirst, lowSecond) = Round(lowQ, lowResidual);
        var (greenFirst, greenSecond) = Round(greenQ, greenResidual);
        var (highFirst, highSecond) = Round(highQ, highResidual);
        var first = (highFirst << 6 | greenFirst) << 5 | lowFirst;
        var second = (highSecond << 6 | greenSecond) << 5 | lowSecond;

        uint weight = 0, changes = 0;
        if (lowFirst != lowSecond) { changes++; weight += lowFirst == lowQ ? lowResidual : 12 - lowResidual; }
        if (greenFirst != greenSecond) { changes++; weight += greenFirst == greenQ ? greenResidual : 12 - greenResidual; }
        if (highFirst != highSecond) { changes++; weight += highFirst == highQ ? highResidual : 12 - highResidual; }
        if (changes != 0) weight = (changes / 2 + weight) / changes;

        var isThreeColor = allowsThreeColorMode && (weight - 5 < 2 || changes == 0);
        if (changes == 0 && !isThreeColor)
        {
            if (second == 0xFFFF)
            {
                weight = 12;
                first--;
            }
            else
            {
                second++;
                weight = 0;
            }
        }

        var color0 = first;
        var color1 = second;
        if (isThreeColor != (first <= second))
        {
            weight = 12 - weight;
            color0 = second;
            color1 = first;
        }

        var index = isThreeColor ? 2u : weight < 2 ? 0u : weight <= 5 ? 2u : weight < 10 ? 3u : 1u;
        index |= index << 2;
        index |= index << 4;
        index |= index << 8;
        index |= index << 16;
        return (color0 & 0xFFFF) | (ulong)(color1 & 0xFFFF) << 16 | (ulong)index << 32;
    }

    private sealed class CodingBits(ReadOnlyMemory<byte> data)
    {
        private long _position;

        public int AlignedByteOffset => (int)((_position + 31) / 32 * 4);

        public uint Read(int count)
        {
            uint value = 0;
            var wordCount = data.Length / 4;
            for (var index = 0; index < count; index++, _position++)
            {
                var wordIndex = (int)(_position / 32);
                var word = wordIndex < wordCount ? BinaryPrimitives.ReadUInt32LittleEndian(data.Span[(wordIndex * 4)..]) : 0u;
                value = (value << 1) | ((word >> (31 - (int)(_position % 32))) & 1);
            }

            return value;
        }
    }

    private sealed class PaletteRunBytes(ReadOnlyMemory<byte> data)
    {
        private int _position;

        public byte ReadByte()
        {
            if (_position >= data.Length / 4 * 4) throw new InvalidDataException("Truncated texture palette run data");
            var word = _position & ~3;
            var byteInWord = 3 - (_position & 3);
            _position++;
            return data.Span[word + byteInWord];
        }

        public byte[] ReadBlock(int count)
        {
            var block = new byte[count];
            for (var index = 0; index < count; index++) block[index] = ReadByte();
            return block;
        }
    }
}
