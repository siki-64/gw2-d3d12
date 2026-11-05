namespace Gw2.Dat.Content;

public sealed record Gw2EnvironmentAmbientOcclusion(float Amount, float Brighten, float Contrast, float SunScale)
{
    private const float FullDay = 1f;
    private const float FullWeight = 1f;
    private const float MaximumAmount = 5f;
    private const float MinimumContrast = 0.5f;
    private const float MaximumContrast = 1.5f;

    public static Gw2EnvironmentAmbientOcclusion WithoutEnvironment { get; } = new(3f, 0.2f, 1f, 0.5f);

    public static Gw2EnvironmentAmbientOcclusion FromEnvironment(Gw2MapEnvironment environment, float dayWeight = FullDay) =>
        BlendedOf(environment, dayWeight)?.Clamped() ?? WithoutEnvironment;

    public static Gw2EnvironmentAmbientOcclusion FromZones(Gw2MapEnvironment environment, IReadOnlyList<Gw2EnvironmentZoneWeight> zoneWeights, float dayWeight = FullDay)
    {
        ArgumentNullException.ThrowIfNull(zoneWeights);
        if (BlendedOf(environment, dayWeight) is not { } global) return WithoutEnvironment;
        var accumulated = global.Scaled(0f);
        var totalWeight = 0f;
        foreach (var zoneWeight in zoneWeights)
        {
            var zoneValues = zoneWeight.Zone.Data is { } zoneEnvironment ? BlendedOf(zoneEnvironment, dayWeight) ?? global : global;
            accumulated = accumulated.Plus(zoneValues.Scaled(zoneWeight.Weight));
            totalWeight += zoneWeight.Weight;
        }
        if (totalWeight < FullWeight)
        {
            accumulated = accumulated.Plus(global.Scaled(FullWeight - totalWeight));
            totalWeight = FullWeight;
        }
        return accumulated.Scaled(1f / totalWeight).Clamped();
    }

    public static Gw2EnvironmentAmbientOcclusion FromEffect(Gw2MapEnvEffect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        return new(effect.SsaoAmount, effect.SsaoBrighten, effect.SsaoContrast, effect.SsaoSunScale);
    }

    public bool IsActive => Amount > 0f;

    public float SunAmount => Amount * SunScale;

    private static Gw2EnvironmentAmbientOcclusion? BlendedOf(Gw2MapEnvironment environment, float dayWeight)
    {
        ArgumentNullException.ThrowIfNull(environment);
        if (environment.Effect is not { } day) return null;
        if (dayWeight >= FullDay || environment.NightEffect is not { } night) return FromEffect(day);
        if (dayWeight <= 0f) return FromEffect(night);
        return FromEffect(day).Scaled(dayWeight).Plus(FromEffect(night).Scaled(1f - dayWeight));
    }

    private Gw2EnvironmentAmbientOcclusion Clamped() => new(
        Math.Clamp(Amount, 0f, MaximumAmount),
        Math.Clamp(Brighten, 0f, 1f),
        Math.Clamp(Contrast, MinimumContrast, MaximumContrast),
        Math.Clamp(SunScale, 0f, 1f));

    private Gw2EnvironmentAmbientOcclusion Scaled(float weight) => new(Amount * weight, Brighten * weight, Contrast * weight, SunScale * weight);

    private Gw2EnvironmentAmbientOcclusion Plus(Gw2EnvironmentAmbientOcclusion other) =>
        new(Amount + other.Amount, Brighten + other.Brighten, Contrast + other.Contrast, SunScale + other.SunScale);
}
