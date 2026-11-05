using System.Numerics;

namespace Gw2.Dat.Content;

public sealed record Gw2EnvironmentGrade(float Saturation, float TintAmount, Vector3 TintColor, Vector3 TintTargetColor, float TintFocus)
{
    private const float NeutralSaturation = 1f;
    private const float SaturationScale = 0.98f;
    private const float SaturationBias = 0.008f;
    private const float SaturationDeadZone = 0.02f;
    private const float TintAmountDeadZone = 1f / 512f;
    private const float TintTargetFloor = 0.004f;

    public static Gw2EnvironmentGrade Neutral { get; } = new(NeutralSaturation, 0f, Vector3.One, Vector3.One, 0f);

    private const float FullDay = 1f;
    private const float FullWeight = 1f;

    public static Gw2EnvironmentGrade? FromEnvironment(Gw2MapEnvironment environment, float dayWeight = FullDay)
    {
        ArgumentNullException.ThrowIfNull(environment);
        if (environment.Effect is not { } day) return null;
        if (dayWeight >= FullDay || environment.NightEffect is not { } night) return FromEffect(day);
        if (dayWeight <= 0f) return FromEffect(night);
        return FromEffect(day).Scaled(dayWeight).Plus(FromEffect(night).Scaled(1f - dayWeight));
    }

    public static Gw2EnvironmentGrade? FromZones(Gw2MapEnvironment environment, IReadOnlyList<Gw2EnvironmentZoneWeight> zoneWeights, float dayWeight = FullDay)
    {
        ArgumentNullException.ThrowIfNull(zoneWeights);
        if (FromEnvironment(environment, dayWeight) is not { } global) return null;
        var accumulated = global.Scaled(0f);
        var totalWeight = 0f;
        foreach (var zoneWeight in zoneWeights)
        {
            var zoneGrade = zoneWeight.Zone.Data is { } zoneEnvironment ? FromEnvironment(zoneEnvironment, dayWeight) ?? global : global;
            accumulated = accumulated.Plus(zoneGrade.Scaled(zoneWeight.Weight));
            totalWeight += zoneWeight.Weight;
        }
        if (totalWeight < FullWeight)
        {
            accumulated = accumulated.Plus(global.Scaled(FullWeight - totalWeight));
            totalWeight = FullWeight;
        }
        return accumulated.Scaled(1f / totalWeight);
    }

    public static Gw2EnvironmentGrade FromEffect(Gw2MapEnvEffect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        return new(effect.Saturation, effect.TintAmount, BgrColor(effect.TintColor), BgrColor(effect.TintTargetColor), effect.TintFocus);
    }

    private Gw2EnvironmentGrade Scaled(float weight) =>
        new(Saturation * weight, TintAmount * weight, TintColor * weight, TintTargetColor * weight, TintFocus * weight);

    private Gw2EnvironmentGrade Plus(Gw2EnvironmentGrade other) =>
        new(Saturation + other.Saturation, TintAmount + other.TintAmount, TintColor + other.TintColor, TintTargetColor + other.TintTargetColor, TintFocus + other.TintFocus);

    public bool IsActive => MathF.Abs(TintAmount) > TintAmountDeadZone || MathF.Abs(Saturation - NeutralSaturation) > SaturationDeadZone;

    public bool AppliesSaturation => IsActive && Saturation != NeutralSaturation;

    public bool AppliesTint => IsActive && TintAmount != 0f;

    public Vector4 SaturationConstant => new(AppliesSaturation ? Saturation * SaturationScale + SaturationBias : NeutralSaturation);

    public Vector4 TintTargetConstant
    {
        get
        {
            var floored = Vector3.Max(TintTargetColor, new Vector3(TintTargetFloor));
            var mean = (floored.X + floored.Y + floored.Z) / 3f;
            return new(floored / mean, Math.Clamp(TintFocus, 0f, 1f));
        }
    }

    public Vector4 TintConstant => new(TintColor, AppliesTint ? TintAmount : 0f);

    private static Vector3 BgrColor(byte[] bytes) =>
        bytes.Length < 3 ? Vector3.One : new Vector3(bytes[2], bytes[1], bytes[0]) / 255f;
}
