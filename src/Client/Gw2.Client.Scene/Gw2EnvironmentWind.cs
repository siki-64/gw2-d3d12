using System;
using System.Numerics;
using Gw2.Dat.Content;

namespace Gw2.Client.Scene;

public readonly record struct Gw2WindSettings(
    float AzimuthDegrees,
    float ElevationDegrees,
    float Gust,
    float GustFrequency,
    float GustSpeed,
    float Turbulence,
    float Speed)
{
    private const float DegreesPerAngleStep = 360f / 256f;
    private const float SpeedPerStep = 7.8125f;
    private const float GustFrequencyPerStep = 0.0390625f;
    private const float GustSpeedPerStep = 0.0078125f;
    private const float TurbulencePerStep = 1f / 256f;

    public static Gw2WindSettings Of(Gw2MapEnvWind wind) => new(
        wind.Azimuth * DegreesPerAngleStep,
        wind.Elevation * DegreesPerAngleStep,
        wind.Gust * SpeedPerStep,
        wind.GustFreq * GustFrequencyPerStep,
        wind.GustSpeed * GustSpeedPerStep,
        wind.Noise * TurbulencePerStep,
        wind.Speed * SpeedPerStep);

    public Vector3 Direction
    {
        get
        {
            float azimuth = AzimuthDegrees * (MathF.PI / 180f);
            float elevation = ElevationDegrees * (MathF.PI / 180f);
            return new Vector3(MathF.Cos(azimuth) * MathF.Cos(elevation), MathF.Sin(azimuth) * MathF.Cos(elevation), MathF.Sin(elevation));
        }
    }
}

public sealed class Gw2EnvironmentWind
{
    private const float SmallestSpeed = 0.0001f;
    private const float GustNoiseFloor = 0.01f;

    public Vector3 Wander { get; private set; }
    public Vector3 Wind { get; private set; }
    public float GustLength { get; private set; }

    public static Vector3 WobbleAt(float wallClockSeconds) => NormalizeOrZero(new Vector3(
        MathF.Sin(wallClockSeconds * 3f),
        MathF.Sin(wallClockSeconds * 1.5f + 1.6f),
        MathF.Cos(wallClockSeconds)));

    public Vector3 Advance(Gw2WindSettings settings, float wallClockSeconds, float wanderBlend, float gustNoise)
    {
        Wander = NormalizeOrZero(Vector3.Lerp(Wander, WobbleAt(wallClockSeconds), wanderBlend));
        var direction = settings.Direction;
        var acrossWind = Wander - direction * (Vector3.Dot(direction, Wander) / direction.LengthSquared());
        var turbulent = NormalizeOrZero(direction + (acrossWind - direction) * settings.Turbulence);
        var wind = turbulent * MathF.Max(settings.Speed, SmallestSpeed);
        GustLength = (gustNoise + GustNoiseFloor) * settings.Gust;
        float length = wind.Length();
        if (length * length > 0.000001f) wind = wind / length * (length + GustLength);
        Wind = wind;
        return wind;
    }

    private static Vector3 NormalizeOrZero(Vector3 vector) =>
        vector.LengthSquared() >= 1e-12f ? Vector3.Normalize(vector) : Vector3.Zero;
}
