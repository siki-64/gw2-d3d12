using System.Numerics;

namespace Gw2.Dat.Content;

public readonly record struct Gw2EnvironmentLight(Vector3 Color, float Intensity, Vector3 Direction)
{
    public Vector3 ScaledColor => Color * Intensity;
}

public sealed record Gw2EnvironmentLighting(IReadOnlyList<Gw2EnvironmentLight> Lights, float ShadowInfluence);

public static class Gw2EnvironmentLightBlend
{
    public const float FullDay = 1f;
    public const float FullNight = 0f;
    private const int MaximumLights = 8;
    private const float FullWeight = 1f;

    public static Gw2EnvironmentLighting DayNight(Gw2MapEnvironment environment, float dayWeight)
    {
        var day = new Gw2EnvironmentLighting(Lights(environment.Lights), environment.Backlight?.ShadowInfluence ?? 0f);
        if (dayWeight == FullDay) return day;
        var night = new Gw2EnvironmentLighting(Lights(environment.NightLights), environment.NightBacklight?.ShadowInfluence ?? 0f);
        if (dayWeight == FullNight) return night;
        var nightWeight = 1f - dayWeight;
        var blended = new Gw2EnvironmentLight[MaximumLights];
        for (var index = 0; index < MaximumLights; index++)
        {
            var dayLight = index < day.Lights.Count ? day.Lights[index] : default;
            var nightLight = index < night.Lights.Count ? night.Lights[index] : default;
            blended[index] = new Gw2EnvironmentLight(
                dayLight.Color * dayWeight + nightLight.Color * nightWeight,
                dayLight.Intensity * dayWeight + nightLight.Intensity * nightWeight,
                dayLight.Direction * dayWeight + nightLight.Direction * nightWeight);
        }

        return new Gw2EnvironmentLighting(blended, day.ShadowInfluence * dayWeight + night.ShadowInfluence * nightWeight);
    }

    public static Gw2EnvironmentLighting FromZones(Gw2MapEnvironment environment, IReadOnlyList<Gw2EnvironmentZoneWeight> zoneWeights, float dayWeight)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(zoneWeights);
        var global = DayNight(environment, dayWeight);
        var accumulated = Scaled(global, 0f);
        var totalWeight = 0f;
        foreach (var zoneWeight in zoneWeights)
        {
            var zoneLighting = zoneWeight.Zone.Data is { Lights.Count: > 0 } zoneEnvironment ? DayNight(zoneEnvironment, dayWeight) : global;
            accumulated = Plus(accumulated, Scaled(zoneLighting, zoneWeight.Weight));
            totalWeight += zoneWeight.Weight;
        }

        if (totalWeight < FullWeight)
        {
            accumulated = Plus(accumulated, Scaled(global, FullWeight - totalWeight));
            totalWeight = FullWeight;
        }

        return Scaled(accumulated, 1f / totalWeight);
    }

    private static Gw2EnvironmentLighting Scaled(Gw2EnvironmentLighting lighting, float weight) =>
        new(lighting.Lights.Select(light => new Gw2EnvironmentLight(light.Color * weight, light.Intensity * weight, light.Direction * weight)).ToArray(), lighting.ShadowInfluence * weight);

    private static Gw2EnvironmentLighting Plus(Gw2EnvironmentLighting first, Gw2EnvironmentLighting second)
    {
        var lights = new Gw2EnvironmentLight[Math.Max(first.Lights.Count, second.Lights.Count)];
        for (var index = 0; index < lights.Length; index++)
        {
            var firstLight = index < first.Lights.Count ? first.Lights[index] : default;
            var secondLight = index < second.Lights.Count ? second.Lights[index] : default;
            lights[index] = new Gw2EnvironmentLight(firstLight.Color + secondLight.Color, firstLight.Intensity + secondLight.Intensity, firstLight.Direction + secondLight.Direction);
        }

        return new Gw2EnvironmentLighting(lights, first.ShadowInfluence + second.ShadowInfluence);
    }

    private static Gw2EnvironmentLight[] Lights(IReadOnlyList<Gw2MapLightInfo> lights) =>
        lights.Take(MaximumLights).Select(FromInfo).ToArray();

    private static Gw2EnvironmentLight FromInfo(Gw2MapLightInfo light) =>
        new(light.Color.Length < 3 ? Vector3.Zero : new Vector3(light.Color[2], light.Color[1], light.Color[0]) / 255f, light.Intensity, light.Direction);
}
