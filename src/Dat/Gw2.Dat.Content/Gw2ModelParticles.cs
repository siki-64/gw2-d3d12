using Gw2.Formats;
using System.Collections;
using System.Numerics;

namespace Gw2.Dat.Content;

public readonly record struct Gw2ParticleRange(float Minimum, float Maximum);

public sealed record Gw2ParticleCurve(uint CurveType, IReadOnlyList<Vector2> Keys);

public sealed record Gw2ParticleFlipbook(uint Columns, uint Rows, uint FrameCount, uint StartFrame, float FramesPerSecond);

public sealed record Gw2ParticleEmitter(
    ulong BoneToken,
    uint Flags,
    uint EmitterFlags,
    uint AlignmentType,
    Vector3 AlignmentDirection,
    uint SpawnShape,
    float SpawnPeriodSeconds,
    float SpawnProbabilityPercent,
    Gw2ParticleRange SpawnGroupSize,
    Gw2ParticleRange SpawnRadius,
    Gw2ParticleRange LifetimeSeconds,
    IReadOnlyList<Gw2ParticleRange> Velocity,
    IReadOnlyList<Gw2ParticleRange> Acceleration,
    Gw2ParticleRange VelocityInherit,
    float Drag,
    IReadOnlyList<Gw2ParticleRange> ScaleInitial,
    IReadOnlyList<Gw2ParticleRange> ScaleChange,
    Gw2ParticleCurve? ScaleCurve,
    uint ScaleCurvePreset,
    Gw2ParticleRange RotationInitialDegrees,
    Gw2ParticleRange RotationChangeDegrees,
    float RotationDrag,
    Vector4 ColorBegin,
    Vector4 ColorEnd,
    float ColorPeriod,
    Gw2ParticleRange ColorFalloff,
    Gw2ParticleCurve? OpacityCurve,
    uint OpacityCurvePreset,
    Gw2ParticleFlipbook? Flipbook,
    Vector4 TexCoordRect)
{
    public uint Affinity { get; init; }
    public float Offset { get; init; }
    public Gw2ParticleRange AccelerationDistanceRange { get; init; }
    public Gw2ParticleRange AccelerationDistanceSpeed { get; init; }
    public Gw2ParticleRange VelocityDistanceRange { get; init; }
    public Gw2ParticleRange VelocityDistanceSpeed { get; init; }
    public Gw2ParticleRange SpawnWindEmit { get; init; }
    public Gw2ParticleRange SpawnWindSpeed { get; init; }
    public uint WindInfluence { get; init; }
    public Matrix4x4 Transform { get; init; } = Matrix4x4.Identity;
    public Gw2ParticleMeshSettings? MeshSettings { get; init; }
}

public sealed record Gw2ParticleMeshSettings(
    uint Alignment,
    float BankThreshold,
    float BankScale,
    PackFileReference MeshFile,
    float ObstacleRadius,
    IReadOnlyList<Gw2ParticleRange> Rotation,
    Vector3 RotationDrag,
    IReadOnlyList<Gw2ParticleRange> ScaleChange,
    IReadOnlyList<Gw2ParticleRange> ScaleInitial,
    IReadOnlyList<Gw2ParticleRange> Spin);

public sealed record Gw2ParticleCloud(
    ulong BoneToken,
    uint Flags,
    uint VertexFormat,
    int MaterialIndex,
    IReadOnlyList<int> EmitterIndices,
    Vector3 Velocity,
    Vector3 Acceleration,
    float Drag)
{
    public IReadOnlyList<int> ObstacleIndices { get; init; } = [];
}

public sealed class Gw2ModelParticles
{
    private Gw2ModelParticles(IReadOnlyList<Gw2ParticleCloud> clouds, IReadOnlyList<Gw2ParticleEmitter> emitters)
    {
        Clouds = clouds;
        Emitters = emitters;
    }

