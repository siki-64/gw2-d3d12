using System;
using System.Numerics;

namespace Gw2.Dat.Content;

public static class Gw2MapLightRange
{
    private static readonly float[] HalfExtentByShaderQuality = [15360f, 3072f, 9216f, 15360f, 15360f];

    public static float HalfExtent(Gw2ShaderQuality quality) =>
        (int)quality is var index && index >= 0 && index < HalfExtentByShaderQuality.Length ? HalfExtentByShaderQuality[index] : 0f;

    public static bool Contains(Vector3 eye, Vector3 lightPosition, float halfExtent) =>
        MathF.Abs(lightPosition.X - eye.X) <= halfExtent && MathF.Abs(lightPosition.Y - eye.Y) <= halfExtent;
}
