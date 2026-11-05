using System.Buffers.Binary;

namespace Gw2.Dat.Content;

public readonly record struct Gw2FontCodePointRange(int First, int Terminator)
{
    public int Count => Terminator - First;
}

public static class Gw2FontCodePointRanges
{
    public static readonly IReadOnlyList<Gw2FontCodePointRange> All =
    [
        new(0x0021, 0x007F),
        new(0x00A1, 0x00FF),
        new(0x0100, 0x0180),
        new(0x0391, 0x0460),
        new(0x2010, 0x266B),
        new(0x3000, 0x3020),
        new(0x3041, 0x3100),
        new(0x3105, 0x312A),
        new(0x3131, 0x318F),
        new(0xAC00, 0xD7A4),
        new(0x4E00, 0x9FA6),
        new(0xF900, 0xFA6B),
        new(0xFF01, 0xFFE7),
    ];
}

public sealed record Gw2FontManifestEntry(
    byte FontIndex,
    byte Variant,
    ulong SizeKey,
    uint Id,
    byte ExtentX,
    byte CellHeight,
    IReadOnlyList<uint?> RangeFileIds);

public sealed record Gw2FontManifestFile(IReadOnlyList<Gw2FontManifestEntry> Entries);

public static class Gw2FontManifest
{
    public const string PackType = "AFNT";
    public const uint ManifestFileId = 0x25D41;
    private const int RangeCount = 13;
    private const int RangeReferencesOffset = 0x10;

    public static Gw2FontManifestFile? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.Pack.Type == PackType ? new(Parse(document.Pack)) : null;
    }

    public static IReadOnlyList<Gw2FontManifestEntry> Parse(ReadOnlyMemory<byte> data) => Parse(Gw2PackFile.Parse(data));

    private static IReadOnlyList<Gw2FontManifestEntry> Parse(Gw2PackFile pack)
    {
        if (pack.Type != PackType)
            throw new InvalidDataException($"Expected an {PackType} pack, found {pack.Type}");
        var chunk = pack.Chunks.FirstOrDefault(candidate => candidate.Type == PackType)
            ?? throw new InvalidDataException($"The pack has no {PackType} chunk");

        var bytes = chunk.Bytes.Span;
        var pointerSize = pack.PointerSize;
        var countOffset = chunk.HeaderSize;
        var count = BinaryPrimitives.ReadUInt32LittleEndian(bytes[countOffset..]);
        var entriesOffset = ResolveRelativePointer(bytes, countOffset + 4, pointerSize)
            ?? throw new InvalidDataException("The font entry array pointer is null");
        var entrySize = RangeReferencesOffset + RangeCount * pointerSize;

        var entries = new List<Gw2FontManifestEntry>((int)count);
        for (var index = 0; index < count; index++)
        {
            var entryOffset = entriesOffset + index * entrySize;
            var entry = bytes.Slice(entryOffset, entrySize);
            var rangeFileIds = new uint?[RangeCount];
            for (var range = 0; range < RangeCount; range++)
            {
                var referenceOffset = ResolveRelativePointer(bytes, entryOffset + RangeReferencesOffset + range * pointerSize, pointerSize);
                rangeFileIds[range] = referenceOffset is { } offset ? ReadFileReference(bytes[offset..]) : null;
            }
            entries.Add(new(
                entry[0],
                entry[1],
                BinaryPrimitives.ReadUInt64LittleEndian(entry[2..]),
                BinaryPrimitives.ReadUInt32LittleEndian(entry[10..]),
                entry[14],
                entry[15],
                rangeFileIds));
        }
        return entries;
    }

    public static uint ReadFileReference(ReadOnlySpan<byte> reference)
    {
        var low = BinaryPrimitives.ReadUInt16LittleEndian(reference);
        var high = BinaryPrimitives.ReadUInt16LittleEndian(reference[2..]);
        if (low < 0x100 || high < 0x100)
            throw new InvalidDataException($"Invalid file reference {low:X4} {high:X4}");
        return 0xFF00u * (uint)(high - 0x100) + (uint)(low - 0x100) + 1;
    }

    private static int? ResolveRelativePointer(ReadOnlySpan<byte> bytes, int fieldOffset, int pointerSize)
    {
        var relative = pointerSize == 8
            ? BinaryPrimitives.ReadInt64LittleEndian(bytes[fieldOffset..])
            : BinaryPrimitives.ReadInt32LittleEndian(bytes[fieldOffset..]);
        if (relative == 0) return null;
        var target = fieldOffset + relative;
        if (target < 0 || target >= bytes.Length)
            throw new InvalidDataException($"Relative pointer at {fieldOffset} leaves the chunk");
        return (int)target;
    }
}

