using Gw2.Formats;
using System.Numerics;

namespace Gw2.Dat.Content;

public sealed record Gw2CollisionMesh(
    IReadOnlyList<Vector3> Vertices,
    IReadOnlyList<ushort> Indices,
    IReadOnlyList<ushort> Surfaces,
    byte[] MoppData);

public sealed record Gw2CollisionGeometry(byte QuantizedExtents, IReadOnlyList<uint> Animations, ushort NavMeshIndex);

public sealed record Gw2CollisionAnimation(
    ulong Sequence,
    IReadOnlyList<uint> CollisionIndices,
    IReadOnlyList<uint> BlockerIndices);

public sealed record Gw2CollisionPlacement(ulong Token, uint GeometryIndex, Vector3 Translate, Vector3 Rotate, float Scale)
{
    public ulong? Sequence { get; init; }
}

public sealed record Gw2CollisionBlocker(IReadOnlyList<Vector3> Vertices);

public sealed record Gw2CollisionWaterVolume(
    ulong Guid,
    string Name,
    uint Flags,
    float[] VerticalRange,
    IReadOnlyList<Vector2> Vertices);

public sealed class Gw2MapCollision
{
    public Gw2MapCollision(
        Vector3 boundsMin,
        Vector3 boundsMax,
        uint flags,
        float waterSurfaceZ,
        IReadOnlyList<Gw2CollisionMesh> collisions,
        IReadOnlyList<Gw2CollisionGeometry> geometries,
        IReadOnlyList<Gw2CollisionPlacement> propModels,
        IReadOnlyList<Gw2CollisionPlacement> zoneModels,
        IReadOnlyList<Gw2CollisionPlacement> obsModels,
        IReadOnlyList<Gw2CollisionBlocker> blockers,
        IReadOnlyList<Gw2CollisionAnimation> animations,
        int waterVolumeCount,
        IReadOnlyList<Gw2CollisionWaterVolume>? waterVolumes = null)
    {
        BoundsMin = boundsMin;
        BoundsMax = boundsMax;
        Flags = flags;
        WaterSurfaceZ = waterSurfaceZ;
        Collisions = collisions;
        Geometries = geometries;
        PropModels = propModels;
        ZoneModels = zoneModels;
        ObsModels = obsModels;
        Blockers = blockers;
        Animations = animations;
        WaterVolumes = waterVolumes ?? [];
        WaterVolumeCount = waterVolumes?.Count ?? waterVolumeCount;
    }

    public Vector3 BoundsMin { get; }
    public Vector3 BoundsMax { get; }
    public uint Flags { get; }
    public float WaterSurfaceZ { get; }
    public IReadOnlyList<Gw2CollisionMesh> Collisions { get; }
    public IReadOnlyList<Gw2CollisionGeometry> Geometries { get; }
    public IReadOnlyList<Gw2CollisionPlacement> PropModels { get; }
    public IReadOnlyList<Gw2CollisionPlacement> ZoneModels { get; }
    public IReadOnlyList<Gw2CollisionPlacement> ObsModels { get; }
    public IReadOnlyList<Gw2CollisionBlocker> Blockers { get; }
    public IReadOnlyList<Gw2CollisionAnimation> Animations { get; }
    public int AnimationCount => Animations.Count;
    public IReadOnlyList<Gw2CollisionWaterVolume> WaterVolumes { get; }
    public int WaterVolumeCount { get; }

    public bool HasCollision => Collisions.Count != 0 || Blockers.Count != 0;

    public bool TryResolveGeometryCollisions(
        uint geometryIndex,
        out IReadOnlyList<uint> collisionIndices,
        out IReadOnlyList<uint> blockerIndices)
    {
        collisionIndices = [];
        blockerIndices = [];
        if (geometryIndex >= Geometries.Count) return false;

        var collisions = new List<uint>();
        var blockers = new List<uint>();
        var seenCollisions = new HashSet<uint>();
        var seenBlockers = new HashSet<uint>();
        foreach (var animationIndex in Geometries[(int)geometryIndex].Animations)
        {
            if (animationIndex >= Animations.Count) continue;
            foreach (var collision in Animations[(int)animationIndex].CollisionIndices)
                if (collision < Collisions.Count && seenCollisions.Add(collision)) collisions.Add(collision);
            foreach (var blocker in Animations[(int)animationIndex].BlockerIndices)
                if (blocker < Blockers.Count && seenBlockers.Add(blocker)) blockers.Add(blocker);
        }

        collisionIndices = collisions;
        blockerIndices = blockers;
        return true;
    }

    public const ulong DefaultPlacementSequence = 0x2CDF07C8BA;
    public const ulong ZonePlacementSequence = 0x83E502B9FA;

    public static ulong PropPlacementSequence(Gw2CollisionPlacement placement) =>
        placement.Sequence is { } sequence and not 0 ? sequence : DefaultPlacementSequence;

