using System.Numerics;

namespace Gw2.Dat.Content;

public sealed record Gw2EnvironmentBloom(Vector4 Glow, Vector4 GlowLevel, float GlowAmplify)
{
    private const float FullDay = 1f;
    private const float FullWeight = 1f;
    private const float GlowDeadZone = 1f / 512f;
    private const float AmplifyBias = 1f;

    public static Gw2EnvironmentBloom? FromEnvironment(Gw2MapEnvironment environment, float dayWeight = FullDay)
    {
        ArgumentNullException.ThrowIfNull(environment);
        if (environment.Effect is not { } day) return null;
        if (dayWeight >= FullDay || environment.NightEffect is not { } night) return FromEffect(day);
        if (dayWeight <= 0f) return FromEffect(night);
        return FromEffect(day).Scaled(dayWeight).Plus(FromEffect(night).Scaled(1f - dayWeight));
    }

    public static Gw2EnvironmentBloom? FromZones(Gw2MapEnvironment environment, IReadOnlyList<Gw2EnvironmentZoneWeight> zoneWeights, float dayWeight = FullDay)
    {
        ArgumentNullException.ThrowIfNull(zoneWeights);
        if (FromEnvironment(environment, dayWeight) is not { } global) return null;
        var accumulated = global.Scaled(0f);
        var totalWeight = 0f;
        foreach (var zoneWeight in zoneWeights)
        {
            var zoneBloom = zoneWeight.Zone.Data is { } zoneEnvironment ? FromEnvironment(zoneEnvironment, dayWeight) ?? global : global;
            accumulated = accumulated.Plus(zoneBloom.Scaled(zoneWeight.Weight));
            totalWeight += zoneWeight.Weight;
        }
        if (totalWeight < FullWeight)
        {
            accumulated = accumulated.Plus(global.Scaled(FullWeight - totalWeight));
            totalWeight = FullWeight;
        }
        return accumulated.Scaled(1f / totalWeight);
    }

    private Gw2EnvironmentBloom Scaled(float weight) => new(Glow * weight, GlowLevel * weight, GlowAmplify * weight);

    private Gw2EnvironmentBloom Plus(Gw2EnvironmentBloom other) => new(Glow + other.Glow, GlowLevel + other.GlowLevel, GlowAmplify + other.GlowAmplify);

    public static Gw2EnvironmentBloom FromEffect(Gw2MapEnvEffect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        return new(BgraColor(effect.Glow), BgraColor(effect.GlowLevel), effect.GlowAmplify);
    }

    public bool IsActive => Glow.X > GlowDeadZone || Glow.Y > GlowDeadZone || Glow.Z > GlowDeadZone;

    public Vector4 BloomAmountConstant => IsActive ? Glow : Vector4.Zero;

    public Vector4 DownsampleLevelConstant => GlowLevel;

    public float BlurGain => GlowAmplify + AmplifyBias;

    private static Vector4 BgraColor(byte[] bytes) =>
        bytes.Length < 4 ? Vector4.Zero : new Vector4(bytes[2], bytes[1], bytes[0], bytes[3]) / 255f;
}
