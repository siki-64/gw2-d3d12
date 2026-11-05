using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Gw2.Dat.Content;

namespace Gw2.Client.Scene;

public sealed record Gw2PortraitLight(float YawDegrees, float PitchDegrees, Vector3 Colour)
{
    public Vector3 Direction
    {
        get
        {
            var yaw = YawDegrees * MathF.PI / 180f;
            var pitch = PitchDegrees * MathF.PI / 180f;
            return -new Vector3(MathF.Cos(pitch) * MathF.Cos(yaw), MathF.Cos(pitch) * MathF.Sin(yaw), MathF.Sin(pitch));
        }
    }
}

public enum Gw2PortraitSide
{
    Left = 0,
    Right = 1,
}

public sealed record Gw2PortraitCamera(Vector3 Eye, Vector3 Target, float FieldOfView)
{
    public const float NearPlane = 1f;
    public const float FarPlane = 5000f;
    public const int SmallAtlasMaximumSize = 128;
    public const int LargeTargetSize = 256;
    public const int SmallAtlasPackingStep = 32;
    public static readonly Vector3 DefaultEye = new(0f, -250f, 0f);
    public static readonly Vector3 Up = new(0f, 0f, -1f);

    public static readonly IReadOnlyList<Gw2PortraitLight> Lights =
    [
        new(52.258057f, 40f, new Vector3(0.905882f, 0.94902f, 1f)),
        new(-161.41936f, -40.064516f, new Vector3(0.72549f, 0.803922f, 1f)),
        new(180f, -89.9999f, new Vector3(1f, 1f, 1f)),
        new(121.93548f, 20.322582f, new Vector3(1f, 0.909804f, 0.866667f)),
        new(-47.6129f, 25f, new Vector3(1f, 0.909804f, 0.866667f)),
    ];

    public static ulong EyeBoneToken(Gw2PortraitSide side) => side == Gw2PortraitSide.Left ? 0x63423A2432A49F0UL : 0x93423A2432A49F0UL;

    public static ulong TargetBoneToken(Gw2PortraitSide side) => side == Gw2PortraitSide.Left ? 0x63521A2432A49F0UL : 0x93521A2432A49F0UL;

    public static ulong FieldOfViewPropertyToken(Gw2PortraitSide side) => side == Gw2PortraitSide.Left ? 0x59E663423UL : 0x59E693423UL;

    public static Gw2PortraitCamera FromSkeleton(Gw2ModelSkeleton skeleton, Gw2PortraitSide side, float fieldOfView)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        return FromModelPoints(BindPositionOf(skeleton, EyeBoneToken(side)), BindPositionOf(skeleton, TargetBoneToken(side)), fieldOfView);
    }

    private static Vector3? BindPositionOf(Gw2ModelSkeleton skeleton, ulong token)
    {
        var index = skeleton.FindBoneIndex(token);
        if (index < 0 || !Matrix4x4.Invert(skeleton.Bones[index].InverseWorld, out var world)) return null;
        return world.Translation;
    }

    public static Gw2PortraitCamera FromModelPoints(Vector3? eyePoint, Vector3? targetPoint, float fieldOfView) =>
        new(eyePoint ?? DefaultEye, targetPoint ?? Vector3.Zero, fieldOfView);

    public Matrix4x4 View => Matrix4x4.CreateLookAtLeftHanded(Eye, Target, Up);

    public Matrix4x4 Projection(int width, int height) =>
        Matrix4x4.CreatePerspectiveFieldOfViewLeftHanded(VerticalFieldOfView(width, height), (float)width / height, NearPlane, FarPlane);

    public float VerticalFieldOfView(int width, int height)
    {
        var aspect = (float)width / height;
        var halfDiagonal = MathF.Tan(FieldOfView * 0.5f);
        return 2f * MathF.Atan(halfDiagonal / MathF.Sqrt(aspect * aspect + 1f));
    }

    public static bool UsesLargeTarget(int width, int height) => width > SmallAtlasMaximumSize || height > SmallAtlasMaximumSize;

    public static IEnumerable<Vector3> LightDirections() => Lights.Select(light => light.Direction);
}