    public bool TryResolveAnimation(uint geometryIndex, ulong sequence, out Gw2CollisionAnimation animation)
    {
        animation = null!;
        if (geometryIndex >= Geometries.Count) return false;
        var animationIndices = Geometries[(int)geometryIndex].Animations;
        foreach (var animationIndex in animationIndices)
            if (animationIndex < Animations.Count && Animations[(int)animationIndex].Sequence == sequence)
            {
                animation = Animations[(int)animationIndex];
                return true;
            }
        if (sequence != 0 || animationIndices.Count == 0 || animationIndices[0] >= Animations.Count) return false;
        animation = Animations[(int)animationIndices[0]];
        return true;
    }

    public IReadOnlyList<IReadOnlyList<Vector3>> ScaledBlockers(uint geometryIndex, ulong sequence, float scale)
    {
        if (!TryResolveAnimation(geometryIndex, sequence, out var animation)) return [];
        var blockers = new List<IReadOnlyList<Vector3>>(animation.BlockerIndices.Count);
        foreach (var blockerIndex in animation.BlockerIndices)
        {
            if (blockerIndex >= Blockers.Count) continue;
            var vertices = Blockers[(int)blockerIndex].Vertices;
            var points = new Vector3[vertices.Count];
            for (var index = 0; index < points.Length; index++)
                points[index] = new Vector3(scale * vertices[index].X, scale * vertices[index].Y, scale * vertices[index].Z);
            blockers.Add(points);
        }
        return blockers;
    }

    public static Gw2MapCollision? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.FindRoot("PackMapCollide");
        if (root is null) return null;

        return new Gw2MapCollision(
            Vec3(root, "boundsMin"),
            Vec3(root, "boundsMax"),
            PackValue.OptionalU32(root, "flags") ?? 0,
            PackValue.OptionalF32(root, "waterSurfaceZ") ?? 0,
            ReadCollisions(root.Fields.GetValueOrDefault("collisions")),
            ReadGeometries(root.Fields.GetValueOrDefault("geometries")),
            ReadPlacements(root.Fields.GetValueOrDefault("propModels")),
            ReadPlacements(root.Fields.GetValueOrDefault("zoneModels")),
            ReadPlacements(root.Fields.GetValueOrDefault("obsModels")),
            ReadBlockers(root.Fields.GetValueOrDefault("blockers")),
            ReadAnimations(root.Fields.GetValueOrDefault("animations")),
            PackValue.ArrayLength(root.Optional("waterVolumes")),
            ReadWaterVolumes(root.Optional("waterVolumes")));
    }

    private static IReadOnlyList<Gw2CollisionMesh> ReadCollisions(object? value)
    {
        if (value is not object?[] collisions) return [];
        var result = new List<Gw2CollisionMesh>(collisions.Length);
        foreach (var collisionObject in collisions)
        {
            if (collisionObject is not PackObject collision) continue;
            var mopp = collision.Fields.GetValueOrDefault("moppCodeData") as PackObject;
            result.Add(new(
                PackValue.AsVec3List(collision.Fields.GetValueOrDefault("vertices")),
                PackValue.AsU16List(collision.Fields.GetValueOrDefault("indices")),
                PackValue.AsU16List(collision.Fields.GetValueOrDefault("surfaces")),
                Blob(mopp?.Fields.GetValueOrDefault("moppData"))));
        }
        return result;
    }

    private static IReadOnlyList<Gw2CollisionGeometry> ReadGeometries(object? value)
    {
        if (value is not object?[] geometries) return [];
        var result = new List<Gw2CollisionGeometry>(geometries.Length);
        foreach (var geometryObject in geometries)
        {
            if (geometryObject is not PackObject geometry) continue;
            result.Add(new(
                AsByte(geometry.Fields.GetValueOrDefault("quantizedExtents")),
                PackValue.AsU32List(geometry.Fields.GetValueOrDefault("animations")),
                (ushort)PackValue.AsU32(geometry.Fields.GetValueOrDefault("navMeshIndex"))));
        }
        return result;
    }

    private static IReadOnlyList<Gw2CollisionPlacement> ReadPlacements(object? value)
    {
        if (value is not object?[] placements) return [];
        var result = new List<Gw2CollisionPlacement>(placements.Length);
        foreach (var placementObject in placements)
        {
            if (placementObject is not PackObject placement) continue;
            result.Add(new(
                PackValue.OptionalU64(placement, "token") ?? 0,
                PackValue.AsU32(placement.Fields.GetValueOrDefault("geometryIndex")),
                Vec3(placement, "translate"),
                Vec3(placement, "rotate"),
                PackValue.OptionalF32(placement, "scale") ?? 0)
            {
                Sequence = PackValue.OptionalU64(placement, "sequence"),
            });
        }
        return result;
    }

    private static IReadOnlyList<Gw2CollisionBlocker> ReadBlockers(object? value)
    {
        if (value is not object?[] blockers) return [];
        var result = new List<Gw2CollisionBlocker>(blockers.Length);
        foreach (var blockerObject in blockers)
        {
            if (blockerObject is not PackObject blocker) continue;
            result.Add(new(PackValue.AsVec3List(blocker.Fields.GetValueOrDefault("vertices"))));
        }
        return result;
    }

    private static IReadOnlyList<Gw2CollisionAnimation> ReadAnimations(object? value)
    {
        if (value is not object?[] animations) return [];
        var result = new List<Gw2CollisionAnimation>(animations.Length);
        foreach (var animationObject in animations)
        {
            if (animationObject is not PackObject animation) continue;
            result.Add(new(
                PackValue.AsU64(animation.Fields.GetValueOrDefault("sequence")),
                PackValue.AsU32List(animation.Fields.GetValueOrDefault("collisionIndices")),
                PackValue.AsU32List(animation.Fields.GetValueOrDefault("blockerIndices"))));
        }
        return result;
    }

    private static IReadOnlyList<Gw2CollisionWaterVolume> ReadWaterVolumes(object? value)
    {
        if (value is not object?[] volumes) return [];
        var result = new List<Gw2CollisionWaterVolume>(volumes.Length);
        foreach (var volumeObject in volumes)
        {
            if (volumeObject is not PackObject volume) continue;
            result.Add(new(
                PackValue.AsU64(volume.Fields.GetValueOrDefault("guid")),
                volume.Fields.GetValueOrDefault("name") as string ?? string.Empty,
                PackValue.AsU32(volume.Fields.GetValueOrDefault("flags")),
                volume.Fields.GetValueOrDefault("verticalRange") is float[] range ? range : [],
                PackValue.AsVec2List(volume.Fields.GetValueOrDefault("vertices"))));
        }
        return result;
    }

    private static byte[] Blob(object? value) => value is ReadOnlyMemory<byte> memory ? memory.ToArray() : [];

    private static Vector3 Vec3(PackObject obj, string name) =>
        obj.Optional(name) is float[] values && values.Length >= 3
            ? new Vector3(values[0], values[1], values[2])
            : Vector3.Zero;

    internal static byte AsByte(object? value) => value is byte b ? b : (byte)0;
}

