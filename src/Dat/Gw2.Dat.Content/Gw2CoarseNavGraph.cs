using Gw2.Formats;
using System.Numerics;

namespace Gw2.Dat.Content;

public sealed record Gw2CoarseNavNode(uint MaterialId, Vector3 BottomLeftBound, Vector3 TopRightBound, Vector3 Centroid);

public sealed record Gw2CoarseNavSection(uint SectionUid, IReadOnlyList<Gw2CoarseNavNode> Nodes);

public sealed class Gw2CoarseNavGraph
{
    public Gw2CoarseNavGraph(IReadOnlyList<Gw2CoarseNavSection> sections)
    {
        Sections = sections;
    }

    public IReadOnlyList<Gw2CoarseNavSection> Sections { get; }

    public static Gw2CoarseNavGraph? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.FindRoot("PackMapCoarseNavGraph");
        if (root is null) return null;

        var sections = new List<Gw2CoarseNavSection>();
        if (root.Fields.GetValueOrDefault("sections") is object?[] sectionObjects)
            foreach (var sectionObject in sectionObjects)
                if (sectionObject is PackObject section)
                    sections.Add(new(
                        PackValue.AsU32(section.Fields.GetValueOrDefault("sectionUid")),
                        Nodes(section.Fields.GetValueOrDefault("nodes"))));

        return new(sections);
    }

    private static IReadOnlyList<Gw2CoarseNavNode> Nodes(object? value)
    {
        if (value is not object?[] values) return [];
        var result = new List<Gw2CoarseNavNode>(values.Length);
        foreach (var item in values)
            if (item is PackObject node)
                result.Add(new(
                    PackValue.AsU32(node.Fields.GetValueOrDefault("materialId")),
                    Vec3(node, "bottomLeftBound"),
                    Vec3(node, "topRightBound"),
                    Vec3(node, "centroid")));
        return result;
    }

    private static Vector3 Vec3(PackObject obj, string name) =>
        obj.Fields.GetValueOrDefault(name) is float[] values && values.Length >= 3
            ? new Vector3(values[0], values[1], values[2])
            : Vector3.Zero;
}
