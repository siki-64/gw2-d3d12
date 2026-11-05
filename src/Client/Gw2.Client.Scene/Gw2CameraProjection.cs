using System;

namespace Gw2.Client.Scene;

public static class Gw2CameraProjection
{
    public const float NearPerViewDistance = 2.4414062f * 0.00024414062f;
    public const float ReferenceFieldOfView = 0.87266463f;
    public const float MaximumNearPlane = 25f;

    public static float NearPlaneOf(float viewDistance, float fieldOfView)
    {
        var near = viewDistance * NearPerViewDistance * (ReferenceFieldOfView / fieldOfView);
        return Math.Clamp(near, 0f, MaximumNearPlane);
    }
}