public sealed record Gw2FontGlyph(int CodePoint, int TopOffset, int Width, int Height, byte[] Coverage);

public static class Gw2FontGlyphStream
{
    private const byte AlternatingSpansStartingOpaque = 0x00;
    private const byte AlternatingSpansStartingTransparent = 0xFF;
    private const byte CoverageWithSpans = 0x01;

    public static IReadOnlyList<Gw2FontGlyph> Decode(ReadOnlySpan<byte> rangeFile, Gw2FontCodePointRange range)
    {
        var glyphs = new List<Gw2FontGlyph>();
        var position = 0;
        var codePoint = range.First;
        while (position < rangeFile.Length)
        {
            if (codePoint >= range.Terminator)
                throw new InvalidDataException($"The range file holds more than {range.Count} glyphs");
            if (rangeFile.Length - position < 4)
                throw new InvalidDataException($"Truncated glyph header at {position}");
            var topOffset = rangeFile[position];
            var width = rangeFile[position + 1] + 1;
            var height = rangeFile[position + 2] + 1;
            var rleType = rangeFile[position + 3];
            position += 4;
            var coverage = new byte[width * height];
            switch (rleType)
            {
                case CoverageWithSpans:
                    DecodeCoverageWithSpans(rangeFile, ref position, coverage);
                    break;
                case AlternatingSpansStartingOpaque:
                case AlternatingSpansStartingTransparent:
                    DecodeAlternatingSpans(rangeFile, ref position, coverage, (byte)~rleType);
                    break;
                default:
                    throw new InvalidDataException($"Unknown glyph RLE type 0x{rleType:X2} at {position - 4}");
            }
            glyphs.Add(new(codePoint, topOffset, width, height, coverage));
            codePoint++;
        }
        return glyphs;
    }

    private static void DecodeCoverageWithSpans(ReadOnlySpan<byte> rangeFile, ref int position, byte[] coverage)
    {
        var filled = 0;
        while (filled < coverage.Length)
        {
            var value = ReadByte(rangeFile, ref position);
            var length = value is 0x00 or 0xFF ? ReadSpanLength(rangeFile, ref position) : 1;
            Fill(coverage, ref filled, length, value);
        }
    }

    private static void DecodeAlternatingSpans(ReadOnlySpan<byte> rangeFile, ref int position, byte[] coverage, byte firstValue)
    {
        var filled = 0;
        var value = firstValue;
        while (filled < coverage.Length)
        {
            Fill(coverage, ref filled, ReadSpanLength(rangeFile, ref position), value);
            value = (byte)~value;
        }
    }

    private static int ReadSpanLength(ReadOnlySpan<byte> rangeFile, ref int position)
    {
        var first = ReadByte(rangeFile, ref position);
        if (first < 0xFF) return first + 1;
        var length = 256;
        byte next;
        do
        {
            next = ReadByte(rangeFile, ref position);
            length += next;
        }
        while (next == 0xFF);
        return length;
    }

    private static void Fill(byte[] coverage, ref int filled, int length, byte value)
    {
        if (filled + length > coverage.Length)
            throw new InvalidDataException("A coverage span runs past the glyph");
        coverage.AsSpan(filled, length).Fill(value);
        filled += length;
    }

    private static byte ReadByte(ReadOnlySpan<byte> rangeFile, ref int position)
    {
        if (position >= rangeFile.Length)
            throw new InvalidDataException("Truncated glyph coverage");
        return rangeFile[position++];
    }
}

public sealed record Gw2FontGlyphProfile(uint[] BandInkLeft, uint[] BandInkWidth, bool IsLetter);

public sealed class Gw2FontMetrics
{
    public const int BandCount = 4;
    public const byte InkThreshold = 0x7F;

    public Gw2FontMetrics(int cellHeight)
    {
        if (cellHeight <= 0) throw new ArgumentOutOfRangeException(nameof(cellHeight));
        CellHeight = cellHeight;
        LetterSpacing = (cellHeight + 23) / 24;
        MissingCharacterAdvance = (cellHeight + 3) / 4;
    }

    public int CellHeight { get; }
    public int LetterSpacing { get; }
    public int MissingCharacterAdvance { get; }