    public IReadOnlyList<Gw2ParticleCloud> Clouds { get; }
    public IReadOnlyList<Gw2ParticleEmitter> Emitters { get; }
    public bool HasParticles => Clouds.Count != 0 && Emitters.Count != 0;

    public static Gw2ModelParticles Parse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var cloudData = document.FindRoot("ModelFileData") is { } modelRoot
            ? Object(modelRoot, "cloudData")
            : null;
        if (cloudData is null) return new([], []);

        return new(
            Objects(cloudData, "clouds").Select(ReadCloud).ToList(),
            Objects(cloudData, "emitters").Select(ReadEmitter).ToList());
    }

    private static Gw2ParticleCloud ReadCloud(PackObject cloud) => new(
        U64(cloud, "bone"),
        U32(cloud, "flags"),
        U32(cloud, "fvf"),
        (int)U32(cloud, "materialIndex"),
        Floats(Field(cloud, "emitterIndices")).Select(value => (int)value).ToList(),
        Vec3(Field(cloud, "velocity")),
        Vec3(Field(cloud, "acceleration")),
        F32(cloud, "drag"))
    {
        ObstacleIndices = Floats(Field(cloud, "obstacleIndices")).Select(value => (int)value).ToList(),
    };

    private static Gw2ParticleEmitter ReadEmitter(PackObject emitter)
    {
        var plane = Object(emitter, "planeEmitterSettings") ?? emitter;
        return ReadEmitter(emitter, plane);
    }

    private static Gw2ParticleEmitter ReadEmitter(PackObject emitter, PackObject plane) => new(
        U64(emitter, "bone"),
        U32(emitter, "flags"),
        U32(emitter, "emitterFlags"),
        U32(plane, "alignmentType"),
        Vec3(Field(plane, "alignmentDir")),
        U32(emitter, "spawnShape"),
        F32(emitter, "spawnPeriod"),
        F32(emitter, "spawnProbability"),
        Range(Field(emitter, "spawnGroupSize")),
        Range(Field(emitter, "spawnRadius")),
        Range(Field(emitter, "lifetime")),
        Ranges(Field(emitter, "velocity")),
        Ranges(Field(emitter, "acceleration")),
        Range(Field(emitter, "velocityInherit")),
        Field(emitter, "drag") is null ? F32(plane, "drag") : F32(emitter, "drag"),
        Ranges(Field(plane, "scaleInitial")),
        Ranges(Field(plane, "scaleChange")),
        Curve(Object(emitter, "scaleCurve")),
        U32(emitter, "scaleCurvePreset"),
        Range(Field(plane, "rotationInitial")),
        Range(Field(plane, "rotationChange")),
        F32(plane, "rotationDrag"),
        Vec4(Field(emitter, "colorBegin")),
        Vec4(Field(emitter, "colorEnd")),
        F32(emitter, "colorPeriod"),
        Range(Field(emitter, "colorFalloff")),
        Curve(Object(emitter, "opacityCurve")),
        U32(emitter, "opacityCurvePreset"),
        Flipbook(Object(plane, "flipbook")),
        Vec4(Field(plane, "texCoordRect")))
    {
        Affinity = U32(emitter, "affinity"),
        Offset = F32(emitter, "offset"),
        AccelerationDistanceRange = Range(Field(emitter, "accelerationDistRange")),
        AccelerationDistanceSpeed = Range(Field(emitter, "accelerationDistSpeed")),
        VelocityDistanceRange = Range(Field(emitter, "velocityDistRange")),
        VelocityDistanceSpeed = Range(Field(emitter, "velocityDistSpeed")),
        SpawnWindEmit = Range(Field(emitter, "spawnWindEmit")),
        SpawnWindSpeed = Range(Field(emitter, "spawnWindSpeed")),
        WindInfluence = U32(emitter, "windInfluence"),
        Transform = Matrix43(Object(emitter, "transform")),
        MeshSettings = MeshSettings(Object(emitter, "meshEmitterSettings")),
    };

    private static Gw2ParticleMeshSettings? MeshSettings(PackObject? mesh) => mesh is null
        ? null
        : new(
            U32(mesh, "alignment"),
            F32(mesh, "bankThreshold"),
            F32(mesh, "bankScale"),
            Field(mesh, "filename") is PackFileReference file ? file : default,
            F32(mesh, "obstacleRadius"),
            Ranges(Field(mesh, "rotation")),
            Vec3(Field(mesh, "rotationDrag")),
            Ranges(Field(mesh, "scaleChange")),
            Ranges(Field(mesh, "scaleInitial")),
            Ranges(Field(mesh, "spin")));

    private static Matrix4x4 Matrix43(PackObject? matrix)
    {
        if (matrix is null) return Matrix4x4.Identity;
        var x = Vec4(Field(matrix, "x"));
        var y = Vec4(Field(matrix, "y"));
        var z = Vec4(Field(matrix, "z"));
        return new Matrix4x4(
            x.X, y.X, z.X, 0,
            x.Y, y.Y, z.Y, 0,
            x.Z, y.Z, z.Z, 0,
            x.W, y.W, z.W, 1);
    }

    private static Gw2ParticleCurve? Curve(PackObject? curve)
    {
        if (curve is null) return null;
        var keys = Items(Field(curve, "keys"))
            .Select(Floats)
            .Where(key => key.Count >= 2)
            .Select(key => new Vector2(key[0], key[1]))
            .ToList();
        return new(U32(curve, "curveType"), keys);
    }

    private static Gw2ParticleFlipbook? Flipbook(PackObject? flipbook) => flipbook is null
        ? null
        : new(U32(flipbook, "columns"), U32(flipbook, "rows"), U32(flipbook, "count"), U32(flipbook, "start"), F32(flipbook, "fps"));

    private static IReadOnlyList<Gw2ParticleRange> Ranges(object? value) => Items(value).Select(Range).ToList();

    private static Gw2ParticleRange Range(object? value)
    {
        var floats = Floats(value);
        return floats.Count >= 2 ? new(floats[0], floats[1]) : default;
    }

    private static Vector3 Vec3(object? value)
    {
        var floats = Floats(value);
        return floats.Count >= 3 ? new(floats[0], floats[1], floats[2]) : Vector3.Zero;
    }

    private static Vector4 Vec4(object? value)
    {
        var floats = Floats(value);
        return floats.Count >= 4 ? new(floats[0], floats[1], floats[2], floats[3]) : Vector4.Zero;
    }

    private static IReadOnlyList<float> Floats(object? value) => value switch
    {
        float[] floats => floats,
        null or string => [],
        IEnumerable items => items.Cast<object?>().Select(AsF32).ToList(),
        _ => [],
    };

    private static IEnumerable<object?> Items(object? value) =>
        value is IEnumerable items and not string ? items.Cast<object?>() : [];

    private static IEnumerable<PackObject> Objects(PackObject obj, string name) =>
        Items(Field(obj, name)).OfType<PackObject>();

    private static object? Field(PackObject obj, string name) =>
        obj.Optional(name);

    private static PackObject? Object(PackObject obj, string name) => Field(obj, name) as PackObject;

    private static uint U32(PackObject obj, string name) => Field(obj, name) switch
    {
        uint unsigned => unsigned,
        int signed => (uint)signed,
        ulong wide => (uint)wide,
        ushort shortValue => shortValue,
        byte byteValue => byteValue,
        _ => 0,
    };

    private static ulong U64(PackObject obj, string name) => Field(obj, name) switch
    {
        ulong wide => wide,
        uint unsigned => unsigned,
        int signed => (ulong)signed,
        _ => 0,
    };

    private static float F32(PackObject obj, string name) => AsF32(Field(obj, name));

    private static float AsF32(object? value) => value switch
    {
        float single => single,
        double wide => (float)wide,
        uint unsigned => unsigned,
        int signed => signed,
        ushort shortValue => shortValue,
        byte byteValue => byteValue,
        _ => 0,
    };
}
