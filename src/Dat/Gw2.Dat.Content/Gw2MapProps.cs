using Gw2.Formats;
using System.Buffers.Binary;
using System.Numerics;

namespace Gw2.Dat.Content;

public sealed record Gw2MapPropConstant(uint Token, IReadOnlyList<float> Value, uint Submodel);

public static class Gw2MapPropFlags
{
    public const uint NoPath = 1u << 0;
    public const uint NoLoopAnim = 1u << 1;
    public const uint AnimOnWater = 1u << 2;
    public const uint NoVolumeDecals = 1u << 3;
    public const uint DropLod2 = 1u << 4;
    public const uint StrippedDecal = 1u << 5;
    public const uint NoZoneModels = 1u << 6;
    public const uint DisableLod = 1u << 7;
    public const uint NoCollision = 1u << 8;
    public const uint ForceOccluderOn = 1u << 9;
    public const uint ForceOccluderOff = 1u << 10;
    public const uint NoShadows = 1u << 11;
    public const uint NoReflection = 1u << 12;
    public const uint NoCull = 1u << 13;
    public const uint NoDecals = 1u << 14;
    public const uint MovingNavMesh = 1u << 15;
    public const uint OverrideLod = 1u << 16;
    public const uint Debris = 1u << 17;
    public const uint WaveCrestAnim = 1u << 18;
    public const uint Detail = 1u << 19;
    public const uint ForceDynamicShadow = 1u << 20;
    public const uint NoInstancing = 1u << 21;
    public const uint Invisible = 1u << 22;
    public const uint Frozen = 1u << 23;
    public const uint CustomCull = 1u << 24;
    public const uint PerSubmodelConstants = 1u << 25;
    public const uint NoCube = 1u << 26;
    public const uint NoFademapStippleFade = 1u << 27;
}

public sealed record Gw2MapPropPlacement(
    uint ModelFileId,
    Vector3 Position,
    Vector3 Rotation,
    float Scale,
    uint Color = 0x80808080u,
    IReadOnlyList<Gw2MapPropConstant>? Constants = null,
    float? Lod1 = null,
    float? Lod2 = null,
    ulong Guid = 0,
    uint Flags = 0,
    ulong? AnimSequence = null)
{
    public IReadOnlyList<uint> BlitTextureFileIds { get; init; } = [];
    public static IReadOnlyList<ulong> BlitSlotIds { get; } = [0xD02D83E50, 0x1502D83E50, 0x1D02D83E50, 0x2502D83E50, 0x11EC83E50, 0x2D02D83E50, 0x3502D83E50];
    public IEnumerable<(ulong BlitId, uint TextureFileId)> BlitTextures =>
        BlitTextureFileIds.Take(BlitSlotIds.Count).Select((fileId, slot) => (BlitSlotIds[slot], fileId)).Where(blit => blit.fileId != 0);
    public ulong Permutation { get; init; }
    public Vector4 Bounds { get; init; }
    public ushort BroadId { get; init; }
    public ushort BucketId { get; init; } = ushort.MaxValue;
    public byte UnnamedByte { get; init; } = byte.MaxValue;
    public byte SortLayer { get; init; }
    public IReadOnlyList<ulong> OriginalGuids { get; init; } = [];
}

public sealed class Gw2MapProps
{
    public Gw2MapProps(IReadOnlyList<Gw2MapPropPlacement> placements) => Placements = placements;

    public IReadOnlyList<Gw2MapPropPlacement> Placements { get; }

    public byte[] BroadPhaseData { get; init; } = [];

    public Gw2Broadphase? BroadPhase => Gw2Broadphase.TryParse(BroadPhaseData);

    public uint NextBroadId { get; init; }

    public bool IsEmpty => Placements.Count == 0;

    public static Gw2MapProps? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.FindRoot("PackMapProp");
        if (root is null) return null;

