using System.IO;
using Gw2.Dat.Content;

namespace Gw2.Client.Model;

public readonly record struct Gw2PlacedGlyph(Gw2FontGlyph Glyph, int X);

public sealed record Gw2TextLine(IReadOnlyList<Gw2PlacedGlyph> Glyphs, int Width);

public sealed class Gw2FontFace
{
    public const int LatinRangeCount = 3;

    private readonly Dictionary<int, Gw2FontGlyph> _glyphs;
    private readonly Dictionary<int, Gw2FontGlyphProfile> _profiles;

    public Gw2FontFace(int cellHeight, IEnumerable<Gw2FontGlyph> glyphs)
    {
        Metrics = new Gw2FontMetrics(cellHeight);
        _glyphs = glyphs.ToDictionary(glyph => glyph.CodePoint);
        _profiles = _glyphs.ToDictionary(pair => pair.Key, pair => Metrics.BuildProfile(pair.Value));
    }

    public Gw2FontMetrics Metrics { get; }
    public int CellHeight => Metrics.CellHeight;
    public IReadOnlyCollection<Gw2FontGlyph> Glyphs => _glyphs.Values;

    public bool TryGetGlyph(int codePoint, out Gw2FontGlyph glyph, out Gw2FontGlyphProfile profile)
    {
        if (_glyphs.TryGetValue(codePoint, out glyph!))
        {
            profile = _profiles[codePoint];
            return true;
        }
        profile = null!;
        return false;
    }

    public Gw2TextLine Layout(string text)
    {
        var pen = new Gw2FontPen(Metrics);
        var placed = new List<Gw2PlacedGlyph>(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            if (!_glyphs.TryGetValue(rune.Value, out var glyph))
            {
                pen.AdvanceMissingCharacter();
                continue;
            }
            placed.Add(new Gw2PlacedGlyph(glyph, (int)pen.Place(rune.Value, _profiles[rune.Value])));
        }

        return new Gw2TextLine(placed, (int)pen.Extent);
    }

    public static Gw2FontManifestEntry? FindEntry(IEnumerable<Gw2FontManifestEntry> manifest, uint typeface, uint variant, ulong sizeKey) =>
        manifest.FirstOrDefault(entry => entry.FontIndex == typeface && entry.Variant == variant && entry.SizeKey == sizeKey);

    public static Gw2FontFace Load(Gw2ContentReader reader, Gw2FontManifestEntry entry)
    {
        var glyphs = new List<Gw2FontGlyph>();
        for (var range = 0; range < LatinRangeCount; range++)
        {
            if (entry.RangeFileIds[range] is not { } fileId || !reader.Contains(fileId)) continue;
            try
            {
                glyphs.AddRange(Gw2FontGlyphStream.Decode(reader.ReadBytes(fileId), Gw2FontCodePointRanges.All[range]));
            }
            catch (InvalidDataException)
            {
            }
        }

        return new Gw2FontFace(entry.CellHeight, glyphs);
    }
}