    public static bool IsLetter(int codePoint) =>
        codePoint is >= 0x41 and <= 0x17F and not (>= 0x5B and <= 0x60) and not (>= 0x7B and <= 0xBF);

    public Gw2FontGlyphProfile BuildProfile(Gw2FontGlyph glyph)
    {
        if (!IsLetter(glyph.CodePoint))
        {
            var width = (uint)glyph.Width;
            return new(new uint[BandCount], [width, width, width, width], false);
        }

        var left = new uint[BandCount];
        var right = new uint[BandCount];
        Array.Fill(left, uint.MaxValue);
        var inkBottom = glyph.TopOffset + glyph.Height;
        var spaceBelow = inkBottom <= CellHeight ? CellHeight - inkBottom : 0;
        var bandDivisor = inkBottom + spaceBelow;
        for (var row = 0; row < glyph.Height; row++)
        {
            var band = (glyph.TopOffset + row) * BandCount / bandDivisor;
            for (var column = 0; column < glyph.Width; column++)
            {
                if (glyph.Coverage[row * glyph.Width + column] < InkThreshold) continue;
                left[band] = Math.Min(left[band], (uint)column);
                right[band] = Math.Max(right[band], (uint)column);
            }
        }

        var inkWidth = new uint[BandCount];
        for (var band = 0; band < BandCount; band++)
            inkWidth[band] = left[band] <= right[band] ? right[band] - left[band] + 1 : 0;
        return new(left, inkWidth, true);
    }
}

public sealed class Gw2FontPen
{
    private readonly Gw2FontMetrics _metrics;
    private readonly int _letterSpacing;
    private readonly uint[] _bandRightEdge = new uint[Gw2FontMetrics.BandCount];
    private uint _cursor;
    private bool _previousIsLetter = true;

    public Gw2FontPen(Gw2FontMetrics metrics, bool widenedSpacing = false)
    {
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _letterSpacing = metrics.LetterSpacing + (widenedSpacing ? 1 : 0);
    }

    public uint Extent => Math.Max(_cursor, _bandRightEdge.Max());

    public void AdvanceMissingCharacter()
    {
        var edge = Extent + (uint)_metrics.MissingCharacterAdvance;
        _cursor = edge;
        Array.Fill(_bandRightEdge, edge);
        _previousIsLetter = true;
    }

    public uint Place(int codePoint, Gw2FontGlyphProfile profile)
    {
        var gap = profile.IsLetter && !_previousIsLetter ? (uint)_letterSpacing : 0;
        var x = PlaceGlyph(profile, gap);
        var step = codePoint < 0x180 && !profile.IsLetter ? 0u : (uint)_letterSpacing;
        AdvancePastGlyph(profile, step);
        _previousIsLetter = profile.IsLetter;
        return x;
    }

    private uint PlaceGlyph(Gw2FontGlyphProfile profile, uint gap)
    {
        if (!profile.IsLetter && !_previousIsLetter)
        {
            _cursor = Math.Max(_cursor, _bandRightEdge[0]) + gap;
            return _cursor;
        }

        uint largestInkRight = 0;
        for (var band = 0; band < Gw2FontMetrics.BandCount; band++)
        {
            if (profile.BandInkWidth[band] == 0) continue;
            var edge = _bandRightEdge[band];
            var left = profile.BandInkLeft[band];
            var touching = left <= edge ? edge - left : edge;
            _cursor = Math.Max(_cursor, touching);
            largestInkRight = Math.Max(largestInkRight, left + profile.BandInkWidth[band]);
        }

        _cursor += gap;
        var overlapLimit = largestInkRight / 3;
        foreach (var edge in _bandRightEdge)
            if (overlapLimit <= edge)
                _cursor = Math.Max(_cursor, edge - overlapLimit);
        return _cursor;
    }

    private void AdvancePastGlyph(Gw2FontGlyphProfile profile, uint step)
    {
        _cursor += step;
        if (!profile.IsLetter)
        {
            var edge = Math.Max(_bandRightEdge[0] + step, _cursor + profile.BandInkWidth[0]);
            Array.Fill(_bandRightEdge, edge);
            return;
        }

        for (var band = 0; band < Gw2FontMetrics.BandCount; band++)
        {
            var inkRight = profile.BandInkWidth[band] == 0 ? 0 : profile.BandInkLeft[band] + profile.BandInkWidth[band];
            _bandRightEdge[band] = Math.Max(_bandRightEdge[band] + step, _cursor + inkRight);
        }
    }
}
