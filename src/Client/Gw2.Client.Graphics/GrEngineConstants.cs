using System;
using System.Numerics;

namespace Gw2.Client.Graphics;

public static class GrEngineConstants
{
    public static readonly Vector4 LightBuffer = new(0.25f, 4f, 1f / 128f, 128f);
    public static readonly Vector4 DefaultCharLightCurve = new(0f, 0.5f, 1f, 0f);
    public static readonly Vector4 BacklightColor = Vector4.Zero;
    public static readonly Vector4 BacklightDirection = Vector4.UnitX;
    public static readonly Vector4 BacklightShadowInfluence = Vector4.Zero;
    public static readonly Vector4 TexelOffset = new(0f, 0f, 1f, 0f);

    public static Vector4 StencilId(int stencilFlags) => new(stencilFlags / 255f);

    private const long ClockWindowFileTimeTicks = 288_000_000_000;
    private const float FileTimeTicksPerSecond = 10_000_000f;
    private const float TimePeriodSeconds = 100_000f;

    public static float ClockSeconds(System.DateTime utcNow) => utcNow.ToFileTimeUtc() % ClockWindowFileTimeTicks / FileTimeTicksPerSecond;

    public static Vector4 Time(float clockSeconds)
    {
        var seconds = clockSeconds % TimePeriodSeconds;
        var turns = seconds * MathF.Tau;
        return new Vector4(seconds, System.MathF.Sin(turns), System.MathF.Sin(turns / 3f), System.MathF.Sin(turns / 10f));
    }

    public static Vector4 ScreenDims(int width, int height, float near, float far) => new(1f / width, 1f / height, near, far);
}
