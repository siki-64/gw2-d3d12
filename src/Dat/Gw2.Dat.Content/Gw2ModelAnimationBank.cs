using Gw2.Formats;
using System.Numerics;

namespace Gw2.Dat.Content;

public sealed record Gw2ModelAnimationData(
    ulong Token,
    float MoveSpeed,
    bool IsAdditive,
    Vector3 Center,
    float Radius,
    byte[] GrannyAnimation,
    IReadOnlyList<uint> GrannyPointers,
    int PointerSize = 4,
    PackFileReference StreamedAnimationFile = default,
    IReadOnlyList<Gw2ModelVisibilityTrack>? VisibilityTracks = null)
{
    public IReadOnlyList<Gw2ModelVisibilityTrack> VisibilityTracksOrEmpty => VisibilityTracks ?? [];

    public bool IsStreamed => GrannyAnimation.Length == 0 && StreamedAnimationFile.TryGetFileId(out var fileId) && fileId != 0;

    public byte[] LodGrannyAnimation { get; init; } = [];
    public IReadOnlyList<uint> LodGrannyPointers { get; init; } = [];
    public IReadOnlyList<Gw2ModelUvAnimation> UvAnimations { get; init; } = [];
    public IReadOnlyList<Gw2ModelBoneAnimation> CloudAnimations { get; init; } = [];
    public IReadOnlyList<Gw2ModelMaterialConstantAnimation> MaterialConstantAnimations { get; init; } = [];
    public IReadOnlyList<ushort> MorphTrackGroups { get; init; } = [];
    public IReadOnlyList<float> TriggerTimes { get; init; } = [];
    public IReadOnlyList<ulong> TriggerTokens { get; init; } = [];
    public IReadOnlyList<Gw2ModelBoneAnimation> LightAnimations { get; init; } = [];
    public uint VariantCount { get; init; }
    public IReadOnlyList<uint> VariantIndices { get; init; } = [];
    public IReadOnlyList<Gw2ModelAnimationProperty> Properties { get; init; } = [];
    public IReadOnlyList<Gw2ModelTokenMapAnimation> TokenMapAnimations { get; init; } = [];
    public IReadOnlyList<Gw2ModelBoneAnimation> BoneConstraintAnimations { get; init; } = [];
    public IReadOnlyList<Gw2ModelStreakAnimation> StreakAnimations { get; init; } = [];
    public IReadOnlyList<Gw2ModelBoneAnimation> LightningAnimations { get; init; } = [];
    public IReadOnlyList<Gw2ModelBoneAnimation> WindAnimations { get; init; } = [];
}

public sealed record Gw2ModelAnimationTrack(byte Type, uint TrackGroupIndex, uint VectorTrackIndex, Vector4 InitialValue);

public sealed record Gw2ModelBoneAnimation(ulong Bone, IReadOnlyList<Gw2ModelAnimationTrack> Tracks);

public sealed record Gw2ModelUvAnimation(uint UvAnimId, IReadOnlyList<Gw2ModelAnimationTrack> Tracks);

public sealed record Gw2ModelStreakAnimation(ulong Bone, IReadOnlyList<Gw2ModelBoneAnimation> Anchors);

public sealed record Gw2ModelMaterialConstantAnimation(uint MaterialId, uint ConstantToken, uint TrackGroupIndex, uint VectorTrackIndex, Vector4 InitialValue);

public sealed record Gw2ModelTokenMapAnimation(ulong LinkToken, uint TrackGroupIndex, uint VectorTrackIndex, Vector4 InitialValue);

public sealed record Gw2ModelAnimationProperty(ulong Id, uint Type, float Time, ulong Value, PackFileReference StringValue);

public sealed record Gw2ModelVisibilityTrack(ulong BoneToken, IReadOnlyList<float> ToggleSeconds)
{
    public bool IsVisibleAt(float seconds)
    {
        var visible = true;
        foreach (var toggle in ToggleSeconds)
        {
            if (toggle > seconds) break;
            visible = !visible;
        }

        return visible;
    }
}

public sealed record Gw2ModelCompoundAnimation(ulong Token, ulong Start, ulong Loop, ulong End);

public sealed record Gw2ModelAnimationImportSequence(ulong Name, float Duration);

public sealed record Gw2ModelAnimationImport(PackFileReference Filename, IReadOnlyList<Gw2ModelAnimationImportSequence> Sequences);

public sealed class Gw2ModelAnimation
{
    public Gw2ModelAnimation(
        IReadOnlyList<Gw2ModelAnimationData> animations,
        IReadOnlyList<Gw2ModelCompoundAnimation> compoundAnimations,
        IReadOnlyList<ulong> fallbacks,
        IReadOnlyList<Gw2ModelAnimationImport> imports,
        byte[] grannyAnimation,
        IReadOnlyList<uint> grannyPointers)
    {
        Animations = animations;
        CompoundAnimations = compoundAnimations;
        Fallbacks = fallbacks;
        Imports = imports;
        GrannyAnimation = grannyAnimation;
        GrannyPointers = grannyPointers;
    }