public sealed record Gw2ModelCollisionMesh(
    IReadOnlyList<Vector3> Vertices,
    IReadOnlyList<ushort> Indices,
    IReadOnlyList<byte> Surfaces,
    IReadOnlyList<Vector3> NavSeedPoints);

public sealed record Gw2CollisionBox(Vector3 Dimensions, Vector3 Position, float[] Rotation, byte Surface);

public sealed record Gw2CollisionSphere(float Radius, Vector3 Position, byte Surface);

public sealed record Gw2CollisionCapsule(Vector3 P0, Vector3 P1, float Radius, byte Surface);

public sealed record Gw2ModelCollisionKeyFrame(float Time, Vector3 Position, Quaternion Orientation);

public sealed record Gw2ModelCollisionAnimatedObject(IReadOnlyList<uint> ShapeIndices, IReadOnlyList<Gw2ModelCollisionKeyFrame> KeyFrames);

public sealed record Gw2ModelCollisionAnimation(ulong Animation, IReadOnlyList<Gw2ModelCollisionAnimatedObject> Objects, IReadOnlyList<Vector3> TargetPoints);

public sealed class Gw2ModelCollision
{
    public Gw2ModelCollision(
        IReadOnlyList<Gw2ModelCollisionMesh> meshes,
        IReadOnlyList<Gw2CollisionBox> boxes,
        IReadOnlyList<Gw2CollisionSphere> spheres,
        IReadOnlyList<Gw2CollisionCapsule> capsules)
    {
        Meshes = meshes;
        Boxes = boxes;
        Spheres = spheres;
        Capsules = capsules;
    }

    public IReadOnlyList<Gw2ModelCollisionMesh> Meshes { get; }
    public IReadOnlyList<Gw2CollisionBox> Boxes { get; }
    public IReadOnlyList<Gw2CollisionSphere> Spheres { get; }
    public IReadOnlyList<Gw2CollisionCapsule> Capsules { get; }
    public IReadOnlyList<Gw2ModelCollisionAnimation> Animations { get; init; } = [];
    public IReadOnlyList<IReadOnlyList<ulong>> SurfaceTokens { get; init; } = [];

    public bool HasCollision => Meshes.Count != 0 || Boxes.Count != 0 || Spheres.Count != 0 || Capsules.Count != 0;

    public static Gw2ModelCollision? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.FindRoot("ModelFileCollision");
        if (root is null) return null;

