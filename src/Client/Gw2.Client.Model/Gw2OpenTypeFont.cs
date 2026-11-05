using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Gw2.Client.Model;

public sealed record Gw2OpenTypeFontNames(string Family, string PostScriptName);

public static class Gw2OpenTypeFont
{
    public const int UnitsPerPixel = 64;
    private const uint TrueTypeSfntVersion = 0x00010000;
    private const uint ChecksumMagic = 0xB1B0AFBA;
    private const int CbdtImageFormatSmallMetricsPng = 17;
    private const int IndexFormatVariableWidthOffsets = 1;
    private const int ColorBitDepth = 32;
    private const int HorizontalMetricsFlag = 1;
    private const int PairAdjustmentLookup = 2;
    private const int ExtensionPositioningLookup = 9;
    private const int XAdvanceValueFormat = 0x0004;
    private const int PairPosSubtableBudget = 60000;
    private const ushort WindowsPlatform = 3;
    private const ushort UnicodeBmpEncoding = 1;
    private const ushort EnglishUnitedStates = 0x0409;
    private const ushort RegularWeight = 400;
    private const ushort MediumWidth = 5;
    private const ushort RegularSelection = 0x0040;

    public static byte[] Write(Gw2BakedFont font, Gw2OpenTypeFontNames names)
    {
        var glyphIdByCodePoint = new Dictionary<int, ushort>(font.Glyphs.Count);
        for (var index = 0; index < font.Glyphs.Count; index++)
            glyphIdByCodePoint[font.Glyphs[index].CodePoint] = (ushort)(index + 1);
        var glyphCount = font.Glyphs.Count + 1;
        var ascender = font.Baseline;
        var descender = font.Baseline - font.CellHeight;

        var tables = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["CBDT"] = Array.Empty<byte>(),
            ["CBLC"] = Array.Empty<byte>(),
            ["OS/2"] = OperatingSystemMetrics(font, ascender, descender),
            ["cmap"] = CharacterMap(font),
            ["glyf"] = Array.Empty<byte>(),
            ["head"] = Header(font, ascender, descender),
            ["hhea"] = HorizontalHeader(font, glyphCount, ascender, descender),
            ["hmtx"] = HorizontalMetrics(font),
            ["loca"] = new byte[(glyphCount + 1) * 2],
            ["maxp"] = MaximumProfile(glyphCount),
            ["name"] = Naming(names),
            ["post"] = PostScript(font),
        };
        (tables["CBLC"], tables["CBDT"]) = ColorBitmaps(font, ascender, descender);
        if (font.PairAdjustments.Count > 0) tables["GPOS"] = GlyphPositioning(font, glyphIdByCodePoint);
        return Assemble(tables);
    }

    public static byte[] EncodeWhiteCoveragePng(int width, int height, byte[] coverage)
    {
        var rgba = new byte[width * height * 4];
        for (var pixel = 0; pixel < coverage.Length; pixel++)
        {
            rgba[pixel * 4] = 0xFF;
            rgba[pixel * 4 + 1] = 0xFF;
            rgba[pixel * 4 + 2] = 0xFF;
            rgba[pixel * 4 + 3] = coverage[pixel];
        }
        return Gw2PngEncoder.EncodeRgba(width, height, rgba);
    }

    private static byte[] Header(Gw2BakedFont font, int ascender, int descender)
    {
        var buffer = new BigEndianBuffer();
        buffer.U32(0x00010000);
        buffer.U32(0x00010000);
        buffer.U32(0);
        buffer.U32(0x5F0F3CF5);
        buffer.U16(0x000B);
        buffer.U16((ushort)(font.CellHeight * UnitsPerPixel));
        buffer.U64(0);
        buffer.U64(0);
        buffer.I16(0);
        buffer.I16((short)(descender * UnitsPerPixel));
        buffer.I16((short)(MaximumInkRight(font) * UnitsPerPixel));
        buffer.I16((short)(ascender * UnitsPerPixel));
        buffer.U16(0);
        buffer.U16((ushort)font.CellHeight);
        buffer.I16(2);
        buffer.I16(0);
        buffer.I16(0);
        return buffer.ToArray();
    }

    private static byte[] HorizontalHeader(Gw2BakedFont font, int glyphCount, int ascender, int descender)
    {
        var buffer = new BigEndianBuffer();
        buffer.U32(0x00010000);
        buffer.I16((short)(ascender * UnitsPerPixel));
        buffer.I16((short)(descender * UnitsPerPixel));
        buffer.I16(0);
        buffer.U16((ushort)(MaximumAdvance(font) * UnitsPerPixel));
        buffer.I16((short)(font.Glyphs.Select(glyph => glyph.BearingX).DefaultIfEmpty(0).Min() * UnitsPerPixel));
        buffer.I16(0);
        buffer.I16((short)(MaximumInkRight(font) * UnitsPerPixel));
        buffer.I16(1);
        buffer.I16(0);
        buffer.I16(0);
        for (var reserved = 0; reserved < 4; reserved++) buffer.I16(0);
        buffer.I16(0);
        buffer.U16((ushort)glyphCount);
        return buffer.ToArray();
    }

    private static byte[] HorizontalMetrics(Gw2BakedFont font)
    {
        var buffer = new BigEndianBuffer();
        buffer.U16((ushort)(font.MissingCharacterAdvance * UnitsPerPixel));
        buffer.I16(0);
        foreach (var glyph in font.Glyphs)
        {
            buffer.U16((ushort)(Math.Max(0, glyph.Advance) * UnitsPerPixel));
            buffer.I16((short)(glyph.BearingX * UnitsPerPixel));
        }
        return buffer.ToArray();
    }

    private static byte[] MaximumProfile(int glyphCount)
    {
        var buffer = new BigEndianBuffer();
        buffer.U32(0x00010000);
        buffer.U16((ushort)glyphCount);
        buffer.U16(0);
        buffer.U16(0);
        buffer.U16(0);
        buffer.U16(0);
        buffer.U16(2);
        for (var field = 0; field < 8; field++) buffer.U16(0);
        return buffer.ToArray();
    }

    private static byte[] OperatingSystemMetrics(Gw2BakedFont font, int ascender, int descender)
    {
        var unitsPerEm = font.CellHeight * UnitsPerPixel;
        var capitalHeight = font.Glyphs.FirstOrDefault(glyph => glyph.CodePoint == 'H')?.BearingY ?? ascender;
        var lowercaseHeight = font.Glyphs.FirstOrDefault(glyph => glyph.CodePoint == 'x')?.BearingY ?? capitalHeight;
        var drawn = font.Glyphs.Where(glyph => glyph.Advance > 0).ToList();
        var averageAdvance = drawn.Count == 0 ? font.MissingCharacterAdvance : (int)Math.Round(drawn.Average(glyph => glyph.Advance));

        var buffer = new BigEndianBuffer();
        buffer.U16(4);
        buffer.I16((short)(averageAdvance * UnitsPerPixel));
        buffer.U16(RegularWeight);
        buffer.U16(MediumWidth);
        buffer.U16(0);
        buffer.I16((short)(unitsPerEm / 2));
        buffer.I16((short)(unitsPerEm / 2));
        buffer.I16(0);
        buffer.I16((short)(-descender * UnitsPerPixel));
        buffer.I16((short)(unitsPerEm / 2));
        buffer.I16((short)(unitsPerEm / 2));
        buffer.I16(0);
        buffer.I16((short)(ascender * UnitsPerPixel / 2));
        buffer.I16(UnitsPerPixel);
        buffer.I16((short)(lowercaseHeight * UnitsPerPixel / 2));
        buffer.I16(0);
        buffer.Bytes(new byte[10]);
        buffer.U32(0);
        buffer.U32(0);
        buffer.U32(0);
        buffer.U32(0);
        buffer.Bytes("GW2 "u8.ToArray());
        buffer.U16(RegularSelection);
        buffer.U16((ushort)font.Glyphs.Select(glyph => glyph.CodePoint).DefaultIfEmpty(0).Min());
        buffer.U16((ushort)font.Glyphs.Select(glyph => glyph.CodePoint).DefaultIfEmpty(0).Max());
        buffer.I16((short)(ascender * UnitsPerPixel));
        buffer.I16((short)(descender * UnitsPerPixel));
        buffer.I16(0);
        buffer.U16((ushort)(ascender * UnitsPerPixel));
        buffer.U16((ushort)(-descender * UnitsPerPixel));
        buffer.U32(1);
        buffer.U32(0);
        buffer.I16((short)(lowercaseHeight * UnitsPerPixel));
        buffer.I16((short)(capitalHeight * UnitsPerPixel));
        buffer.U16(0);
        buffer.U16(Gw2FontArchive.SpaceCodePoint);
        buffer.U16(2);
        return buffer.ToArray();
    }

    private static byte[] Naming(Gw2OpenTypeFontNames names)
    {
        var records = new (ushort NameId, string Value)[]
        {
            (1, names.Family),
            (2, "Regular"),
            (3, names.PostScriptName),
            (4, names.Family),
            (6, names.PostScriptName),
        };
        var strings = new BigEndianBuffer();
        var buffer = new BigEndianBuffer();
        buffer.U16(0);
        buffer.U16((ushort)records.Length);
        buffer.U16((ushort)(6 + records.Length * 12));
        foreach (var (nameId, value) in records)
        {
            var encoded = Encoding.BigEndianUnicode.GetBytes(value);
            buffer.U16(WindowsPlatform);
            buffer.U16(UnicodeBmpEncoding);
            buffer.U16(EnglishUnitedStates);
            buffer.U16(nameId);
            buffer.U16((ushort)encoded.Length);
            buffer.U16((ushort)strings.Length);
            strings.Bytes(encoded);
        }
        buffer.Bytes(strings.ToArray());
        return buffer.ToArray();
    }

    private static byte[] PostScript(Gw2BakedFont font)
    {
        var buffer = new BigEndianBuffer();
        buffer.U32(0x00030000);
        buffer.U32(0);
        buffer.I16((short)((font.Baseline - font.CellHeight) * UnitsPerPixel / 2));
        buffer.I16(UnitsPerPixel);
        buffer.U32(0);
        for (var field = 0; field < 4; field++) buffer.U32(0);
        return buffer.ToArray();
    }

    private static byte[] CharacterMap(Gw2BakedFont font)
    {
        var segments = new List<(ushort Start, ushort End, short Delta)>();
        for (var index = 0; index < font.Glyphs.Count; index++)
        {
            var codePoint = font.Glyphs[index].CodePoint;
            var glyphId = index + 1;
            if (segments.Count > 0 && segments[^1].End + 1 == codePoint && (short)(glyphId - codePoint) == segments[^1].Delta)
                segments[^1] = (segments[^1].Start, (ushort)codePoint, segments[^1].Delta);
            else
                segments.Add(((ushort)codePoint, (ushort)codePoint, (short)(glyphId - codePoint)));
        }
        segments.Add((0xFFFF, 0xFFFF, 1));

        var segmentCount = segments.Count;
        var searchRange = 2 * (1 << (int)Math.Floor(Math.Log(segmentCount, 2)));
        var subtable = new BigEndianBuffer();
        subtable.U16(4);
        subtable.U16((ushort)(16 + segmentCount * 8));
        subtable.U16(0);
        subtable.U16((ushort)(segmentCount * 2));
        subtable.U16((ushort)searchRange);
        subtable.U16((ushort)Math.Log(searchRange / 2, 2));
        subtable.U16((ushort)(segmentCount * 2 - searchRange));
        foreach (var segment in segments) subtable.U16(segment.End);
        subtable.U16(0);
        foreach (var segment in segments) subtable.U16(segment.Start);
        foreach (var segment in segments) subtable.I16(segment.Delta);
        foreach (var _ in segments) subtable.U16(0);

        var buffer = new BigEndianBuffer();
        buffer.U16(0);
        buffer.U16(1);
        buffer.U16(WindowsPlatform);
        buffer.U16(UnicodeBmpEncoding);
        buffer.U32(12);
        buffer.Bytes(subtable.ToArray());
        return buffer.ToArray();
    }

    private static (byte[] Locations, byte[] Data) ColorBitmaps(Gw2BakedFont font, int ascender, int descender)
    {
        var data = new BigEndianBuffer();
        data.U16(3);
        data.U16(0);
        var imageDataOffset = data.Length;
        var glyphOffsets = new List<uint>(font.Glyphs.Count + 1);
        foreach (var glyph in font.Glyphs)
        {
            glyphOffsets.Add((uint)(data.Length - imageDataOffset));
            var source = glyph.Source;
            var width = Math.Max(1, source.Width);
            var height = Math.Max(1, source.Height);
            var coverage = source.Width == 0 || source.Height == 0 ? new byte[1] : source.Coverage;
            var png = EncodeWhiteCoveragePng(width, height, coverage);
            data.U8((byte)height);
            data.U8((byte)width);
            data.I8((sbyte)glyph.BearingX);
            data.I8((sbyte)glyph.BearingY);
            data.U8((byte)Math.Clamp(glyph.Advance, 0, byte.MaxValue));
            data.U32((uint)png.Length);
            data.Bytes(png);
        }
        glyphOffsets.Add((uint)(data.Length - imageDataOffset));

        const int headerSize = 8;
        const int bitmapSizeRecordSize = 48;
        const int indexSubtableArrayEntrySize = 8;
        var indexSubtableSize = 8 + glyphOffsets.Count * 4;
        var locations = new BigEndianBuffer();
        locations.U16(3);
        locations.U16(0);
        locations.U32(1);
        locations.U32(headerSize + bitmapSizeRecordSize);
        locations.U32((uint)(indexSubtableArrayEntrySize + indexSubtableSize));
        locations.U32(1);
        locations.U32(0);
        LineMetrics(locations, font, ascender, descender);
        LineMetrics(locations, font, ascender, descender);
        locations.U16(1);
        locations.U16((ushort)font.Glyphs.Count);
        locations.U8((byte)font.CellHeight);
        locations.U8((byte)font.CellHeight);
        locations.U8(ColorBitDepth);
        locations.I8(HorizontalMetricsFlag);
        locations.U16(1);
        locations.U16((ushort)font.Glyphs.Count);
        locations.U32(indexSubtableArrayEntrySize);
        locations.U16(IndexFormatVariableWidthOffsets);
        locations.U16(CbdtImageFormatSmallMetricsPng);
        locations.U32((uint)imageDataOffset);
        foreach (var offset in glyphOffsets) locations.U32(offset);
        return (locations.ToArray(), data.ToArray());
    }

    private static void LineMetrics(BigEndianBuffer buffer, Gw2BakedFont font, int ascender, int descender)
    {
        buffer.I8((sbyte)ascender);
        buffer.I8((sbyte)descender);
        buffer.U8((byte)Math.Min(byte.MaxValue, MaximumInkRight(font)));
        buffer.I8(1);
        buffer.I8(0);
        buffer.I8(0);
        buffer.I8((sbyte)Math.Clamp(font.Glyphs.Select(glyph => glyph.BearingX).DefaultIfEmpty(0).Min(), sbyte.MinValue, sbyte.MaxValue));
        buffer.I8(0);
        buffer.I8((sbyte)ascender);
        buffer.I8((sbyte)descender);
        buffer.I8(0);
        buffer.I8(0);
    }

    private static byte[] GlyphPositioning(Gw2BakedFont font, IReadOnlyDictionary<int, ushort> glyphIdByCodePoint)
    {
        var pairSets = font.PairAdjustments
            .GroupBy(pair => glyphIdByCodePoint[pair.FirstCodePoint])
            .OrderBy(group => group.Key)
            .Select(group => (First: group.Key, Seconds: group
                .Select(pair => (Second: glyphIdByCodePoint[pair.SecondCodePoint], Adjustment: (short)(pair.AdvanceAdjustment * UnitsPerPixel)))
                .OrderBy(pair => pair.Second)
                .ToList()))
            .ToList();

        var pairPosSubtables = new List<byte[]>();
        var chunk = new List<(ushort First, List<(ushort Second, short Adjustment)> Seconds)>();
        var chunkSize = 0;
        foreach (var pairSet in pairSets)
        {
            var pairSetSize = 2 + 2 + 2 + 2 + pairSet.Seconds.Count * 4;
            if (chunk.Count > 0 && chunkSize + pairSetSize > PairPosSubtableBudget)
            {
                pairPosSubtables.Add(PairPosition(chunk));
                chunk = new();
                chunkSize = 0;
            }
            chunk.Add(pairSet);
            chunkSize += pairSetSize;
        }
        if (chunk.Count > 0) pairPosSubtables.Add(PairPosition(chunk));

        var lookup = new BigEndianBuffer();
        lookup.U16(ExtensionPositioningLookup);
        lookup.U16(0);
        lookup.U16((ushort)pairPosSubtables.Count);
        var extensionsStart = 6 + pairPosSubtables.Count * 2;
        for (var index = 0; index < pairPosSubtables.Count; index++) lookup.U16((ushort)(extensionsStart + index * 8));
        var extensionTargets = extensionsStart + pairPosSubtables.Count * 8;
        var targetOffset = extensionTargets;
        for (var index = 0; index < pairPosSubtables.Count; index++)
        {
            var extensionStart = extensionsStart + index * 8;
            lookup.U16(1);
            lookup.U16(PairAdjustmentLookup);
            lookup.U32((uint)(targetOffset - extensionStart));
            targetOffset += pairPosSubtables[index].Length;
        }
        foreach (var subtable in pairPosSubtables) lookup.Bytes(subtable);

        var scriptList = new BigEndianBuffer();
        var scriptTags = new[] { "DFLT", "latn" };
        scriptList.U16((ushort)scriptTags.Length);
        var scriptTableStart = 2 + scriptTags.Length * 6;
        foreach (var tag in scriptTags)
        {
            scriptList.Tag(tag);
            scriptList.U16((ushort)scriptTableStart);
        }
        scriptList.U16(4);
        scriptList.U16(0);
        scriptList.U16(0);
        scriptList.U16(0xFFFF);
        scriptList.U16(1);
        scriptList.U16(0);

        var featureList = new BigEndianBuffer();
        featureList.U16(1);
        featureList.Tag("kern");
        featureList.U16(8);
        featureList.U16(0);
        featureList.U16(1);
        featureList.U16(0);

        var lookupList = new BigEndianBuffer();
        lookupList.U16(1);
        lookupList.U16(4);

        var header = new BigEndianBuffer();
        const int headerSize = 10;
        header.U16(1);
        header.U16(0);
        header.U16(headerSize);
        header.U16((ushort)(headerSize + scriptList.Length));
        header.U16((ushort)(headerSize + scriptList.Length + featureList.Length));
        header.Bytes(scriptList.ToArray());
        header.Bytes(featureList.ToArray());
        header.Bytes(lookupList.ToArray());
        header.Bytes(lookup.ToArray());
        return header.ToArray();
    }

    private static byte[] PairPosition(IReadOnlyList<(ushort First, List<(ushort Second, short Adjustment)> Seconds)> pairSets)
    {
        var headerSize = 10 + pairSets.Count * 2;
        var coverageSize = 4 + pairSets.Count * 2;
        var buffer = new BigEndianBuffer();
        buffer.U16(1);
        buffer.U16((ushort)headerSize);
        buffer.U16(XAdvanceValueFormat);
        buffer.U16(0);
        buffer.U16((ushort)pairSets.Count);
        var pairSetOffset = headerSize + coverageSize;
        foreach (var pairSet in pairSets)
        {
            buffer.U16((ushort)pairSetOffset);
            pairSetOffset += 2 + pairSet.Seconds.Count * 4;
        }
        buffer.U16(1);
        buffer.U16((ushort)pairSets.Count);
        foreach (var pairSet in pairSets) buffer.U16(pairSet.First);
        foreach (var pairSet in pairSets)
        {
            buffer.U16((ushort)pairSet.Seconds.Count);
            foreach (var (second, adjustment) in pairSet.Seconds)
            {
                buffer.U16(second);
                buffer.I16(adjustment);
            }
        }
        return buffer.ToArray();
    }

    private static byte[] Assemble(SortedDictionary<string, byte[]> tables)
    {
        var tableCount = tables.Count;
        var searchRange = 16 * (1 << (int)Math.Floor(Math.Log(tableCount, 2)));
        var font = new BigEndianBuffer();
        font.U32(TrueTypeSfntVersion);
        font.U16((ushort)tableCount);
        font.U16((ushort)searchRange);
        font.U16((ushort)Math.Log(searchRange / 16, 2));
        font.U16((ushort)(tableCount * 16 - searchRange));

        var offset = 12 + tableCount * 16;
        var headOffset = 0;
        foreach (var (tag, table) in tables)
        {
            if (tag == "head") headOffset = offset;
            font.Tag(tag);
            font.U32(Checksum(table));
            font.U32((uint)offset);
            font.U32((uint)table.Length);
            offset += Padded(table.Length);
        }
        foreach (var table in tables.Values)
        {
            font.Bytes(table);
            font.Bytes(new byte[Padded(table.Length) - table.Length]);
        }

        var bytes = font.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(headOffset + 8), ChecksumMagic - Checksum(bytes));
        return bytes;
    }

    private static int MaximumAdvance(Gw2BakedFont font) =>
        Math.Max(font.MissingCharacterAdvance, font.Glyphs.Select(glyph => glyph.Advance).DefaultIfEmpty(0).Max());

    private static int MaximumInkRight(Gw2BakedFont font) =>
        font.Glyphs.Select(glyph => glyph.BearingX + glyph.Source.Width).DefaultIfEmpty(0).Max();

    private static int Padded(int length) => (length + 3) & ~3;

    private static uint Checksum(byte[] table)
    {
        uint sum = 0;
        for (var index = 0; index < table.Length; index += 4)
        {
            uint word = 0;
            for (var byteIndex = 0; byteIndex < 4; byteIndex++)
                word = (word << 8) | (index + byteIndex < table.Length ? table[index + byteIndex] : 0u);
            sum = unchecked(sum + word);
        }
        return sum;
    }

    private sealed class BigEndianBuffer
    {
        private readonly MemoryStream _stream = new();

        public int Length => (int)_stream.Length;

        public void U8(byte value) => _stream.WriteByte(value);
        public void I8(sbyte value) => _stream.WriteByte((byte)value);
        public void U16(ushort value) { U8((byte)(value >> 8)); U8((byte)value); }
        public void I16(short value) => U16((ushort)value);
        public void U16(int value) => U16((ushort)value);
        public void I16(int value) => I16((short)value);
        public void U32(uint value) { U16((ushort)(value >> 16)); U16((ushort)value); }
        public void U64(ulong value) { U32((uint)(value >> 32)); U32((uint)value); }
        public void Tag(string tag) => Bytes(Encoding.ASCII.GetBytes(tag));
        public void Bytes(byte[] bytes) => _stream.Write(bytes, 0, bytes.Length);
        public byte[] ToArray() => _stream.ToArray();
    }
}
