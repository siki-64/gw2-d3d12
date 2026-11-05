using System.Numerics;

namespace Gw2.Dat.Content;

public readonly record struct Gw2SkyDomeBlend(float Brightness, float HazeBottom, float HazeDensity, float HazeFalloff, float LightIntensity, float StarDensity);

public readonly record struct Gw2CloudLayerBlend(float Brightness, float Density, float Haze, float LightIntensity, Vector2 Velocity);

public sealed record Gw2CloudEnvironmentWeight(Gw2MapEnvironment Environment, float Weight);

public readonly record struct Gw2SkyCardBlend(float Density, float Brightness, float HazeDensity, float LightIntensity, float MinHaze, Vector2 Scale, float Azimuth, float Latitude, float Speed);

public static class Gw2EnvironmentSkyBlend
{
    private const int DayCloudAttributes = 0;
    private const int NightCloudAttributes = 1;
    private const float LatitudeToRadians = 1.570796f;
    private const float FullWeight = 1f;

    public static Gw2SkyDomeBlend SkyDome(Gw2MapEnvSkyDome dome, float dayWeight)
    {
        var hazeFalloffWeight = 1f - MathF.Sqrt(1f - dayWeight);
        return new(
            Lerp(dome.Night.Brightness, dome.Day.Brightness, dayWeight),
            Lerp(dome.Night.HazeBottom, dome.Day.HazeBottom, dayWeight),
            Lerp(dome.Night.HazeDensity, dome.Day.HazeDensity, dayWeight),
            Lerp(dome.Night.HazeFalloff, dome.Day.HazeFalloff, hazeFalloffWeight),
            Lerp(dome.Night.LightIntensity, dome.Day.LightIntensity, dayWeight),
            Lerp(dome.Night.StarDensity, dome.Day.StarDensity, dayWeight));
    }

    public static Gw2MapEnvSkyDome? SkyDomeFromZones(Gw2MapEnvironment environment, IReadOnlyList<Gw2EnvironmentZoneWeight> zoneWeights)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(zoneWeights);
        if (environment.Sky?.Dome is not { } global) return null;
        var accumulated = Scaled(global, 0f);
        var totalWeight = 0f;
        foreach (var zoneWeight in zoneWeights)
        {
            var zoneDome = zoneWeight.Zone.Data?.Sky?.Dome ?? global;
            accumulated = Plus(accumulated, Scaled(zoneDome, zoneWeight.Weight));
            totalWeight += zoneWeight.Weight;
        }

        if (totalWeight < FullWeight)
        {
            accumulated = Plus(accumulated, Scaled(global, FullWeight - totalWeight));
            totalWeight = FullWeight;
        }

        return Scaled(accumulated, 1f / totalWeight) with { Flags = global.Flags };
    }

    private static Gw2MapEnvSkyDome Scaled(Gw2MapEnvSkyDome dome, float weight) =>
        new(dome.Flags, Scaled(dome.Day, weight), Scaled(dome.Night, weight), dome.VerticalOffset * weight);

    private static Gw2MapEnvSkyPhase Scaled(Gw2MapEnvSkyPhase phase, float weight) =>
        new(phase.Brightness * weight, phase.HazeBottom * weight, phase.HazeDensity * weight, phase.HazeFalloff * weight, phase.LightIntensity * weight, phase.StarDensity * weight);

    private static Gw2MapEnvSkyDome Plus(Gw2MapEnvSkyDome first, Gw2MapEnvSkyDome second) =>
        new(first.Flags, Plus(first.Day, second.Day), Plus(first.Night, second.Night), first.VerticalOffset + second.VerticalOffset);

    private static Gw2MapEnvSkyPhase Plus(Gw2MapEnvSkyPhase first, Gw2MapEnvSkyPhase second) =>
        new(first.Brightness + second.Brightness, first.HazeBottom + second.HazeBottom, first.HazeDensity + second.HazeDensity, first.HazeFalloff + second.HazeFalloff, first.LightIntensity + second.LightIntensity, first.StarDensity + second.StarDensity);

    public static Gw2CloudLayerBlend? CloudLayer(Gw2MapEnvCloudLayer layer, float dayWeight)
    {
        if (layer.Attributes.Count <= NightCloudAttributes) return null;
        var day = layer.Attributes[DayCloudAttributes];
        var night = layer.Attributes[NightCloudAttributes];
        return new(
            Lerp(night.Brightness, day.Brightness, dayWeight),
            Lerp(night.Density, day.Density, dayWeight),
            Lerp(night.Haze, day.Haze, dayWeight),
            Lerp(night.LightIntensity, day.LightIntensity, dayWeight),
            Vector2.Lerp(night.Velocity, day.Velocity, dayWeight));
    }

    public static IReadOnlyList<Gw2CloudEnvironmentWeight> CloudEnvironmentsFromZones(Gw2MapEnvironment environment, IReadOnlyList<Gw2EnvironmentZoneWeight> zoneWeights)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(zoneWeights);
        var cloudEnvironments = new List<Gw2CloudEnvironmentWeight>();
        var zoneWeightTotal = 0f;
        foreach (var zoneWeight in zoneWeights)
        {
            zoneWeightTotal += zoneWeight.Weight;
            if (zoneWeight.Weight > 0f && zoneWeight.Zone.Data is { Ambience.CloudLayers.Count: > 0 } zoneEnvironment)
                cloudEnvironments.Add(new(zoneEnvironment, zoneWeight.Weight));
        }

        if (zoneWeightTotal < FullWeight) cloudEnvironments.Insert(0, new(environment, FullWeight - zoneWeightTotal));
        return cloudEnvironments;
    }

    public static Gw2CloudLayerBlend WeightedByEnvironment(Gw2CloudLayerBlend layer, float environmentWeight) =>
        layer with { Density = layer.Density * environmentWeight };

    public static Gw2SkyCardBlend? SkyCard(Gw2MapEnvSkyCard card, float dayWeight)
    {
        if (card.Day is not { } day || card.Night is not { } night) return null;
        return new(
            Lerp(night.Density, day.Density, dayWeight),
            Lerp(night.Brightness, day.Brightness, dayWeight),
            Lerp(night.HazeDensity, day.HazeDensity, dayWeight),
            Lerp(night.LightIntensity, day.LightIntensity, dayWeight),
            Lerp(night.MinHaze, day.MinHaze, dayWeight),
            Vector2.Lerp(night.Scale, day.Scale, dayWeight),
            Lerp(night.Azimuth, day.Azimuth, dayWeight),
            Lerp(night.Latitude, day.Latitude, dayWeight) * LatitudeToRadians,
            Lerp(night.Speed, day.Speed, dayWeight));
    }

    private static float Lerp(float night, float day, float dayWeight) => (day - night) * dayWeight + night;
}
