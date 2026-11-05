using Gw2.Dat.Content;
using Gw2.Dat.Archive;

namespace Gw2.Client.Model;

public static class Gw2FontArchive
{
    public const uint ManifestFileId = Gw2FontManifest.ManifestFileId;
    public const int SpaceCodePoint = 0x20;
    public const int LatinLayoutTerminator = 0x180;

    public static readonly IReadOnlyCollection<int> SharedRangeIndices = [9, 10];
}

public sealed record Gw2BakedGlyph(Gw2FontGlyph Source, int BearingX, int BearingY, int Advance)
{
    public int CodePoint => Source.CodePoint;
}

public readonly record struct Gw2FontPairAdjustment(int FirstCodePoint, int SecondCodePoint, int AdvanceAdjustment);

public sealed record Gw2BakedFont(
    int CellHeight,
    int Baseline,
    int MissingCharacterAdvance,
    IReadOnlyList<Gw2BakedGlyph> Glyphs,
    IReadOnlyList<Gw2FontPairAdjustment> PairAdjustments);

public static class Gw2FontBaker
{
    private const int BaselineReferenceCodePoint = 'H';

    public static int BaselineOf(IEnumerable<Gw2FontGlyph> glyphs, int cellHeight)
    {
        var reference = glyphs.FirstOrDefault(glyph => glyph.CodePoint == BaselineReferenceCodePoint);
        return reference is null ? cellHeight : Math.Min(cellHeight, reference.TopOffset + reference.Height);
    }

    public static Gw2BakedFont Bake(IReadOnlyList<Gw2FontGlyph> glyphs, Gw2FontMetrics metrics, int baseline, bool includeSpace)
    {
        var profiles = glyphs.ToDictionary(glyph => glyph.CodePoint, metrics.BuildProfile);
        var bearings = profiles.ToDictionary(pair => pair.Key, pair => BearingXOf(pair.Value));
        var latin = glyphs.Where(glyph => glyph.CodePoint < Gw2FontArchive.LatinLayoutTerminator).Select(glyph => glyph.CodePoint).ToArray();
        var advances = new Dictionary<int, int>(glyphs.Count);
        var pairAdjustments = new List<Gw2FontPairAdjustment>();

        foreach (var glyph in glyphs)
        {
            var profile = profiles[glyph.CodePoint];
            if (glyph.CodePoint >= Gw2FontArchive.LatinLayoutTerminator || latin.Length == 0)
            {
                advances[glyph.CodePoint] = PairDistance(metrics, glyph.CodePoint, profile, glyph.CodePoint, profile);
                continue;
            }

            var firstBearing = bearings[glyph.CodePoint];
            var distances = latin.ToDictionary(second => second,
                second => PairDistance(metrics, glyph.CodePoint, profile, second, profiles[second]) + firstBearing - bearings[second]);
            var advance = distances.Values.GroupBy(distance => distance).OrderByDescending(group => group.Count()).ThenBy(group => group.Key).First().Key;
            advances[glyph.CodePoint] = advance;
            foreach (var (second, distance) in distances)
                if (distance != advance)
                    pairAdjustments.Add(new(glyph.CodePoint, second, distance - advance));
            if (includeSpace)
            {
                var extent = ExtentAfter(metrics, glyph.CodePoint, profile) + firstBearing;
                if (extent != advance) pairAdjustments.Add(new(glyph.CodePoint, Gw2FontArchive.SpaceCodePoint, extent - advance));
            }
        }

        var baked = glyphs
            .Select(glyph => new Gw2BakedGlyph(glyph, bearings[glyph.CodePoint], baseline - glyph.TopOffset, advances[glyph.CodePoint]))
            .ToList();
        if (includeSpace)
            baked.Add(new(new Gw2FontGlyph(Gw2FontArchive.SpaceCodePoint, 0, 0, 0, []), 0, 0, metrics.MissingCharacterAdvance));
        baked.Sort((left, right) => left.CodePoint.CompareTo(right.CodePoint));
        return new(metrics.CellHeight, baseline, metrics.MissingCharacterAdvance, baked, pairAdjustments);
    }

    public static int BearingXOf(Gw2FontGlyphProfile profile)
    {
        if (!profile.IsLetter) return 0;
        var inkedLefts = profile.BandInkLeft.Where((_, band) => profile.BandInkWidth[band] != 0).ToArray();
        return inkedLefts.Length == 0 ? 0 : -(int)inkedLefts.Min();
    }

    public static int PairDistance(Gw2FontMetrics metrics, int first, Gw2FontGlyphProfile firstProfile, int second, Gw2FontGlyphProfile secondProfile)
    {
        var pen = new Gw2FontPen(metrics);
        var firstX = pen.Place(first, firstProfile);
        return (int)(pen.Place(second, secondProfile) - firstX);
    }

    private static int ExtentAfter(Gw2FontMetrics metrics, int codePoint, Gw2FontGlyphProfile profile)
    {
        var pen = new Gw2FontPen(metrics);
        var x = pen.Place(codePoint, profile);
        return (int)(pen.Extent - x);
    }
}
