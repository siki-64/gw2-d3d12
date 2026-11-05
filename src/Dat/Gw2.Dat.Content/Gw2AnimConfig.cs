using Gw2.Formats;
using System.Numerics;

namespace Gw2.Dat.Content;

public sealed record Gw2AimIkBone(ulong BoneToken, float ClampAngle, float Weight, float SmoothingWeight);

public sealed record Gw2AimIkConfig(
    ulong Name,
    ulong BoneEye,
    ulong BoneSight,
    ulong EndEffector,
    float FrustumHorizontalAngle,
    float FrustumVerticalAngle,
    float FrustumLength,
    float TargetVelocityConstraint,
    IReadOnlyList<Gw2AimIkBone> Bones,
    IReadOnlyList<ulong> Targets,
    uint Flags);

public sealed record Gw2IkChain(
    ulong Name,
    Vector3 SmoothWeight,
    float HyperExtensionStart,
    float HyperExtensionScale,
    IReadOnlyList<ulong> Bones,
    uint TargetMode,
    uint ChainType)
{
    public Gw2IkRaycastTarget? TargetRaycast { get; init; }
}

public sealed record Gw2IkRaycastTarget(uint Flags, Vector3 Direction);

public sealed record Gw2IkChainGroup(ulong Name, IReadOnlyList<Gw2IkChain> Chains);

public sealed class Gw2AnimConfig
{
    public Gw2AnimConfig(IReadOnlyList<Gw2AimIkConfig> aimIkConfigs, IReadOnlyList<Gw2IkChainGroup> chainGroups)
    {
        AimIkConfigs = aimIkConfigs;
        ChainGroups = chainGroups;
    }

    public IReadOnlyList<Gw2AimIkConfig> AimIkConfigs { get; }
    public IReadOnlyList<Gw2IkChainGroup> ChainGroups { get; }

    public static Gw2AnimConfig? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.FindRoot("PackAnimConfig");
        if (root is null) return null;

        var aim = new List<Gw2AimIkConfig>();
        if (root.Fields.GetValueOrDefault("aimIKConfigs") is object?[] aimObjects)
            foreach (var aimObject in aimObjects)
                if (aimObject is PackObject config)
                    aim.Add(new(
                        PackValue.AsU64(config.Fields.GetValueOrDefault("name")),
                        PackValue.AsU64(config.Fields.GetValueOrDefault("boneEye")),
                        PackValue.AsU64(config.Fields.GetValueOrDefault("boneSight")),
                        PackValue.AsU64(config.Fields.GetValueOrDefault("endEffector")),
                        PackValue.AsF32(config.Fields.GetValueOrDefault("frustumHAngle")),
                        PackValue.AsF32(config.Fields.GetValueOrDefault("frustumVAngle")),
                        PackValue.AsF32(config.Fields.GetValueOrDefault("frustumLength")),
                        PackValue.AsF32(config.Fields.GetValueOrDefault("targetVelocityConstraint")),
                        Bones(config.Fields.GetValueOrDefault("bones")),
                        PackValue.AsU64List(config.Fields.GetValueOrDefault("targets")),
                        PackValue.AsU32(config.Fields.GetValueOrDefault("flags"))));

        var groups = new List<Gw2IkChainGroup>();
        if (root.Fields.GetValueOrDefault("chainGroups") is object?[] groupObjects)
            foreach (var groupObject in groupObjects)
                if (groupObject is PackObject group)
                    groups.Add(new(PackValue.AsU64(group.Fields.GetValueOrDefault("name")), Chains(group.Fields.GetValueOrDefault("chains"))));

        return new(aim, groups);
    }

    private static IReadOnlyList<Gw2AimIkBone> Bones(object? value)
    {
        if (value is not object?[] values) return [];
        var result = new List<Gw2AimIkBone>(values.Length);
        foreach (var item in values)
            if (item is PackObject bone)
                result.Add(new(
                    PackValue.AsU64(bone.Fields.GetValueOrDefault("boneToken")),
                    PackValue.AsF32(bone.Fields.GetValueOrDefault("clampAngle")),
                    PackValue.AsF32(bone.Fields.GetValueOrDefault("weight")),
                    PackValue.AsF32(bone.Fields.GetValueOrDefault("smoothingWeight"))));
        return result;
    }

    private static IReadOnlyList<Gw2IkChain> Chains(object? value)
    {
        if (value is not object?[] values) return [];
        var result = new List<Gw2IkChain>(values.Length);
        foreach (var item in values)
            if (item is PackObject chain)
                result.Add(new(
                    PackValue.AsU64(chain.Fields.GetValueOrDefault("name")),
                    Vec3(chain, "smoothWeight"),
                    PackValue.AsF32(chain.Fields.GetValueOrDefault("hyperExtensionStart")),
                    PackValue.AsF32(chain.Fields.GetValueOrDefault("hyperExtensionScale")),
                    PackValue.AsU64List(chain.Fields.GetValueOrDefault("bones")),
                    PackValue.AsU32(chain.Fields.GetValueOrDefault("targetMode")),
                    PackValue.AsU32(chain.Fields.GetValueOrDefault("chainType")))
                {
                    TargetRaycast = chain.Fields.GetValueOrDefault("targetRaycastInfo") is PackObject target
                        ? new(PackValue.AsU32(target.Fields.GetValueOrDefault("flags")), Vec3(target, "direction"))
                        : null,
                });
        return result;
    }

    private static Vector3 Vec3(PackObject obj, string name) =>
        obj.Fields.GetValueOrDefault(name) is float[] values && values.Length >= 3
            ? new Vector3(values[0], values[1], values[2])
            : Vector3.Zero;
}
