using System.Numerics;

namespace Gw2.Dat.Content;

public sealed record Gw2EnvironmentFog(
    Vector4 FogColorNearMinusFar,
    Vector4 FogColorFar,
    Vector4 FogColorHeight,
    Vector4 FogDepthCue,
    Vector4 FogParam0,
    Vector4 FogParam1)
{
    private const float FullDay = 1f;
    private const float FullWeight = 1f;

    public static Gw2EnvironmentFog? FromEnvironment(Gw2MapEnvironment environment, float dayWeight = FullDay)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return BlendOf(environment, dayWeight) is { } blend ? FromBlend(blend) : null;
    }

    public static Gw2EnvironmentFog? FromZones(Gw2MapEnvironment environment, IReadOnlyList<Gw2EnvironmentZoneWeight> zoneWeights, float dayWeight = FullDay)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(zoneWeights);
        if (BlendOf(environment, dayWeight) is not { } global) return null;
        var accumulated = global.Weighted(0f);
        var totalWeight = 0f;
        foreach (var zoneWeight in zoneWeights)
        {
            var zoneBlend = zoneWeight.Zone.Data is { } zoneEnvironment ? BlendOf(zoneEnvironment, dayWeight) ?? global : global;
            accumulated = accumulated.Plus(zoneBlend.Weighted(zoneWeight.Weight));
            totalWeight += zoneWeight.Weight;
        }

        if (totalWeight < FullWeight)
        {
            accumulated = accumulated.Plus(global.Weighted(FullWeight - totalWeight));
            totalWeight = FullWeight;
        }

        return FromBlend(accumulated.Weighted(1f / totalWeight));
    }

    private static HazeBlend? BlendOf(Gw2MapEnvironment environment, float dayWeight)
    {
        if (environment.Haze is not { } day) return null;
        if (dayWeight >= FullDay || environment.NightHaze is not { } night) return HazeBlend.Of(day);
        if (dayWeight <= 0f) return HazeBlend.Of(night);
        return HazeBlend.Of(day).Weighted(dayWeight).Plus(HazeBlend.Of(night).Weighted(1f - dayWeight));
    }

    public static Gw2EnvironmentFog FromHaze(Gw2MapHazeInfo haze) => FromBlend(HazeBlend.Of(haze));

    private static Gw2EnvironmentFog FromBlend(HazeBlend haze)
    {
        var (distanceScale, distanceBias) = RangeScaleAndBias(haze.DistanceRange);
        var (heightScale, heightBias) = RangeScaleAndBias(haze.HeightRange);
        return new(
            haze.NearColor - haze.FarColor,
            haze.FarColor,
            haze.HeightColor,
            new Vector4(haze.DepthCue),
            new Vector4(distanceScale, distanceBias, heightScale, heightBias),
            new Vector4(distanceScale, distanceBias, 0f, 0f));
    }

    private static (float Scale, float Bias) RangeScaleAndBias(Vector2? range)
    {
        if (range is not { } value) return (0f, 0f);
        var span = value.Y - value.X;
        if (span == 0f) return (0f, 0f);
        return (1f / span, -value.X / span);
    }

    private readonly record struct HazeBlend(Vector4 NearColor, Vector4 FarColor, Vector4 HeightColor, Vector2? DistanceRange, Vector2? HeightRange, float DepthCue)
    {
        public static HazeBlend Of(Gw2MapHazeInfo haze) => new(
            Color(haze.NearColor),
            Color(haze.FarColor),
            Color(haze.HeightColor),
            RangeOf(haze.DistanceRange),
            RangeOf(haze.HeightRange),
            haze.DepthCue);

        public HazeBlend Weighted(float weight) => new(
            NearColor * weight,
            FarColor * weight,
            HeightColor * weight,
            DistanceRange * weight,
            HeightRange * weight,
            DepthCue * weight);

        public HazeBlend Plus(HazeBlend other) => new(
            NearColor + other.NearColor,
            FarColor + other.FarColor,
            HeightColor + other.HeightColor,
            DistanceRange + other.DistanceRange,
            HeightRange + other.HeightRange,
            DepthCue + other.DepthCue);

        private static Vector2? RangeOf(float[] range) => range.Length < 2 ? null : new Vector2(range[0], range[1]);
    }

    private static Vector4 Color(byte[] blueGreenRedAlpha) =>
        blueGreenRedAlpha.Length < 4
            ? Vector4.Zero
            : new Vector4(blueGreenRedAlpha[2], blueGreenRedAlpha[1], blueGreenRedAlpha[0], blueGreenRedAlpha[3]) / 255f;
}
