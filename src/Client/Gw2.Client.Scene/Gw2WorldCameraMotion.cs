using System;
using System.Numerics;

namespace Gw2.Client.Scene;

public static class Gw2WorldCameraMotion
{
    public const float DefaultCentreRatePerSecond = 20f;
    public const float DefaultEyeRatePerSecond = 20f;
    public const float DefaultBehindCharacterTurnRatePerSecond = 1f;
    public const float FollowDistanceRatePerSecond = 1.5f;
    public const float SwingBehindIdleSeconds = 0.25f;
    public const float AnchorCentreHeight = 80f;
    public const float DefaultPlacementPitch = -0.2268928f;

    public const float DefaultFieldOfView = 0.87266463f;
    public const float DefaultMinimumDistance = 100f;
    public const float DefaultMaximumDistance = 600f;
    public const float DefaultDistance = (DefaultMinimumDistance + DefaultMaximumDistance) / 2f;
    public const float DefaultSettingsSmoothSeconds = 0.1f;
    public const float MouseLookRadiansPerPixel = 0.0020943950f;
    private const float RotationSpeedPercentOfUnitScale = 100f;

    public static float MouseLookScaleOf(float rotationSpeedPercent) => rotationSpeedPercent / RotationSpeedPercentOfUnitScale;

    public static float FieldOfViewOf(int degrees) => degrees * (MathF.PI / 180f);
    public const float DefaultPitchLimit = 1.2566371f;
    public const int DefaultZoomSpeedOption = 6;
    public const int NoSmoothing = 0;
    public const int CharacterSmoothing = 1;
    public const int WideSmoothing = 2;
    private const float CharacterSnapDistance = 5000f;
    private const float CharacterSnapDistanceWhenSnappingClose = 150f;
    private const float WideSnapDistance = 2000f;
    private const float ZoomDistancePerNotchAtSlowestOption = 83.333336f;
    private const int ZoomSpeedOptionCount = 7;

    public static float ZoomDistancePerNotch(int zoomSpeedOption) => ZoomDistancePerNotchAtSlowestOption / (ZoomSpeedOptionCount - zoomSpeedOption);

    public static float ZoomedDistance(float distance, float notches, int zoomSpeedOption, float minimumDistance, float maximumDistance) =>
        Math.Clamp(distance - notches * ZoomDistancePerNotch(zoomSpeedOption), minimumDistance, maximumDistance);

    private const float WrapWithoutModuloLimit = 3f * MathF.PI;

    public static (float Value, float Velocity) SmoothDamp(float value, float target, float velocity, float smoothSeconds, float elapsedSeconds)
    {
        var omega = 2f / smoothSeconds;
        var x = omega * elapsedSeconds;
        var decay = 1f / (((x / 6f + 0.5f) * x + 1f) * x + 1f);
        var offset = value - target;
        var step = (offset * omega + velocity) * elapsedSeconds;
        return (target + (step + offset) * decay, (velocity - step * omega) * decay);
    }

    public static float WrapAngle(float angle)
    {
        if (angle < -WrapWithoutModuloLimit || angle > WrapWithoutModuloLimit)
            angle %= MathF.Tau;
        if (angle > MathF.PI) return angle - MathF.Tau;
        if (angle < -MathF.PI) return angle + MathF.Tau;
        return angle;
    }

    public static float TurnAngleToward(float from, float to, float ratePerSecond, float elapsedSeconds)
    {
        var fraction = Math.Clamp(ratePerSecond * elapsedSeconds, 0f, 1f);
        var start = WrapAngle(from);
        var end = WrapAngle(to);
        if (end - start > MathF.PI) start += MathF.Tau;
        else if (start - end > MathF.PI) end += MathF.Tau;
        return start + (end - start) * fraction;
    }

    public static float EaseDistance(float current, float desired, float ratePerSecond, float elapsedSeconds) =>
        current + (desired - current) * Math.Clamp(ratePerSecond * elapsedSeconds, 0f, 1f);

    public static float SnapDistanceOf(int smoothingMode, bool snapsCloseToTheCharacter) => smoothingMode switch
    {
        NoSmoothing => 0f,
        CharacterSmoothing => snapsCloseToTheCharacter ? CharacterSnapDistanceWhenSnappingClose : CharacterSnapDistance,
        WideSmoothing => WideSnapDistance,
        _ => throw new ArgumentOutOfRangeException(nameof(smoothingMode)),
    };

    public static Vector3 FollowCentre(Vector3 centre, Vector3 desiredCentre, float snapDistance, float ratePerSecond, float elapsedSeconds) =>
        Vector3.DistanceSquared(centre, desiredCentre) >= snapDistance * snapDistance ? desiredCentre : Ease(centre, desiredCentre, ratePerSecond, elapsedSeconds);

    public static Vector3 Ease(Vector3 current, Vector3 desired, float ratePerSecond, float elapsedSeconds) =>
        current + (desired - current) * Math.Clamp(ratePerSecond * elapsedSeconds, 0f, 1f);

    public const float AgentAnchorHeightScale = 0.8f;
    public const float MinimumAgentAnchorHeight = 40f;

    public static Vector3 AgentAnchorOf(Vector3 agentPosition, float agentHeight) =>
        agentPosition - new Vector3(0f, 0f, MathF.Max(MinimumAgentAnchorHeight, agentHeight * AgentAnchorHeightScale));

    public static (Vector3 Eye, Vector3 Centre) PlaceBehindAnchor(Vector3 anchor, float yaw, float distance)
    {
        var centre = anchor - new Vector3(0f, 0f, AnchorCentreHeight);
        var horizontal = MathF.Cos(DefaultPlacementPitch) * distance;
        var eye = new Vector3(
            centre.X - MathF.Cos(yaw) * horizontal,
            centre.Y - MathF.Sin(yaw) * horizontal,
            centre.Z + MathF.Sin(DefaultPlacementPitch) * distance);
        return (eye, centre);
    }
}
