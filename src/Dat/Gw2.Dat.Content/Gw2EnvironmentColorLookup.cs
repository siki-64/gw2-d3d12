namespace Gw2.Dat.Content;

public static class Gw2EnvironmentColorLookup
{
    private const float FullDay = 1f;
    private const float FullWeight = 1f;

    public static IReadOnlyList<(uint? ClutFileId, float Weight)> WeightsFromZones(Gw2MapEnvironment environment, IReadOnlyList<Gw2EnvironmentZoneWeight> zoneWeights, float dayWeight = FullDay)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(zoneWeights);
        var weights = new Dictionary<uint, float>();
        var identityWeight = 0f;
        var totalZoneWeight = 0f;
        foreach (var zoneWeight in zoneWeights)
        {
            var zoneEnvironment = zoneWeight.Zone.Data is { Effect: not null } data ? data : environment;
            Add(zoneEnvironment, zoneWeight.Weight);
            totalZoneWeight += zoneWeight.Weight;
        }
        if (totalZoneWeight < FullWeight) Add(environment, FullWeight - totalZoneWeight);
        var result = weights.Select(pair => ((uint?)pair.Key, pair.Value)).ToList();
        if (identityWeight > 0f) result.Add((null, identityWeight));
        return result;

        void Add(Gw2MapEnvironment source, float weight)
        {
            if (dayWeight > 0f) AddEffect(source.Effect, weight * Math.Min(dayWeight, FullDay));
            if (dayWeight < FullDay) AddEffect(source.NightEffect ?? source.Effect, weight * (FullDay - Math.Max(dayWeight, 0f)));
        }

        void AddEffect(Gw2MapEnvEffect? effect, float weight)
        {
            if (weight <= 0f) return;
            if (effect is not null && effect.ClutTexture.TryGetFileId(out var fileId)) weights[fileId] = weights.GetValueOrDefault(fileId) + weight;
            else identityWeight += weight;
        }
    }
}
