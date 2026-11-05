using System.Numerics;

namespace Gw2.Dat.Content;

public readonly record struct Gw2PointLightFrame(Vector3 Position, Vector3 Color, float Intensity, float NearDistance, float FarDistance, bool Enabled)
{
    public Vector3 ScaledColor => Color * Intensity;
}

public sealed class Gw2MapPointLightAnimation
{
    private readonly Gw2MapPointLight _day;
    private readonly Gw2MapPointLight _night;
    private readonly Gw2MapLightIntensity _intensity;

    public Gw2MapPointLightAnimation(Gw2MapLightGroup<Gw2MapPointLight> group)
    {
        ArgumentNullException.ThrowIfNull(group);
        if (group.Lights.Count != 2) throw new ArgumentException("A map point light group holds a day and a night light.", nameof(group));

        _day = group.Lights[0];
        _night = group.Lights[1];
        _intensity = new Gw2MapLightIntensity(group.Curves, _night.Flags);
    }

    public Vector3 Position => _day.Position;

    public Gw2PointLightFrame Advance(float dayWeight, float mapLightSeconds, float elapsedSeconds)
    {
        var towardNight = 1f - dayWeight;
        var baseIntensity = Gw2MapLightIntensity.Lerp(_day.Intensity, _night.Intensity, towardNight);
        return new Gw2PointLightFrame(
            Vector3.Lerp(_day.Position, _night.Position, towardNight),
            Vector3.Lerp(Gw2MapLightIntensity.ColorOf(_day.Color), Gw2MapLightIntensity.ColorOf(_night.Color), towardNight),
            _intensity.Advance(baseIntensity, mapLightSeconds, elapsedSeconds),
            Gw2MapLightIntensity.Lerp(_day.NearDistance, _night.NearDistance, towardNight),
            Gw2MapLightIntensity.Lerp(_day.FarDistance, _night.FarDistance, towardNight),
            Gw2MapLightIntensity.IsEnabled(baseIntensity));
    }
}

public sealed class Gw2MapLightIntensity
{
    private const int CurveTypeMask = 0x3FFFF;
    private const uint NoiseCurveFlag = 0x40000;
    private const uint IgnoresCurveFlag = 2;
    private const int CurveShapeCount = 18;
    private const float DisabledIntensity = -1f;
    private const float DisabledTolerance = 0.0001f;

    private readonly Gw2MapLightCurve _curve;
    private readonly int _curveShape;
    private readonly bool _usesNoiseCurve;
    private readonly bool _ignoresCurve;
    private readonly Gw2LightNoise _noise = new();
    private float _gustPhase;

    public Gw2MapLightIntensity(IReadOnlyList<Gw2MapLightCurve> curves, uint lastLightFlags)
    {
        ArgumentNullException.ThrowIfNull(curves);
        _curve = curves.Count > 0 ? curves[0] : new Gw2MapLightCurve(0, 0, 0, 1, 0, 1, 0);
        _curveShape = CurveShapeOf(_curve.CurveType);
        if (_curveShape >= CurveShapeCount) throw new ArgumentException("Curve type is outside CURVE_MAPPING.", nameof(curves));
        _usesNoiseCurve = (_curve.CurveType & NoiseCurveFlag) != 0;
        _ignoresCurve = (lastLightFlags & IgnoresCurveFlag) != 0;
    }

    public static int CurveShapeOf(uint curveType)
    {
        var mask = curveType & CurveTypeMask;
        return mask == 0 ? 0 : 31 - System.Numerics.BitOperations.LeadingZeroCount(mask);
    }

    public static Vector3 ColorOf(byte[] blueGreenRed) =>
        blueGreenRed.Length >= 3 ? new Vector3(blueGreenRed[2], blueGreenRed[1], blueGreenRed[0]) / 255f : Vector3.One;

    public static bool IsEnabled(float baseIntensity) => MathF.Abs(baseIntensity - DisabledIntensity) >= DisabledTolerance;

    public static float Lerp(float from, float to, float amount) => (to - from) * amount + from;

    public float Advance(float baseIntensity, float mapLightSeconds, float elapsedSeconds) =>
        _ignoresCurve
            ? baseIntensity + 1f
            : _usesNoiseCurve
                ? AdvanceNoiseCurve(elapsedSeconds) * _curve.Amplitude + baseIntensity
                : (AdvanceCurve(mapLightSeconds, elapsedSeconds) + baseIntensity) * _curve.Amplitude;

    private float AdvanceNoiseCurve(float elapsedSeconds)
    {
        _noise.Smoothness = Math.Clamp(_curve.Phase, 0.0001f, 1f);
        _noise.Advance(elapsedSeconds * _curve.Gust);
        return _noise.Value;
    }

