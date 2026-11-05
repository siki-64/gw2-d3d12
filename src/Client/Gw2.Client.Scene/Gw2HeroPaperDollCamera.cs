using System;
using System.Numerics;

namespace Gw2.Client.Scene;

public sealed record Gw2HeroPaperDollCamera(float Yaw, float YawOffset, float FieldOfView, float FrustumScale, float EyeDistance, float VerticalOffset, float HeightOffset)
{
    public const float SceneDepth = -10000f;
    public const float HeightScale = 0.85f;
    public const float NearPlane = Gw2PortraitCamera.NearPlane;
    public const float FarPlane = Gw2PortraitCamera.FarPlane;

    public float ModelScale { get; init; } = 1f;

    public float Pitch { get; init; }

    public Vector3 Eye => new(0f, -EyeDistance, SceneDepth);

    public Vector3 Target => new(0f, 0f, SceneDepth);

    public Matrix4x4 World =>
        Matrix4x4.CreateScale(ModelScale) * Matrix4x4.CreateRotationX(Pitch) * Matrix4x4.CreateRotationZ(-Yaw - YawOffset) * Matrix4x4.CreateTranslation(0f, 0f, HeightOffset * HeightScale + SceneDepth);

    public Matrix4x4 View => Matrix4x4.CreateLookAtLeftHanded(Eye, Target, Gw2PortraitCamera.Up);

    public float HalfHeightTangent(int width, int height)
    {
        var aspect = (float)width / height;
        return MathF.Tan(FieldOfView * 0.5f) / MathF.Sqrt(aspect * aspect + 1f);
    }

    public Matrix4x4 Projection(int width, int height)
    {
        var halfHeight = HalfHeightTangent(width, height);
        var halfWidth = halfHeight * width / height;
        var shift = 2f * (VerticalOffset - 0.5f) * halfHeight;
        return Matrix4x4.CreatePerspectiveOffCenterLeftHanded(
            -halfWidth * NearPlane,
            halfWidth * NearPlane,
            (-halfHeight - shift) * NearPlane,
            (halfHeight - shift) * NearPlane,
            NearPlane,
            FarPlane);
    }
}
