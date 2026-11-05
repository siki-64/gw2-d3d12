using System.Numerics;

namespace Gw2.Dat.Content;

public sealed class Gw2EnvironmentDistortion
{
    public const float WaterAmountScale = 0.25f;
    public const float WrapSeconds = 3600f;
    private const float SixtyDegrees = 1.0471976f;

    public float Seconds { get; private set; }

    public void Advance(float elapsedSeconds)
    {
        Seconds += elapsedSeconds;
        if (Seconds > WrapSeconds) Seconds = 0f;
    }

    public static bool IsUnderwater(Vector3 eye, float waterSurfaceZ) => eye.Z > waterSurfaceZ;

    public static float BaseAmount(float underwaterDistortAmount, bool underwater) =>
        underwater ? underwaterDistortAmount * WaterAmountScale : 0f;

    public static Vector2 ViewOffset(Vector3 viewDirection, bool underwater)
    {
        if (!underwater) return Vector2.Zero;
        var yaw = -MathF.Atan2(viewDirection.Y, viewDirection.X) / SixtyDegrees;
        var pitch = MathF.Asin(Math.Clamp(viewDirection.Z, -1f, 1f)) / SixtyDegrees;
        return new Vector2(yaw + yaw, pitch + pitch);
    }

    public Vector4 Constant(float liveAmount, Vector2 viewOffset) => new(liveAmount, Seconds, viewOffset.X, viewOffset.Y);
}
