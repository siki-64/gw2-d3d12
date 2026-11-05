namespace Gw2.Dat.Content;

public sealed record Gw2EnvironmentLightAdaptation(
    float BrightTime,
    float DarkCoeff,
    float DarkExp,
    float DimTime,
    float DarkMin,
    float DarkMax,
    float BrightMin,
    float BrightMax,
    float BrightScale,
    float DarkScale)
{
    private const float FullWeight = 1f;

    public static Gw2EnvironmentLightAdaptation WithoutExposure { get; } = new(2.5f, 1.2f, 0.8f, 5f, 0.1f, 0.3f, 0.3f, 0.5f, 1f, 1f);

    public static Gw2EnvironmentLightAdaptation FromEnvironment(Gw2MapEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return environment.Exposure is { } exposure ? FromExposure(exposure) : WithoutExposure;
    }

    public static Gw2EnvironmentLightAdaptation FromZones(Gw2MapEnvironment environment, IReadOnlyList<Gw2EnvironmentZoneWeight> zoneWeights)
    {
        ArgumentNullException.ThrowIfNull(zoneWeights);
        var global = FromEnvironment(environment);
        var accumulated = global.Scaled(0f);
        var totalWeight = 0f;
        foreach (var zoneWeight in zoneWeights)
        {
            var zoneAdaptation = zoneWeight.Zone.Data is { Exposure: { } exposure } ? FromExposure(exposure) : global;
            accumulated = accumulated.Plus(zoneAdaptation.Scaled(zoneWeight.Weight));
            totalWeight += zoneWeight.Weight;
        }
        if (totalWeight < FullWeight)
        {
            accumulated = accumulated.Plus(global.Scaled(FullWeight - totalWeight));
            totalWeight = FullWeight;
        }
        return accumulated.Scaled(1f / totalWeight);
    }

    public static Gw2EnvironmentLightAdaptation FromExposure(Gw2MapEnvExposure exposure)
    {
        ArgumentNullException.ThrowIfNull(exposure);
        return new(exposure.BrightTime, exposure.DarkCoeff, exposure.DarkExp, exposure.DimTime, exposure.DarkMin, exposure.DarkMax, exposure.BrightMin, exposure.BrightMax, exposure.BrightScale, exposure.DarkScale);
    }

    private Gw2EnvironmentLightAdaptation Scaled(float weight) =>
        new(BrightTime * weight, DarkCoeff * weight, DarkExp * weight, DimTime * weight, DarkMin * weight, DarkMax * weight, BrightMin * weight, BrightMax * weight, BrightScale * weight, DarkScale * weight);

    private Gw2EnvironmentLightAdaptation Plus(Gw2EnvironmentLightAdaptation other) =>
        new(BrightTime + other.BrightTime, DarkCoeff + other.DarkCoeff, DarkExp + other.DarkExp, DimTime + other.DimTime, DarkMin + other.DarkMin, DarkMax + other.DarkMax,
            BrightMin + other.BrightMin, BrightMax + other.BrightMax, BrightScale + other.BrightScale, DarkScale + other.DarkScale);
}
