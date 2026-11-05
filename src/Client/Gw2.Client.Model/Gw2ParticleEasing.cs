using Gw2.Dat.Content;
using Gw2.Dat.Archive;

namespace Gw2.Client.Model;

public static class Gw2ParticleEasing
{
    public static float Evaluate(uint preset, float t) => preset switch
    {
        0 => 1f,
        1 => t,
        2 => RiseAndSettle(t),
        3 => RiseAndSettle(t) * 0.75f + 0.25f,
        4 => RiseAndSettle(t) * 0.5f + 0.5f,
        5 => RiseAndSettle(t) * 0.25f + 0.75f,
        6 => 8.0606f * t - 20.059f * t * t + 16.354f * t * t * t - 4.3561f * t * t * t * t,
        7 => 9.3333f * t - 30.667f * t * t + 42.667f * t * t * t - 21.333f * t * t * t * t,
        8 => 6.3333f * t - 11.4f * t * t + 5.0667f * t * t * t,
        9 => t * t,
        10 => t * t * t * t,
        11 => EaseIn(t),
        12 => EaseIn(t) * 0.75f + 0.25f,
        13 => EaseIn(t) * 0.5f + 0.5f,
        14 => EaseIn(t) * 0.25f + 0.75f,
        15 => Tent(t) * Tent(t),
        16 => Tent(t) * Tent(t) * Tent(t) * Tent(t),
        17 => Tent(t),
        _ => 1f,
    };

    public static float Evaluate(Gw2ParticleCurve? curve, uint preset, bool inverted, float t)
    {
        if (curve is { Keys.Count: > 0 }) return EvaluateCurve(curve, t);
        var value = Evaluate(preset, t);
        return inverted ? 1f - value : value;
    }

    public static float EvaluateCurve(Gw2ParticleCurve curve, float t)
    {
        var keys = curve.Keys;
        if (keys.Count == 0) return 1f;
        if (t <= keys[0].X) return keys[0].Y;
        for (var index = 1; index < keys.Count; index++)
        {
            if (t > keys[index].X) continue;
            var span = keys[index].X - keys[index - 1].X;
            var blend = span <= 0f ? 1f : (t - keys[index - 1].X) / span;
            return keys[index - 1].Y + (keys[index].Y - keys[index - 1].Y) * blend;
        }

        return keys[^1].Y;
    }

    public static float Remap(float from, float to, float value, float low, float high)
    {
        if (from == to) return value > to ? high : low;
        var blend = Math.Clamp((value - from) / (to - from), 0f, 1f);
        return (high - low) * blend + low;
    }

    private static float RiseAndSettle(float t) =>
        -1.0347f * t + 21.174f * t * t - 40.278f * t * t * t + 20.139f * t * t * t * t;

    private static float EaseIn(float t) => -0.6f * t + 4.8f * t * t - 3.2f * t * t * t;

    private static float Tent(float t) => 1f - MathF.Abs((t - 0.5f) * 2f);
}