    public IReadOnlyList<Gw2ModelAnimationData> Animations { get; }
    public IReadOnlyList<Gw2ModelCompoundAnimation> CompoundAnimations { get; }
    public IReadOnlyList<ulong> Fallbacks { get; }
    public IReadOnlyList<Gw2ModelAnimationImport> Imports { get; }
    public byte[] GrannyAnimation { get; }
    public IReadOnlyList<uint> GrannyPointers { get; }

    public static Gw2ModelAnimation? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.FindRoot("ModelFileAnimation");
        if (root is null) return null;

        var bank = root.Fields.GetValueOrDefault("bank") as PackObject;
        var pointerSize = document.Pack.PointerSize;

        var animations = new List<Gw2ModelAnimationData>();
        if (bank?.Fields.GetValueOrDefault("animations") is object?[] animationObjects)
            foreach (var animationObject in animationObjects)
                if (animationObject is PackObject animation)
                {
                    var data = animation.Fields.GetValueOrDefault("data") as PackObject;
                    var lod = animation.Fields.GetValueOrDefault("animLod") as PackObject;
                    animations.Add(new(
                        PackValue.AsU64(animation.Fields.GetValueOrDefault("token")),
                        PackValue.AsF32(animation.Fields.GetValueOrDefault("moveSpeed")),
                        PackValue.AsU32(animation.Fields.GetValueOrDefault("isAdditive")) != 0,
                        Vec3(animation, "center"),
                        PackValue.AsF32(animation.Fields.GetValueOrDefault("radius")),
                        PackValue.AsBytes(data?.Fields.GetValueOrDefault("animation")),
                        PackValue.AsU32List(data?.Optional("pointers")),
                        pointerSize,
                        lod?.Fields.GetValueOrDefault("fileFull") is PackFileReference streamed ? streamed : default,
                        VisibilityTracks(animation))
                    {
                        LodGrannyAnimation = PackValue.AsBytes((lod?.Optional("data") as PackObject)?.Fields.GetValueOrDefault("animation")),
                        LodGrannyPointers = PackValue.AsU32List((lod?.Optional("data") as PackObject)?.Optional("pointers")),
                        UvAnimations = PackValue.AsObjects(animation.Optional("uvAnimData"))
                            .Select(uv => new Gw2ModelUvAnimation(PackValue.AsU32(uv.Fields.GetValueOrDefault("uvAnimId")), Tracks(uv, "uvTransformData")))
                            .ToArray(),
                        CloudAnimations = BoneAnimations(animation, "cloudAnim", "cloudTrackData"),
                        MaterialConstantAnimations = PackValue.AsObjects(animation.Optional("matConstAnim"))
                            .Select(constant => new Gw2ModelMaterialConstantAnimation(
                                PackValue.AsU32(constant.Fields.GetValueOrDefault("materialId")),
                                PackValue.AsU32(constant.Fields.GetValueOrDefault("constToken")),
                                PackValue.AsU32(constant.Fields.GetValueOrDefault("trackGroupIndex")),
                                PackValue.AsU32(constant.Fields.GetValueOrDefault("vectorTrackIndex")),
                                Vec4(constant, "initialValue")))
                            .ToArray(),
                        MorphTrackGroups = PackValue.AsU16List(animation.Optional("morphTrackGroups")),
                        TriggerTimes = FloatList(animation.Optional("triggerTimes")),
                        TriggerTokens = PackValue.AsU64List(animation.Optional("triggerTokens")),
                        LightAnimations = BoneAnimations(animation, "lightAnimData", "lightTrackData"),
                        VariantCount = PackValue.AsU32(animation.Optional("variantCount")),
                        VariantIndices = PackValue.AsU32List(animation.Optional("variantIndices")),
                        Properties = PackValue.AsObjects(animation.Optional("properties"))
                            .Select(property => new Gw2ModelAnimationProperty(
                                PackValue.AsU64(property.Fields.GetValueOrDefault("id")),
                                PackValue.AsU32(property.Fields.GetValueOrDefault("type")),
                                PackValue.AsF32(property.Fields.GetValueOrDefault("time")),
                                PackValue.AsU64(property.Fields.GetValueOrDefault("val")),
                                property.Fields.GetValueOrDefault("strVal") is PackFileReference text ? text : default))
                            .ToArray(),
                        TokenMapAnimations = PackValue.AsObjects(animation.Optional("tokenMapAnims"))
                            .Select(map => new Gw2ModelTokenMapAnimation(
                                PackValue.AsU64(map.Fields.GetValueOrDefault("linkToken")),
                                PackValue.AsU32(map.Fields.GetValueOrDefault("trackGroupIndex")),
                                PackValue.AsU32(map.Fields.GetValueOrDefault("vectorTrackIndex")),
                                Vec4(map, "initialValue")))
                            .ToArray(),
                        BoneConstraintAnimations = BoneAnimations(animation, "bcAnim", "bcTrackData"),
                        StreakAnimations = PackValue.AsObjects(animation.Optional("streakAnim"))
                            .Select(streak => new Gw2ModelStreakAnimation(
                                PackValue.AsU64(streak.Fields.GetValueOrDefault("bone")),
                                BoneAnimations(streak, "anchorAnim", "anchorTrackData")))
                            .ToArray(),
                        LightningAnimations = BoneAnimations(animation, "lightningAnim", "lightningTrackData"),
                        WindAnimations = BoneAnimations(animation, "windAnimData", "windTrackData"),
                    });
                }