        var placements = new List<Gw2MapPropPlacement>();
        if (root.Fields.GetValueOrDefault("propArray") is object?[] props)
        {
            foreach (var propObject in props)
                if (propObject is PackObject prop)
                    AddPlacement(prop, placements);
        }

        if (root.Fields.GetValueOrDefault("propInstanceArray") is object?[] instances)
        {
            foreach (var instanceObject in instances)
                if (instanceObject is PackObject instance)
                    AddInstance(instance, placements);
        }

        if (root.Fields.GetValueOrDefault("propAnimArray") is object?[] animatedProps)
        {
            foreach (var animatedObject in animatedProps)
                if (animatedObject is PackObject animated)
                    AddAnimated(animated, placements);
        }

        return new(placements)
        {
            BroadPhaseData = PackValue.AsBytes((root.Fields.GetValueOrDefault("broadPhase") as PackObject)?.Fields.GetValueOrDefault("broadphaseData")),
            NextBroadId = PackValue.AsU32(root.Fields.GetValueOrDefault("nextBroadId")),
        };
    }

    public IEnumerable<uint> EnumerateModelDependencies()
    {
        var seen = new HashSet<uint>();
        foreach (var placement in Placements)
            if (seen.Add(placement.ModelFileId))
                yield return placement.ModelFileId;
    }

    private static void AddPlacement(PackObject prop, List<Gw2MapPropPlacement> placements)
    {
        if (!TryReadModel(prop, out var modelFileId)) return;
        placements.Add(WithObjectFields(new(modelFileId, Vector(prop, "position"), Vector(prop, "rotation"), Scalar(prop, "scale"), Color(prop), Constants(prop), Lod(prop, "lod1"), Lod(prop, "lod2"), Guid(prop), Flags(prop)), prop));
    }

    private static Gw2MapPropPlacement WithObjectFields(Gw2MapPropPlacement placement, PackObject prop) => placement with
    {
        BlitTextureFileIds = BlitTextures(prop),
        Permutation = PackValue.AsU64(prop.Fields.GetValueOrDefault("permutation")),
        Bounds = prop.Fields.GetValueOrDefault("bounds") is float[] { Length: >= 4 } bounds ? new Vector4(bounds[0], bounds[1], bounds[2], bounds[3]) : Vector4.Zero,
        BroadId = (ushort)PackValue.AsU32(prop.Fields.GetValueOrDefault("broadId")),
        BucketId = prop.Fields.ContainsKey("bucketId") ? (ushort)PackValue.AsU32(prop.Fields["bucketId"]) : ushort.MaxValue,
        UnnamedByte = prop.Fields.ContainsKey("byte") ? PackValue.AsU8(prop.Fields["byte"]) : byte.MaxValue,
        SortLayer = PackValue.AsU8(prop.Fields.GetValueOrDefault("sortLayer")),
        OriginalGuids = PackValue.AsU64List(prop.Fields.GetValueOrDefault("origGuidArray")),
    };

    private static IReadOnlyList<uint> BlitTextures(PackObject prop) =>
        prop.Fields.GetValueOrDefault("blitTextures") is object?[] slots
            ? slots.Select(slot => slot is PackFileReference reference && reference.TryGetFileId(out var fileId) ? fileId : 0u).ToArray()
            : [];

    private static void AddAnimated(PackObject prop, List<Gw2MapPropPlacement> placements)
    {
        if (!TryReadModel(prop, out var modelFileId)) return;
        placements.Add(WithObjectFields(new(modelFileId, Vector(prop, "position"), Vector(prop, "rotation"), Scalar(prop, "scale"), Color(prop), Constants(prop), Lod(prop, "lod1"), Lod(prop, "lod2"), Guid(prop), Flags(prop),
            PackValue.AsU64(prop.Fields.GetValueOrDefault("animSequence"))), prop));
    }

    private static void AddInstance(PackObject prop, List<Gw2MapPropPlacement> placements)
    {
        if (!TryReadModel(prop, out var modelFileId)) return;

        var color = Color(prop);
        var constants = Constants(prop);
        var lod1 = Lod(prop, "lod1");
        var lod2 = Lod(prop, "lod2");
        var guid = Guid(prop);
        var flags = Flags(prop);

        var instance = WithObjectFields(new(modelFileId, Vector(prop, "position"), Vector(prop, "rotation"), Scalar(prop, "scale"), color, constants, lod1, lod2, guid, flags), prop);
        placements.Add(instance);

        if (prop.Fields.GetValueOrDefault("transforms") is not object?[] transforms) return;
        foreach (var transformObject in transforms)
            if (transformObject is PackObject transform)
                placements.Add(instance with { Position = Vector(transform, "position"), Rotation = Vector(transform, "rotation"), Scale = Scalar(transform, "scale") });
    }

    private static uint Flags(PackObject prop) => PackValue.AsU32(prop.Fields.GetValueOrDefault("flags"));

    private static ulong Guid(PackObject prop) => PackValue.AsU64(prop.Fields.GetValueOrDefault("guid"));

    private static uint Color(PackObject prop) =>
        prop.Fields.GetValueOrDefault("color") is ReadOnlyMemory<byte> bytes && bytes.Length >= 4
            ? BinaryPrimitives.ReadUInt32LittleEndian(bytes.Span)
            : 0x80808080u;

    private static IReadOnlyList<Gw2MapPropConstant> Constants(PackObject prop)
    {
        if (prop.Fields.GetValueOrDefault("constants") is not object?[] values || values.Length == 0) return [];
        var result = new List<Gw2MapPropConstant>(values.Length);
        foreach (var value in values)
            if (value is PackObject constant)
                result.Add(new(
                    PackValue.AsU32(constant.Fields.GetValueOrDefault("token")),
                    constant.Fields.GetValueOrDefault("constant") is float[] floats ? floats : [],
                    PackValue.AsU32(constant.Fields.GetValueOrDefault("submodel"))));
        return result;
    }

    private static bool TryReadModel(PackObject prop, out uint modelFileId)
    {
        modelFileId = 0;
        return prop.Fields.GetValueOrDefault("filename") is PackFileReference reference && reference.TryGetFileId(out modelFileId);
    }

    private static Vector3 Vector(PackObject obj, string name) =>
        obj.Fields.GetValueOrDefault(name) is float[] values && values.Length >= 3
            ? new Vector3(values[0], values[1], values[2])
            : Vector3.Zero;

    private static float Scalar(PackObject obj, string name) =>
        obj.Fields.GetValueOrDefault(name) is float value ? value : 0;

    private static float? Lod(PackObject obj, string name) =>
        obj.Fields.GetValueOrDefault(name) is float value && value > 0 && !float.IsPositiveInfinity(value) ? value : null;
}

public static class Gw2MapPropTransform
{
    public static Matrix4x4 World(Gw2MapPropPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        return World(placement.Position, placement.Rotation, placement.Scale);
    }

    public static Matrix4x4 World(Vector3 position, Vector3 rotation, float scale)
    {
        var (sinX, cosX) = MathF.SinCos(rotation.X);
        var (sinY, cosY) = MathF.SinCos(rotation.Y);
        var (sinZ, cosZ) = MathF.SinCos(rotation.Z);
        return new Matrix4x4(
            (cosZ * cosY - sinY * sinX * sinZ) * scale, -cosX * sinZ * scale, (cosY * sinX * sinZ + cosZ * sinY) * scale, 0f,
            (cosZ * sinX * sinY + sinZ * cosY) * scale, cosZ * cosX * scale, (sinZ * sinY - cosZ * cosY * sinX) * scale, 0f,
            -cosX * sinY * scale, sinX * scale, cosY * cosX * scale, 0f,
            position.X, position.Y, position.Z, 1f);
    }
}
