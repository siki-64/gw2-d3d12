using Gw2.Formats;
using System.Numerics;

namespace Gw2.Dat.Content;

public sealed record Gw2MapAreaPolygon(IReadOnlyList<Vector3> Points, float Height);

public sealed record Gw2MapAreaPortal(Vector3 Position, Vector3 Extents, Vector3 Rotation);

public sealed record Gw2MapAreaVolume(
    IReadOnlyList<Gw2MapAreaPortal> Portals,
    Vector3 Position,
    Vector3 Extents,
    Vector3 PointInterior,
    Vector3 PointExterior);

public sealed record Gw2MapArea(ulong Token, uint Type, uint Floor, uint Flags)
{
    public Gw2MapAreaPolygon? Polygon { get; init; }
    public Gw2MapAreaVolume? Volume { get; init; }
}

public sealed record Gw2MapAreaTool(string Annotation, float RenderOffset);

public sealed class Gw2MapAreas
{
    public Gw2MapAreas(IReadOnlyList<Gw2MapArea> areas, IReadOnlyList<Gw2MapAreaTool> tools)
    {
        Areas = areas;
        Tools = tools;
    }

    public IReadOnlyList<Gw2MapArea> Areas { get; }
    public IReadOnlyList<Gw2MapAreaTool> Tools { get; }

    public static Gw2MapAreas? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.FindRoot("PackMapAreas");
        if (root is null) return null;

        var areas = new List<Gw2MapArea>();
        if (root.Fields.GetValueOrDefault("areas") is object?[] areaObjects)
            foreach (var areaObject in areaObjects)
                if (areaObject is PackObject area)
                    areas.Add(new(
                        PackValue.AsU64(area.Fields.GetValueOrDefault("token")),
                        PackValue.AsU32(area.Fields.GetValueOrDefault("type")),
                        PackValue.AsU32(area.Fields.GetValueOrDefault("floor")),
                        PackValue.AsU32(area.Fields.GetValueOrDefault("flags")))
                    {
                        Polygon = area.Optional("polygon") is PackObject polygon ? ParsePolygon(polygon) : null,
                        Volume = area.Optional("volume") is PackObject volume ? ParseVolume(volume) : null,
                    });

        var tools = new List<Gw2MapAreaTool>();
        if (root.Fields.GetValueOrDefault("areaTools") is object?[] toolObjects)
            foreach (var toolObject in toolObjects)
                if (toolObject is PackObject tool)
                    tools.Add(new(
                        tool.Fields.GetValueOrDefault("annotation") as string ?? string.Empty,
                        PackValue.AsF32(tool.Fields.GetValueOrDefault("renderOffset"))));

        return new(areas, tools);
    }

    private static Gw2MapAreaPolygon ParsePolygon(PackObject polygon) => new(
        PackValue.AsVec3List(polygon.Optional("points")),
        PackValue.AsF32(polygon.Optional("height")));

    private static Gw2MapAreaVolume ParseVolume(PackObject volume) => new(
        PackValue.AsObjects(volume.Optional("portals"))
            .Select(portal => new Gw2MapAreaPortal(
                Vector(portal.Optional("position")),
                Vector(portal.Optional("extents")),
                Vector(portal.Optional("rotation"))))
            .ToArray(),
        Vector(volume.Optional("position")),
        Vector(volume.Optional("extents")),
        Vector(volume.Optional("pointInterior")),
        Vector(volume.Optional("pointExterior")));

    private static Vector3 Vector(object? value) =>
        value is float[] { Length: >= 3 } values ? new Vector3(values[0], values[1], values[2]) : Vector3.Zero;
}