    private float AdvanceCurve(float mapLightSeconds, float elapsedSeconds)
    {
        var period = _curve.Phase;
        var timeInPeriod = (mapLightSeconds + _curve.Offset) % period;
        var cycle = timeInPeriod / period;
        var shape = CurveShape(_curveShape, cycle, timeInPeriod);
        var angle = cycle * MathF.PI;
        var wave = MathF.Sin(angle + angle) * MathF.Cos(angle + 0.7f) * _curve.Noise * 0.5f;

        var phase = _gustPhase;
        var shifted = phase * 1.357f + 1.8f;
        var slope = -((MathF.Sin(shifted) * MathF.Cos(phase) + MathF.Cos(phase - 1f) * 0.73692f) * 1.357f + MathF.Cos(shifted) * MathF.Sin(phase)) / 6f;
        if (slope < 0f) slope *= -0.5f;
        var speed = 0.01f - slope < 0f ? slope : 0.01f;
        phase = elapsedSeconds * _curve.GustFrequency * speed + phase;
        _gustPhase = phase;
        var gust = (MathF.Cos(phase * 1.357f + 1.8f) * MathF.Cos(phase) + MathF.Sin(1f - phase) + 3f) / 6f * _curve.Gust;
        return gust + wave + shape;
    }

    public static float CurveShape(int shape, float cycle, float timeInPeriod)
    {
        var t = cycle;
        var t2 = t * t;
        var t3 = t2 * t;
        var t4 = t3 * t;
        var bounce = t * -1.0347f + t2 * 21.174f - t3 * 40.278f + t4 * 20.139f;
        var rise = t2 * 4.8f + t * -0.6f - t3 * 3.2f;
        var pulse = 1f - MathF.Abs(t - 0.5f + (t - 0.5f));
        return shape switch
        {
            0 => 1f,
            1 => timeInPeriod,
            2 => bounce,
            3 => bounce * 0.75f + 0.25f,
            4 => bounce * 0.5f + 0.5f,
            5 => bounce * 0.25f + 0.75f,
            6 => t3 * 16.354f + (t * 8.0606003f - t2 * 20.059f) - t4 * 4.3561001f,
            7 => t3 * 42.667f + (t * 9.3332996f - t2 * 30.667f) - t4 * 21.333f,
            8 => t * 6.3333001f - t2 * 11.4f + t3 * 5.0667f,
            9 => t2,
            10 => t4,
            11 => rise,
            12 => rise * 0.75f + 0.25f,
            13 => rise * 0.5f + 0.5f,
            14 => rise * 0.25f + 0.75f,
            15 => pulse * pulse,
            16 => pulse * pulse * pulse * pulse,
            _ => pulse,
        };
    }
}

public sealed class Gw2LightNoise
{
    private const uint Seed = 123459876;
    private const float RandomScale = 4.6566129e-10f;

    private float _time;
    private uint _intervalSeed = Seed;
    private uint _startSeed = Seed;
    private float _nextFrequency;
    private float _frequency;
    private float _elapsedInInterval;

    public Gw2LightNoise()
    {
        _nextFrequency = NextRandom(ref _startSeed);
        _frequency = NextRandom(ref _startSeed);
        Value = 1f;
        Smoothness = 1f;
    }

    public float Value { get; private set; }

    public float Smoothness { get; set; }

    public void Advance(float step)
    {
        if (step < 0.000001f) return;

        var floor = MathF.Abs(Value) + 0.01f;
        var scaledStep = step * ((1f - floor) * Smoothness + floor);
        _time += scaledStep;
        _elapsedInInterval += scaledStep;
        if (_elapsedInInterval > _nextFrequency)
        {
            _frequency = _nextFrequency;
            _nextFrequency = NextRandom(ref _intervalSeed) + 0.2f;
            _elapsedInInterval = 0f;
        }

        var blend = SmoothStep(_elapsedInInterval, 0f, _nextFrequency);
        var from = Triangle(_time * _frequency);
        var to = Triangle(_time * _nextFrequency);
        Value = (to - from) * blend + from;
    }

    private static float NextRandom(ref uint seed)
    {
        var next = unchecked(48271u * seed - 0x7FFFFFFFu * (seed / 0xADC8u));
        if (next > 0x7FFFFFFFu) next = unchecked(next + 0x80000000u);
        seed = next == 0 ? Seed : next;
        return (int)seed * RandomScale;
    }

    private static float SmoothStep(float value, float from, float to)
    {
        if (from > value) return 0f;
        if (value >= to) return 1f;
        var t = (value - from) / (to - from);
        return (3f - (t + t)) * (t * t);
    }

    private static float Triangle(float value)
    {
        var fraction = value >= 0f ? value - (int)value : -(-value - (int)-value);
        var distance = MathF.Abs(fraction + fraction - 1f);
        return (3f - (distance + distance)) * (distance * distance);
    }
}
