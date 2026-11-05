using Gw2.Formats;
using System.Numerics;

namespace Gw2.Dat.Content;

public sealed record Gw2MapDecalVertex(Vector3 Position, Vector3 Normal, Vector3 Tangent, Vector3 Bitangent);

public sealed record Gw2MapDecalInfo(
    Vector3 Position,
    Vector3 Extents,
    Vector3 Rotation,
    uint MaterialFileId,
    IReadOnlyList<uint> TextureFileIds,
    uint Flags,
    float SurfaceBias,
    ulong SurfaceId,
    ulong Id,
    IReadOnlyList<Gw2MapDecalVertex> Vertices,
    IReadOnlyList<int> Indices)
{
    public Vector2 TextureScaleUv0 { get; init; }
    public Vector2 TextureOffsetUv0 { get; init; }
    public Vector2 TextureScaleUv1 { get; init; }
    public Vector2 TextureOffsetUv1 { get; init; }
    public Vector2 GridSize { get; init; }
    public Vector2 AnimationTranslation { get; init; }
    public Vector2 AnimationScaleRangeX { get; init; }
    public Vector2 AnimationScaleRangeY { get; init; }
    public Vector2 AnimationScaleSpeed { get; init; }
    public float AnimationRotation { get; init; }
    public IReadOnlyList<Gw2MapDecalConstant> Constants { get; init; } = [];
    public IReadOnlyList<ulong> PropIds { get; init; } = [];

    public Matrix4x4 Placement => Gw2MapPropTransform.World(Position, Rotation, 1f);

    public Vector2 ProjectionSize => new(Extents.X * 2f, Extents.Y * 2f);

    public float ProjectionDepth => Extents.Z * 2f;
}

public sealed record Gw2MapDecalConstant(uint Token, Vector4 Value);

public sealed class Gw2MapDecals
{
    public Gw2MapDecals(IReadOnlyList<Gw2MapDecalInfo> decals)
    {
        Decals = decals;
    }

    public IReadOnlyList<Gw2MapDecalInfo> Decals { get; }

    public static Gw2MapDecals? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.FindRoot("PackMapDecals");
        if (root is null) return null;

        var decals = new List<Gw2MapDecalInfo>();
        if (root.Fields.GetValueOrDefault("decals") is object?[] decalObjects)
            foreach (var decalObject in decalObjects)
                if (decalObject is PackObject decal)
                    decals.Add(new(
                        Vec3(decal, "position"),
                        Vec3(decal, "extents"),
                        Vec3(decal, "rotation"),
                        FileId(decal.Fields.GetValueOrDefault("materialFilename")),
                        FileIds(decal.Fields.GetValueOrDefault("textureFilenames")),
                        PackValue.AsU32(decal.Fields.GetValueOrDefault("flags")),
                        PackValue.AsF32(decal.Fields.GetValueOrDefault("surfaceBias")),
                        PackValue.AsU64(decal.Fields.GetValueOrDefault("surfaceId")),
                        PackValue.AsU64(decal.Fields.GetValueOrDefault("id")),
                        Vertices(decal.Fields.GetValueOrDefault("vertices")),
                        Ints(decal.Fields.GetValueOrDefault("indices")))
                    {
                        TextureScaleUv0 = Vec2(decal, "textureScaleUV0"),
                        TextureOffsetUv0 = Vec2(decal, "textureOffsetUV0"),
                        TextureScaleUv1 = Vec2(decal, "textureScaleUV1"),
                        TextureOffsetUv1 = Vec2(decal, "textureOffsetUV1"),
                        GridSize = Vec2(decal, "gridSize"),
                        AnimationTranslation = Vec2(decal, "animTranslation"),
                        AnimationScaleRangeX = Vec2(decal, "animScaleRangeX"),
                        AnimationScaleRangeY = Vec2(decal, "animScaleRangeY"),
                        AnimationScaleSpeed = Vec2(decal, "animScaleSpeed"),
                        AnimationRotation = PackValue.AsF32(decal.Fields.GetValueOrDefault("animRotation")),
                        Constants = Constants(decal),
                        PropIds = PackValue.AsU64List(decal.Fields.GetValueOrDefault("propIds")),
                    });

        return new(decals);
    }

    private static IReadOnlyList<Gw2MapDecalVertex> Vertices(object? value)
    {
        if (value is not object?[] values) return [];
        var result = new List<Gw2MapDecalVertex>(values.Length);
        foreach (var item in values)
            if (item is PackObject vertex)
                result.Add(new(
                    Vec3(vertex, "position"),
                    Vec3(vertex, "normal"),
                    Vec3(vertex, "tangent"),
                    Vec3(vertex, "bitangent")));
        return result;
    }

    private static IReadOnlyList<int> Ints(object? value)
    {
        if (value is not object?[] values) return [];
        var result = new List<int>(values.Length);
        foreach (var item in values)
            result.Add((int)PackValue.AsU32(item));
        return result;
    }

    private static IReadOnlyList<Gw2MapDecalConstant> Constants(PackObject decal)
    {
        var tokens = PackValue.AsU32List(decal.Fields.GetValueOrDefault("constantTokens"));
        var values = decal.Fields.GetValueOrDefault("constantValues") as object?[] ?? [];
        return tokens
            .Select((token, index) => new Gw2MapDecalConstant(token, index < values.Length && values[index] is float[] { Length: >= 4 } value
                ? new Vector4(value[0], value[1], value[2], value[3])
                : Vector4.Zero))
            .ToArray();
    }

    private static Vector2 Vec2(PackObject obj, string name) =>
        obj.Fields.GetValueOrDefault(name) is float[] values && values.Length >= 2 ? new Vector2(values[0], values[1]) : Vector2.Zero;

    private static uint FileId(object? value) => value is PackFileReference reference && reference.TryGetFileId(out var fileId) ? fileId : 0;

    private static IReadOnlyList<uint> FileIds(object? value) => value is object?[] values ? [.. values.Select(FileId)] : [];

    private static Vector3 Vec3(PackObject obj, string name) =>
        obj.Fields.GetValueOrDefault(name) is float[] values && values.Length >= 3
            ? new Vector3(values[0], values[1], values[2])
            : Vector3.Zero;
}