        return new Gw2ModelCollision(
            ReadMeshes(root.Fields.GetValueOrDefault("meshes")),
            ReadBoxes(root.Fields.GetValueOrDefault("boxes")),
            ReadSpheres(root.Fields.GetValueOrDefault("spheres")),
            ReadCapsules(root.Fields.GetValueOrDefault("capsules")))
        {
            Animations = PackValue.AsObjects(root.Optional("animations"))
                .Select(animation => new Gw2ModelCollisionAnimation(
                    PackValue.AsU64(animation.Fields.GetValueOrDefault("animation")),
                    PackValue.AsObjects(animation.Fields.GetValueOrDefault("objects"))
                        .Select(animated => new Gw2ModelCollisionAnimatedObject(
                            PackValue.AsU32List(animated.Fields.GetValueOrDefault("shapeIndices")),
                            PackValue.AsObjects(animated.Fields.GetValueOrDefault("keyFrames"))
                                .Select(keyFrame => new Gw2ModelCollisionKeyFrame(
                                    PackValue.AsF32(keyFrame.Fields.GetValueOrDefault("time")),
                                    Vec3(keyFrame, "position"),
                                    keyFrame.Fields.GetValueOrDefault("orientation") is float[] { Length: >= 4 } orientation
                                        ? new Quaternion(orientation[0], orientation[1], orientation[2], orientation[3])
                                        : Quaternion.Identity))
                                .ToArray()))
                        .ToArray(),
                    PackValue.AsVec3List(animation.Fields.GetValueOrDefault("targetPoints"))))
                .ToArray(),
            SurfaceTokens = PackValue.AsObjects(root.Optional("surfaces"))
                .Select(surface => PackValue.AsU64List(surface.Fields.GetValueOrDefault("tokens")))
                .ToArray(),
        };
    }

    private static IReadOnlyList<Gw2ModelCollisionMesh> ReadMeshes(object? value)
    {
        if (value is not object?[] meshes) return [];
        var result = new List<Gw2ModelCollisionMesh>(meshes.Length);
        foreach (var meshObject in meshes)
        {
            if (meshObject is not PackObject mesh) continue;
            result.Add(new(
                PackValue.AsVec3List(mesh.Fields.GetValueOrDefault("vertices")),
                PackValue.AsU16List(mesh.Fields.GetValueOrDefault("indices")),
                ByteList(mesh.Fields.GetValueOrDefault("surfaces")),
                PackValue.AsVec3List(mesh.Fields.GetValueOrDefault("navSeedPoints"))));
        }
        return result;
    }

    private static IReadOnlyList<Gw2CollisionBox> ReadBoxes(object? value)
    {
        if (value is not object?[] boxes) return [];
        var result = new List<Gw2CollisionBox>(boxes.Length);
        foreach (var boxObject in boxes)
        {
            if (boxObject is not PackObject box) continue;
            result.Add(new(
                Vec3(box, "dimensions"),
                Vec3(box, "position"),
                box.Fields.GetValueOrDefault("rotation") is float[] rotation ? rotation : [],
                Gw2MapCollision.AsByte(box.Fields.GetValueOrDefault("surface"))));
        }
        return result;
    }

    private static IReadOnlyList<Gw2CollisionSphere> ReadSpheres(object? value)
    {
        if (value is not object?[] spheres) return [];
        var result = new List<Gw2CollisionSphere>(spheres.Length);
        foreach (var sphereObject in spheres)
        {
            if (sphereObject is not PackObject sphere) continue;
            result.Add(new(
                PackValue.AsF32(sphere.Fields.GetValueOrDefault("radius")),
                Vec3(sphere, "position"),
                Gw2MapCollision.AsByte(sphere.Fields.GetValueOrDefault("surface"))));
        }
        return result;
    }

    private static IReadOnlyList<Gw2CollisionCapsule> ReadCapsules(object? value)
    {
        if (value is not object?[] capsules) return [];
        var result = new List<Gw2CollisionCapsule>(capsules.Length);
        foreach (var capsuleObject in capsules)
        {
            if (capsuleObject is not PackObject capsule) continue;
            result.Add(new(
                Vec3(capsule, "p0"),
                Vec3(capsule, "p1"),
                PackValue.AsF32(capsule.Fields.GetValueOrDefault("radius")),
                Gw2MapCollision.AsByte(capsule.Fields.GetValueOrDefault("surface"))));
        }
        return result;
    }

    private static IReadOnlyList<byte> ByteList(object? value) =>
        value is ReadOnlyMemory<byte> memory ? memory.ToArray() : [];

    private static Vector3 Vec3(PackObject obj, string name) =>
        obj.Fields.GetValueOrDefault(name) is float[] values && values.Length >= 3
            ? new Vector3(values[0], values[1], values[2])
            : Vector3.Zero;
}
