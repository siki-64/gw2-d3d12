namespace Gw2.Dat.Content;

public static class Gw2LocomotionSpeeds
{
    public const float UnitsPerMeter = 39.37f;

    public const float Walk = 80f;
    public const float Run = 294f;
    public const float CombatRun = 210f;
    public const float Strafe = 180f;
    public const float Backpedal = 105f;
    public const float Cap = 400f;

    public const float AuthoredWalk = 120f;
    public const float AuthoredRun = 240f;

    public static float MetersPerSecond(float unitsPerSecond) => unitsPerSecond / UnitsPerMeter;

    public static float AuthoredMetersPerSecond(float authoredUnitsPerSecond, float fallbackUnitsPerSecond) =>
        MetersPerSecond(authoredUnitsPerSecond > 0f ? authoredUnitsPerSecond : fallbackUnitsPerSecond);

    public static float ClipTimeScale(float authoredMetersPerSecond) =>
        authoredMetersPerSecond > 1e-4f ? 1f / authoredMetersPerSecond : 1f;
}
