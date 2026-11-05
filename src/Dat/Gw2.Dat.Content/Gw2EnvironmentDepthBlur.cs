namespace Gw2.Dat.Content;

public sealed record Gw2EnvironmentDepthBlur(float FocalDepth, float FocalRange)
{
    private const float FullDay = 1f;
    private const float FullWeight = 1f;

    public static Gw2EnvironmentDepthBlur WithoutEffect { get; } = new(6000f, 4000f);

    public static Gw2EnvironmentDepthBlur FromEnvironment(Gw2MapEnvironment environment, float dayWeight = FullDay)
    {
        ArgumentNullException.ThrowIfNull(environment);
        if (environment.Effect is not { } day) return WithoutEffect;
        if (dayWeight >= FullDay || environment.NightEffect is not { } night) return FromEffect(day);
        if (dayWeight <= 0f) return FromEffect(night);
        return FromEffect(day).Scaled(dayWeight).Plus(FromEffect(night).Scaled(1f - dayWeight));
    }

    public static Gw2EnvironmentDepthBlur FromZones(Gw2MapEnvironment environment, IReadOnlyList<Gw2EnvironmentZoneWeight> zoneWeights, float dayWeight = FullDay)
    {
        ArgumentNullException.ThrowIfNull(zoneWeights);
        var global = FromEnvironment(environment, dayWeight);
        if (environment.Effect is null) return global;
        var accumulated = global.Scaled(0f);
        var totalWeight = 0f;
        foreach (var zoneWeight in zoneWeights)
        {
            var zoneBlur = zoneWeight.Zone.Data is { Effect: not null } zoneEnvironment ? FromEnvironment(zoneEnvironment, dayWeight) : global;
            accumulated = accumulated.Plus(zoneBlur.Scaled(zoneWeight.Weight));
            totalWeight += zoneWeight.Weight;
        }
        if (totalWeight < FullWeight)
        {
            accumulated = accumulated.Plus(global.Scaled(FullWeight - totalWeight));
            totalWeight = FullWeight;
        }
        return accumulated.Scaled(1f / totalWeight);
    }

    public static Gw2EnvironmentDepthBlur FromEffect(Gw2MapEnvEffect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        return new(effect.FocalDepth, effect.FocalRange);
    }

    private Gw2EnvironmentDepthBlur Scaled(float weight) => new(FocalDepth * weight, FocalRange * weight);

    private Gw2EnvironmentDepthBlur Plus(Gw2EnvironmentDepthBlur other) => new(FocalDepth + other.FocalDepth, FocalRange + other.FocalRange);
}
