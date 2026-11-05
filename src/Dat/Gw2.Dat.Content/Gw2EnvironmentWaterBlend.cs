namespace Gw2.Dat.Content;

public static class Gw2EnvironmentWaterBlend
{
    private const float FullWeight = 1f;

    public static Gw2MapEnvWaterInfo? DayNight(Gw2MapEnvironment environment, float dayWeight)
    {
        ArgumentNullException.ThrowIfNull(environment);
        if (environment.Water is not { } day) return null;
        if (dayWeight == Gw2EnvironmentLightBlend.FullDay || environment.NightWater is not { } night) return day;
        if (dayWeight == Gw2EnvironmentLightBlend.FullNight) return night;
        return Blend(day, night, dayWeight);
    }

    public static Gw2MapEnvWaterInfo? FromZones(Gw2MapEnvironment environment, IReadOnlyList<Gw2EnvironmentZoneWeight> zoneWeights, float dayWeight)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(zoneWeights);
        if (DayNight(environment, dayWeight) is not { } global) return null;
        var entries = new List<(Gw2MapEnvWaterInfo Water, float Weight)>();
        var totalWeight = 0f;
        foreach (var zoneWeight in zoneWeights)
        {
            var zoneWater = zoneWeight.Zone.Data is { } zoneEnvironment ? DayNight(zoneEnvironment, dayWeight) ?? global : global;
            entries.Add((zoneWater, zoneWeight.Weight));
            totalWeight += zoneWeight.Weight;
        }
        if (entries.Count == 0) return global;
        if (totalWeight < FullWeight) entries.Add((global, FullWeight - totalWeight));
        return Accumulate(entries);
    }

    public static Gw2MapEnvWaterInfo Blend(Gw2MapEnvWaterInfo day, Gw2MapEnvWaterInfo night, float dayWeight)
    {
        ArgumentNullException.ThrowIfNull(day);
        ArgumentNullException.ThrowIfNull(night);
        return Accumulate([(day, dayWeight), (night, 1f - dayWeight)]);
    }

    private static Gw2MapEnvWaterInfo Accumulate(IReadOnlyList<(Gw2MapEnvWaterInfo Water, float Weight)> entries)
    {
        var heavier = entries[0];
        var lastWeighted = entries[0].Water;
        var totalWeight = 0f;
        foreach (var entry in entries)
        {
            if (entry.Weight > heavier.Weight) heavier = entry;
            if (entry.Weight > 0f) lastWeighted = entry.Water;
            totalWeight += entry.Weight;
        }
        var inverseTotal = 1f / totalWeight;
        float Mix(Func<Gw2MapEnvWaterInfo, float> field) => entries.Sum(entry => field(entry.Water) * entry.Weight) * inverseTotal;
        float[] MixPair(Func<Gw2MapEnvWaterInfo, float[]> field) => [Mix(water => field(water)[0]), Mix(water => field(water)[1])];
        byte[] MixColor(Func<Gw2MapEnvWaterInfo, byte[]> field) =>
            Enumerable.Range(0, entries.Max(entry => field(entry.Water).Length))
                .Select(channel => ColorByte(Mix(water => Channel(field(water), channel))))
                .ToArray();
        return heavier.Water with
        {
            BumpAmount = Mix(water => water.BumpAmount),
            BumpAngles = MixPair(water => water.BumpAngles),
            BumpScales = MixPair(water => water.BumpScales),
            BumpSpeeds = MixPair(water => water.BumpSpeeds),
            BumpTiles = MixPair(water => water.BumpTiles),
            ShallowColor = MixColor(water => water.ShallowColor),
            DeepColor = MixColor(water => water.DeepColor),
            PatternColor = MixColor(water => water.PatternColor),
            SurfaceFresnel = Mix(water => water.SurfaceFresnel),
            DistortAmount = Mix(water => water.DistortAmount),
            FoamSpawn = Mix(water => water.FoamSpawn),
            FoamDissolve = Mix(water => water.FoamDissolve),
            FoamDepthAttenuation = Mix(water => water.FoamDepthAttenuation),
            FoamColor0 = MixColor(water => water.FoamColor0),
            FoamColor1 = MixColor(water => water.FoamColor1),
            MaterialFile = lastWeighted.MaterialFile,
            TextureFiles = lastWeighted.TextureFiles,
            ConstantTokens = lastWeighted.ConstantTokens,
            ConstantValues = lastWeighted.ConstantValues,
        };
    }

    private static float Channel(byte[] color, int channel) => channel < color.Length ? color[channel] / 255f : 0f;

    private static byte ColorByte(float value) => (byte)MathF.Round(Math.Clamp(value, 0f, 1f) * 255f);
}
