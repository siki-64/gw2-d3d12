using System;
using System.Numerics;
using Gw2.Client.Scene;

namespace Gw2.Client;

public sealed class Gw2OrbitCamera
{
    private static readonly Vector3 Up = new(0f, 0f, -1f);

    public float NearPlane { get; set; } = Gw2CameraProjection.NearPlaneOf(Gw2.Dat.Content.Gw2MapTerrain.DefaultViewSpan, Gw2WorldCameraMotion.DefaultFieldOfView);
    public const float FarPlane = 36864f;

    public Vector3 Target { get; set; }

    public float Distance { get; set; } = 200f;

    public float Yaw { get; set; } = -MathF.PI / 2f;

    public float Pitch { get; set; } = 0.15f;

    public float FieldOfView { get; set; } = Gw2WorldCameraMotion.DefaultFieldOfView;

    public Vector3 Eye => Target + Distance * new Vector3(
        MathF.Cos(Pitch) * MathF.Cos(Yaw),
        MathF.Cos(Pitch) * MathF.Sin(Yaw),
        -MathF.Sin(Pitch));

    public void Orbit(float yawDelta, float pitchDelta)
    {
        Yaw += yawDelta;
        Pitch = Math.Clamp(Pitch + pitchDelta, -1.5f, 1.5f);
    }

    public void LookFrom(Vector3 eye, Vector3 front)
    {
        var direction = Vector3.Normalize(front);
        Target = eye + Distance * direction;
        Yaw = MathF.Atan2(-direction.Y, -direction.X);
        Pitch = MathF.Asin(direction.Z);
    }

    public void Zoom(float factor) => Distance = Math.Clamp(Distance * factor, 10f, 10000f);

    public Matrix4x4 View => Matrix4x4.CreateLookAtLeftHanded(Eye, Target, Up);

    public Matrix4x4 Projection(float aspectRatio) =>
        Matrix4x4.CreatePerspectiveFieldOfViewLeftHanded(FieldOfView, aspectRatio, NearPlane, FarPlane);
}
