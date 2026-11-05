using System.Numerics;

namespace Gw2.Dat.Content;

public readonly record struct Gw2SpotLightFrame(
    Vector3 Position,
    Vector3 Direction,
    Vector3 Color,
    float Intensity,
    float NearDistance,
    float FarDistance,
    float InnerAngle,
    float OuterAngle,
    bool Enabled)
{
    public Vector3 ScaledColor => Color * Intensity;
}

public sealed class Gw2MapSpotLightAnimation
{
    private readonly Gw2MapSpotLight _day;
    private readonly Gw2MapSpotLight _night;
    private readonly Gw2MapLightIntensity _intensity;

    public Gw2MapSpotLightAnimation(Gw2MapLightGroup<Gw2MapSpotLight> group)
    {
        ArgumentNullException.ThrowIfNull(group);
        if (group.Lights.Count != 2) throw new ArgumentException("A map spot light group holds a day and a night light.", nameof(group));

        _day = group.Lights[0];
        _night = group.Lights[1];
        _intensity = new Gw2MapLightIntensity(group.Curves, _night.Flags);
    }

    public Vector3 Position => _day.Position;

    public Gw2SpotLightFrame Advance(float dayWeight, float mapLightSeconds, float elapsedSeconds)
    {
        var towardNight = 1f - dayWeight;
        var baseIntensity = Gw2MapLightIntensity.Lerp(_day.Intensity, _night.Intensity, towardNight);
        var direction = Vector3.Lerp(_day.Direction, _night.Direction, towardNight);
        return new Gw2SpotLightFrame(
            _day.Position,
            direction.LengthSquared() >= 1e-12f ? Vector3.Normalize(direction) : Vector3.Zero,
            Vector3.Lerp(Gw2MapLightIntensity.ColorOf(_day.Color), Gw2MapLightIntensity.ColorOf(_night.Color), towardNight),
            _intensity.Advance(baseIntensity, mapLightSeconds, elapsedSeconds),
            Gw2MapLightIntensity.Lerp(_day.NearDistance, _night.NearDistance, towardNight),
            Gw2MapLightIntensity.Lerp(_day.FarDistance, _night.FarDistance, towardNight),
            Gw2MapLightIntensity.Lerp(_day.InnerAngle, _night.InnerAngle, towardNight),
            Gw2MapLightIntensity.Lerp(_day.OuterAngle, _night.OuterAngle, towardNight),
            Gw2MapLightIntensity.IsEnabled(baseIntensity));
    }
}
