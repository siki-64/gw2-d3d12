using System.Numerics;

namespace Gw2.Dat.Content;

public sealed record Gw2EnvironmentShLighting(
    Vector4 ShRed,
    Vector4 ShGreen,
    Vector4 ShBlue,
    Vector4 ShSunData,
    Vector4 ShSunColor,
    Vector4 CharLightCurve)
{
    private const float BandZeroWeight = 0.28209499f;
    private const float BandOneWeight = 0.488603f;
    private const float HighSunElevationDegrees = 50f;
    private static readonly Vector4 LowSunCharLightCurve = new(0.5f, 0.65f, 1f, 0f);
    private static readonly Vector4 HighSunCharLightCurve = new(0.62f, 0.76f, 1f, 0f);

    public static Gw2EnvironmentShLighting FromEnvironment(Gw2MapEnvironment environment, float dayWeight = Gw2EnvironmentLightBlend.FullDay) =>
        FromLighting(Gw2EnvironmentLightBlend.DayNight(environment, dayWeight));

    public static Gw2EnvironmentShLighting FromZones(Gw2MapEnvironment environment, IReadOnlyList<Gw2EnvironmentZoneWeight> zoneWeights, float dayWeight = Gw2EnvironmentLightBlend.FullDay) =>
        FromLighting(Gw2EnvironmentLightBlend.FromZones(environment, zoneWeights, dayWeight));

    public static Gw2EnvironmentShLighting FromLighting(Gw2EnvironmentLighting lighting)
    {
        var lights = lighting.Lights;
        var shRed = Vector4.Zero;
        var shGreen = Vector4.Zero;
        var shBlue = Vector4.Zero;
        foreach (var light in lights.Skip(1))
        {
            var basis = new Vector4(-light.Direction * BandOneWeight, BandZeroWeight) * light.Intensity;
            var color = light.Color;
            shRed += basis * color.X;
            shGreen += basis * color.Y;
            shBlue += basis * color.Z;
        }

        if (lights.Count == 0) return new(shRed, shGreen, shBlue, Vector4.Zero, Vector4.Zero, LowSunCharLightCurve);
        var sun = lights[0];
        var shadowInfluence = lighting.ShadowInfluence;
        var sunElevationDegrees = MathF.Asin(Math.Clamp(sun.Direction.Z, -1f, 1f)) * 180f / MathF.PI;
        return new(
            shRed,
            shGreen,
            shBlue,
            new Vector4(-sun.Direction, shadowInfluence),
            new Vector4(sun.Color * sun.Intensity, sun.Intensity),
            sunElevationDegrees >= HighSunElevationDegrees ? HighSunCharLightCurve : LowSunCharLightCurve);
    }
}
