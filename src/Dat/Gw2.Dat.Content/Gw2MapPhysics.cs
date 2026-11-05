using Gw2.Formats;
using System.Numerics;

namespace Gw2.Dat.Content;

public sealed record Gw2PhysicsGeometryObjectRef(ulong Sequence, uint ObjectIndex);

public sealed record Gw2PhysicsGeometry(
    string FilePath,
    IReadOnlyList<Gw2PhysicsGeometryObjectRef> ObjectRefs,
    IReadOnlyList<ushort> Surfaces);

public sealed record Gw2PhysicsObject(byte[] SceneFile);

public sealed record Gw2PhysicsPlacement(
    ulong Token,
    uint GeometryIndex,
    Vector3 Translate,
    Quaternion Rotation,
    float Scale);

public sealed record Gw2PhysicsBlock(
    Vector3 BoundsMin,
    Vector3 BoundsMax,
    IReadOnlyList<Gw2PhysicsObject> Objects,
    IReadOnlyList<Gw2PhysicsGeometry> Geometries,
    IReadOnlyList<Gw2PhysicsPlacement> Props,
    IReadOnlyList<Gw2PhysicsPlacement> Debris,
    IReadOnlyList<Gw2PhysicsPlacement> Zones,
    IReadOnlyList<Gw2PhysicsPlacement> Obstacles);

public sealed class Gw2MapPhysics
{
    public Gw2MapPhysics(Vector3 boundsMin, Vector3 boundsMax, IReadOnlyList<Gw2PhysicsBlock> blocks)
    {
        BoundsMin = boundsMin;
        BoundsMax = boundsMax;
        Blocks = blocks;
    }

    public Vector3 BoundsMin { get; }
    public Vector3 BoundsMax { get; }
    public IReadOnlyList<Gw2PhysicsBlock> Blocks { get; }

    public static Gw2MapPhysics? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.FindRoot("PackMapPhysics");
        if (root is null) return null;

        var blocks = new List<Gw2PhysicsBlock>();
        if (root.Fields.GetValueOrDefault("blockArray") is object?[] blockObjects)
            foreach (var blockObject in blockObjects)
                if (blockObject is PackObject block)
                    blocks.Add(new(
                        Vec3(block, "boundsMin"),
                        Vec3(block, "boundsMax"),
                        Objects(block.Fields.GetValueOrDefault("objectArray")),
                        Geometries(block.Fields.GetValueOrDefault("geometryArray")),
                        Placements(block.Fields.GetValueOrDefault("propModelArray")),
                        Placements(block.Fields.GetValueOrDefault("debrisModelArray")),
                        Placements(block.Fields.GetValueOrDefault("zoneModelArray")),
                        Placements(block.Fields.GetValueOrDefault("obsModelArray"))));

        return new(Vec3(root, "boundsMin"), Vec3(root, "boundsMax"), blocks);
    }

    private static IReadOnlyList<Gw2PhysicsObject> Objects(object? value)
    {
        if (value is not object?[] values) return [];
        var result = new List<Gw2PhysicsObject>(values.Length);
        foreach (var item in values)
            if (item is PackObject obj)
                result.Add(new(PackValue.AsBytes(obj.Fields.GetValueOrDefault("sceneFilePtr"))));
        return result;
    }

    private static IReadOnlyList<Gw2PhysicsGeometry> Geometries(object? value)
    {
        if (value is not object?[] values) return [];
        var result = new List<Gw2PhysicsGeometry>(values.Length);
        foreach (var item in values)
            if (item is PackObject geometry)
                result.Add(new(
                    geometry.Fields.GetValueOrDefault("filePath") as string ?? string.Empty,
                    ObjectRefs(geometry.Fields.GetValueOrDefault("objRefArray")),
                    PackValue.AsU16List(geometry.Fields.GetValueOrDefault("surface"))));
        return result;
    }

    private static IReadOnlyList<Gw2PhysicsGeometryObjectRef> ObjectRefs(object? value)
    {
        if (value is not object?[] values) return [];
        var result = new List<Gw2PhysicsGeometryObjectRef>(values.Length);
        foreach (var item in values)
            if (item is PackObject reference)
                result.Add(new(
                    PackValue.AsU64(reference.Fields.GetValueOrDefault("sequence")),
                    PackValue.AsU32(reference.Fields.GetValueOrDefault("objectIndex"))));
        return result;
    }

    private static IReadOnlyList<Gw2PhysicsPlacement> Placements(object? value)
    {
        if (value is not object?[] values) return [];
        var result = new List<Gw2PhysicsPlacement>(values.Length);
        foreach (var item in values)
            if (item is PackObject placement)
                result.Add(new(
                    PackValue.AsU64(placement.Fields.GetValueOrDefault("token")),
                    PackValue.AsU32(placement.Fields.GetValueOrDefault("geometryIndex")),
                    Vec3(placement, "translate"),
                    Quat(placement, "rotate"),
                    PackValue.AsF32(placement.Fields.GetValueOrDefault("scale"))));
        return result;
    }

    private static Vector3 Vec3(PackObject obj, string name) =>
        obj.Fields.GetValueOrDefault(name) is float[] values && values.Length >= 3
            ? new Vector3(values[0], values[1], values[2])
            : Vector3.Zero;

    private static Quaternion Quat(PackObject obj, string name) =>
        obj.Fields.GetValueOrDefault(name) is float[] values && values.Length >= 4
            ? new Quaternion(values[0], values[1], values[2], values[3])
            : Quaternion.Identity;
}
