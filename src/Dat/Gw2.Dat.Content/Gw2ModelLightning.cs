using Gw2.Formats;
using System.Numerics;

namespace Gw2.Dat.Content;

public sealed record Gw2ModelLightningSystem(ulong Bone, IReadOnlyList<ushort> BoltIndices, uint Flags, uint Fvf, uint MaterialIndex);

public sealed record Gw2ModelLightningBolt(
    ulong Bone,
    IReadOnlyList<ushort> NodeIndices,
    IReadOnlyList<uint> ColorBegin,
    IReadOnlyList<uint> ColorEnd,
    float ColorPeriod,
    Vector2 ColorFalloff,
    uint Flags,
    Gw2ParticleFlipbook? Flipbook,
    float Fps,
    float Frequency,
    uint GroupMax,
    uint GroupMin,
    float Probability,
    Vector2 Lifetime,
    ushort SegmentCount,
    Vector2 Opacity,
    byte OpacityPreset,
    float TextureOffset,
    Vector2 TextureRange,
    float TextureScale,
    float TextureSpeed,
    byte ThicknessPreset,
    Vector2 ThicknessRange,
    byte Type,
    float Variance,
    byte VariancePreset,
    float Noise);

public sealed record Gw2ModelLightningNode(
    ulong Bone,
    IReadOnlyList<ushort> ChildIndices,
    uint Flags,
    float Probability,
    Vector2 Radius,
    byte Shape,
    float UpdatePosition);

public sealed record Gw2ModelLightning(
    IReadOnlyList<Gw2ModelLightningSystem> Systems,
    IReadOnlyList<Gw2ModelLightningBolt> Bolts,
    IReadOnlyList<Gw2ModelLightningNode> Nodes)
{
    public static Gw2ModelLightning? Parse(PackObject modelRoot)
    {
        if (modelRoot.Fields.GetValueOrDefault("lightningData") is not PackObject lightning) return null;
        return new(
            PackValue.AsObjects(lightning.Fields.GetValueOrDefault("systems"))
                .Select(system => new Gw2ModelLightningSystem(
                    PackValue.RequireU64(system, "bone"),
                    PackValue.AsU16List(system.Fields.GetValueOrDefault("boltIndices")),
                    PackValue.RequireU32(system, "flags"),
                    PackValue.RequireU32(system, "fvf"),
                    PackValue.RequireU32(system, "materialIndex")))
                .ToArray(),
            PackValue.AsObjects(lightning.Fields.GetValueOrDefault("bolts")).Select(ParseBolt).ToArray(),
            PackValue.AsObjects(lightning.Fields.GetValueOrDefault("nodes"))
                .Select(node => new Gw2ModelLightningNode(
                    PackValue.RequireU64(node, "bone"),
                    PackValue.AsU16List(node.Fields.GetValueOrDefault("childrenIndices")),
                    PackValue.RequireU32(node, "flags"),
                    PackValue.RequireF32(node, "probability"),
                    Vec2(node, "radius"),
                    (byte)PackValue.RequireU32(node, "shape"),
                    PackValue.RequireF32(node, "updatePos")))
                .ToArray());
    }

    private static Gw2ModelLightningBolt ParseBolt(PackObject bolt) =>
        new(
            PackValue.RequireU64(bolt, "bone"),
            PackValue.AsU16List(bolt.Fields.GetValueOrDefault("nodeIndices")),
            PackValue.AsU32List(bolt.Fields.GetValueOrDefault("colorBegin")),
            PackValue.AsU32List(bolt.Fields.GetValueOrDefault("colorEnd")),
            PackValue.RequireF32(bolt, "colorPeriod"),
            Vec2(bolt, "colorFalloff"),
            PackValue.RequireU32(bolt, "flags"),
            bolt.Fields.GetValueOrDefault("flipbook") is PackObject flipbook
                ? new Gw2ParticleFlipbook(
                    PackValue.RequireU32(flipbook, "columns"),
                    PackValue.RequireU32(flipbook, "rows"),
                    PackValue.RequireU32(flipbook, "count"),
                    PackValue.RequireU32(flipbook, "start"),
                    PackValue.RequireF32(flipbook, "fps"))
                : null,
            PackValue.RequireF32(bolt, "fps"),
            PackValue.RequireF32(bolt, "frequency"),
            PackValue.RequireU32(bolt, "groupMax"),
            PackValue.RequireU32(bolt, "groupMin"),
            PackValue.RequireF32(bolt, "probability"),
            Vec2(bolt, "lifetime"),
            (ushort)PackValue.RequireU32(bolt, "numSegments"),
            Vec2(bolt, "opacity"),
            (byte)PackValue.RequireU32(bolt, "opacityPreset"),
            PackValue.RequireF32(bolt, "texOffset"),
            Vec2(bolt, "texRange"),
            PackValue.RequireF32(bolt, "texScale"),
            PackValue.RequireF32(bolt, "texSpeed"),
            (byte)PackValue.RequireU32(bolt, "thicknessPreset"),
            Vec2(bolt, "thicknessRange"),
            (byte)PackValue.RequireU32(bolt, "type"),
            PackValue.RequireF32(bolt, "variance"),
            (byte)PackValue.RequireU32(bolt, "variancePreset"),
            PackValue.RequireF32(bolt, "noise"));

    private static Vector2 Vec2(PackObject obj, string name) =>
        obj.Fields.GetValueOrDefault(name) is float[] { Length: >= 2 } values ? new Vector2(values[0], values[1]) : Vector2.Zero;
}
