using Gw2.Formats;
using System.Numerics;

namespace Gw2.Dat.Content;

public sealed record Gw2MapRiverProperty(uint Type, ulong Value, string StringValue);

public sealed record Gw2MapRiverReach(IReadOnlyList<Gw2MapRiverProperty> Properties);

public sealed record Gw2MapRiver(
    ulong Guid,
    string Name,
    IReadOnlyList<Gw2MapRiverProperty> Properties,
    IReadOnlyList<Vector3> Points,
    IReadOnlyList<Gw2MapRiverReach> Reaches);

public sealed class Gw2MapRivers
{
    public Gw2MapRivers(IReadOnlyList<Gw2MapRiver> rivers)
    {
        Rivers = rivers;
    }

    public IReadOnlyList<Gw2MapRiver> Rivers { get; }

    public static Gw2MapRivers? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.FindRoot("PackMapRivers");
        if (root is null) return null;

        var rivers = new List<Gw2MapRiver>();
        if (root.Fields.GetValueOrDefault("rivers") is object?[] riverObjects)
            foreach (var riverObject in riverObjects)
                if (riverObject is PackObject river)
                    rivers.Add(new(
                        PackValue.AsU64(river.Fields.GetValueOrDefault("guid")),
                        river.Fields.GetValueOrDefault("name") as string ?? string.Empty,
                        Properties(river.Fields.GetValueOrDefault("properties")),
                        PackValue.AsVec3List(river.Fields.GetValueOrDefault("points")),
                        Reaches(river.Fields.GetValueOrDefault("reaches"))));

        return new(rivers);
    }

    private static IReadOnlyList<Gw2MapRiverProperty> Properties(object? value)
    {
        if (value is not object?[] values) return [];
        var result = new List<Gw2MapRiverProperty>(values.Length);
        foreach (var item in values)
            if (item is PackObject property)
                result.Add(new(
                    PackValue.AsU32(property.Fields.GetValueOrDefault("type")),
                    PackValue.AsU64(property.Fields.GetValueOrDefault("val")),
                    property.Fields.GetValueOrDefault("strVal") as string ?? string.Empty));
        return result;
    }

    private static IReadOnlyList<Gw2MapRiverReach> Reaches(object? value)
    {
        if (value is not object?[] values) return [];
        var result = new List<Gw2MapRiverReach>(values.Length);
        foreach (var item in values)
            if (item is PackObject reach)
                result.Add(new(Properties(reach.Fields.GetValueOrDefault("properties"))));
        return result;
    }
}
