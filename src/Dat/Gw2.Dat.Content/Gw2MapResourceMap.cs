using Gw2.Formats;
using System.Numerics;

namespace Gw2.Dat.Content;

public sealed record Gw2ResourceMapItem(string Filename, uint Type, ulong Permutation);

public sealed record Gw2ResourceMapNode(Vector3 Position, uint Flags, IReadOnlyList<Gw2ResourceMapItem> Items);

public sealed class Gw2MapResourceMap
{
    public Gw2MapResourceMap(IReadOnlyList<Gw2ResourceMapNode> nodes)
    {
        Nodes = nodes;
    }

    public IReadOnlyList<Gw2ResourceMapNode> Nodes { get; }

    public static Gw2MapResourceMap? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.FindRoot("PackMapResourceMap");
        if (root is null) return null;

        var nodes = new List<Gw2ResourceMapNode>();
        if (root.Fields.GetValueOrDefault("nodeArray") is object?[] nodeObjects)
            foreach (var nodeObject in nodeObjects)
                if (nodeObject is PackObject node)
                    nodes.Add(new(
                        Vec3(node, "position"),
                        PackValue.AsU32(node.Fields.GetValueOrDefault("flags")),
                        Items(node.Fields.GetValueOrDefault("itemArray"))));

        return new(nodes);
    }

    private static IReadOnlyList<Gw2ResourceMapItem> Items(object? value)
    {
        if (value is not object?[] values) return [];
        var result = new List<Gw2ResourceMapItem>(values.Length);
        foreach (var item in values)
            if (item is PackObject entry)
                result.Add(new(
                    entry.Fields.GetValueOrDefault("filename") as string ?? string.Empty,
                    PackValue.AsU32(entry.Fields.GetValueOrDefault("type")),
                    PackValue.AsU64(entry.Fields.GetValueOrDefault("permutation"))));
        return result;
    }

    private static Vector3 Vec3(PackObject obj, string name) =>
        obj.Fields.GetValueOrDefault(name) is float[] values && values.Length >= 3
            ? new Vector3(values[0], values[1], values[2])
            : Vector3.Zero;
}