        var compound = new List<Gw2ModelCompoundAnimation>();
        if (bank?.Fields.GetValueOrDefault("compoundAnimations") is object?[] compoundObjects)
            foreach (var compoundObject in compoundObjects)
                if (compoundObject is PackObject item)
                    compound.Add(new(
                        PackValue.AsU64(item.Fields.GetValueOrDefault("token")),
                        PackValue.AsU64(item.Fields.GetValueOrDefault("start")),
                        PackValue.AsU64(item.Fields.GetValueOrDefault("loop")),
                        PackValue.AsU64(item.Fields.GetValueOrDefault("end"))));

        var imports = new List<Gw2ModelAnimationImport>();
        if (bank?.Fields.GetValueOrDefault("imports") is object?[] importObjects)
            foreach (var importObject in importObjects)
                if (importObject is PackObject import)
                    imports.Add(new(
                        import.Fields.GetValueOrDefault("filename") is PackFileReference reference ? reference : default,
                        ImportSequences(import)));

        var granny = root.Fields.GetValueOrDefault("anim") as PackObject;
        return new(
            animations,
            compound,
            PackValue.AsU64List(bank?.Fields.GetValueOrDefault("fallbacks")),
            imports,
            PackValue.AsBytes(granny?.Fields.GetValueOrDefault("animation")),
            PackValue.AsU32List(granny?.Optional("pointers")));
    }

    private static IReadOnlyList<Gw2ModelVisibilityTrack> VisibilityTracks(PackObject animation)
    {
        if (animation.Fields.GetValueOrDefault("visTrackData") is not object?[] trackObjects) return [];
        var tracks = new List<Gw2ModelVisibilityTrack>(trackObjects.Length);
        foreach (var trackObject in trackObjects)
            if (trackObject is PackObject track)
                tracks.Add(new(
                    PackValue.AsU64(track.Fields.GetValueOrDefault("boneToken")),
                    FloatList(track.Fields.GetValueOrDefault("keys"))));
        return tracks;
    }

    private static IReadOnlyList<float> FloatList(object? value) => value switch
    {
        float[] floats => floats,
        object?[] values => values.Select(PackValue.AsF32).ToList(),
        _ => [],
    };

    private static IReadOnlyList<Gw2ModelAnimationImportSequence> ImportSequences(PackObject import)
    {
        if (import.Fields.GetValueOrDefault("sequences") is object?[] sequences)
        {
            var named = new List<Gw2ModelAnimationImportSequence>(sequences.Length);
            foreach (var item in sequences)
                if (item is PackObject sequence)
                    named.Add(new(
                        PackValue.AsU64(sequence.Fields.GetValueOrDefault("name")),
                        PackValue.AsF32(sequence.Fields.GetValueOrDefault("duration"))));
            return named;
        }

        if (import.Fields.GetValueOrDefault("sequenceTokens") is object?[] tokens)
        {
            var named = new List<Gw2ModelAnimationImportSequence>(tokens.Length);
            foreach (var item in tokens) named.Add(new(PackValue.AsU64(item), 0f));
            return named;
        }

        return [];
    }

    private static IReadOnlyList<Gw2ModelBoneAnimation> BoneAnimations(PackObject owner, string name, string trackName) =>
        PackValue.AsObjects(owner.Optional(name))
            .Select(item => new Gw2ModelBoneAnimation(PackValue.AsU64(item.Fields.GetValueOrDefault("bone")), Tracks(item, trackName)))
            .ToArray();

    private static IReadOnlyList<Gw2ModelAnimationTrack> Tracks(PackObject owner, string name) =>
        PackValue.AsObjects(owner.Fields.GetValueOrDefault(name))
            .Select(track => new Gw2ModelAnimationTrack(
                PackValue.AsU8(track.Fields.GetValueOrDefault("type")),
                PackValue.AsU32(track.Fields.GetValueOrDefault("trackGroupIndex")),
                PackValue.AsU32(track.Fields.GetValueOrDefault("vectorTrackIndex")),
                Vec4(track, "initialValue")))
            .ToArray();

    private static Vector4 Vec4(PackObject obj, string name) =>
        obj.Fields.GetValueOrDefault(name) is float[] { Length: >= 4 } values ? new Vector4(values[0], values[1], values[2], values[3]) : Vector4.Zero;

    private static Vector3 Vec3(PackObject obj, string name) =>
        obj.Fields.GetValueOrDefault(name) is float[] values && values.Length >= 3
            ? new Vector3(values[0], values[1], values[2])
            : Vector3.Zero;
}
