using System;

namespace Gw2.Client.Graphics;

public static class GrGamma
{
    public const float Minimum = 0.5f;
    public const float Maximum = 3.5f;
    public const float Neutral = 1f;
    private const float MetricScale = 65535f;
    private const float ShaderScale = 65536f;

    public static float Clamp(float gamma) => float.Clamp(gamma, Minimum, Maximum);

    public static float PresentScale(float gamma) => MathF.Round(Clamp(gamma) * MetricScale) / ShaderScale;
}
