using System.Numerics;

namespace Gw2.Dat.Content;

public readonly record struct Gw2FarShadowCombineTap(Vector2 Offset, float Weight);

public static class Gw2FarShadowCombine
{
    public const float WeightSum = 3f;

    public static Gw2FarShadowCombineTap[] Taps(int resolution)
    {
        var texel = 1f / resolution;
        return
        [
            new(new Vector2(-texel, 0f), 0.5f),
            new(new Vector2(0f, -texel), 0.5f),
            new(Vector2.Zero, 1f),
            new(new Vector2(texel, 0f), 0.5f),
            new(new Vector2(0f, texel), 0.5f),
        ];
    }

    public static Vector2 RingOffset(int ringColumn, int ringRow, int pagesPerSide) =>
        new((float)ringColumn / pagesPerSide, (float)ringRow / pagesPerSide);

    public static Vector2 Moments(ReadOnlySpan<float> dynamicDepths, ReadOnlySpan<float> bakedDepths)
    {
        var moments = Vector2.Zero;
        var taps = Taps(1);
        for (var tap = 0; tap < taps.Length; tap++)
        {
            var depth = MathF.Min(dynamicDepths[tap], bakedDepths[tap]);
            moments += taps[tap].Weight * new Vector2(depth, depth * depth);
        }
        return moments / WeightSum;
    }
}
