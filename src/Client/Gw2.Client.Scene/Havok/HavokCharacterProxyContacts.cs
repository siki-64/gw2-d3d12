using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using static Gw2.Client.Scene.Havok.HavokVector;

namespace Gw2.Client.Scene.Havok;

[StructLayout(LayoutKind.Explicit, Size = 64)]
public struct HavokContactPoint
{
    [FieldOffset(0)] public Vector128<float> Position;
    [FieldOffset(16)] public Vector128<float> Normal;
    [FieldOffset(32)] public long ProxyCollidable;
    [FieldOffset(40)] public int ProxyShapeKey;
    [FieldOffset(48)] public long Body;
    [FieldOffset(56)] public int ShapeKey;
}

public enum HavokMotionType : byte
{
    Keyframed = 4,
    Fixed = 5,
}

public struct HavokBodyMotion
{
    public HavokMotionType MotionType;
    public Vector128<float> CenterOfMass;
    public Vector128<float> PreviousCenterOfMass;
    public Vector128<float> SweptAngularVelocity;
    public Vector128<float> LinearVelocity;
    public Vector128<float> AngularVelocity;
}

public struct HavokProxySettings
{
    public float DynamicFriction;
    public float StaticFriction;
    public float ExtraUpStaticFriction;
    public float ExtraDownStaticFriction;
    public float KeepDistance;
    public float KeepContactTolerance;
    public float ContactAngleSensitivity;
    public float MaxCharacterSpeedForSolver;
    public float PenetrationRecoverySpeed;
    public int MaxCastIterations;
    public Vector128<float> Up;
}

public static class HavokCharacterProxyContacts
{
    private const float MinimumSlopeDot = 0.01f;
    private static readonly Vector128<float> XyzMask = Vector128.Create(-1, -1, -1, 0).AsSingle();

    public static void ConvertFractionToDistance(Span<HavokContactPoint> points, Vector128<float> displacement)
    {
        for (var index = points.Length - 1; index >= 0; index--)
        {
            ref var point = ref points[index];
            var normal = point.Normal;
            var distance = Splat(normal, 3);
            point.Position = Sse.Shuffle(point.Position, Sse.UnpackHigh(point.Position, distance), 196);
            var negativeApproach = Sse.Multiply(Sse.Subtract(Vector128<float>.Zero, Dot3(normal, displacement)), distance);
            point.Normal = Sse.Shuffle(normal, Sse.UnpackHigh(normal, negativeApproach), 196);
        }
    }

    public static HavokSurfaceConstraint Extract(in HavokProxySettings settings, in HavokContactPoint point, HavokBodyMotion? body, float deltaTime)
    {
        var constraint = new HavokSurfaceConstraint
        {
            Plane = point.Normal,
            StaticFriction = settings.StaticFriction,
            DynamicFriction = settings.DynamicFriction,
            ExtraUpStaticFriction = settings.ExtraUpStaticFriction,
            ExtraDownStaticFriction = settings.ExtraDownStaticFriction,
        };
        constraint.Plane = constraint.Plane.WithElement(3, constraint.Plane.GetElement(3) - settings.KeepDistance);
        if (body is { } motion)
        {
            Vector128<float> velocity;
            if ((byte)(motion.MotionType - 4) > 1)
            {
                var timeFactor = Splat(motion.CenterOfMass, 3);
                var angular = Sse.Multiply(motion.SweptAngularVelocity, timeFactor);
                var linear = Sse.Multiply(Sse.Subtract(motion.CenterOfMass, motion.PreviousCenterOfMass), timeFactor);
                var arm = Sse.Subtract(point.Position, motion.CenterOfMass);
                var spin = Sse.Subtract(Sse.Multiply(YZX(arm), angular), Sse.Multiply(YZX(angular), arm));
                velocity = Sse.Add(YZX(spin), linear);
            }
            else
            {
                var arm = Sse.Subtract(point.Position, motion.CenterOfMass);
                var spin = Sse.Subtract(Sse.Multiply(YZX(arm), motion.AngularVelocity), Sse.Multiply(YZX(motion.AngularVelocity), arm));
                velocity = Sse.Add(motion.LinearVelocity, YZX(spin));
            }
            constraint.Velocity = velocity;
            constraint.Plane = constraint.Plane.WithElement(3, constraint.Plane.GetElement(3) - Dot3Scalar(constraint.Plane, velocity) * deltaTime);
            if (motion.MotionType == HavokMotionType.Fixed) constraint.Priority = 2;
            if (motion.MotionType == HavokMotionType.Keyframed) constraint.Priority = 1;
        }
        var planeDistance = Splat(constraint.Plane, 3);
        if (0f - Epsilon > planeDistance.GetElement(0))
        {
            var recovery = Sse.Multiply(Sse.Multiply(Broadcast(settings.PenetrationRecoverySpeed), Sse.Subtract(Vector128<float>.Zero, planeDistance)), point.Normal);
            constraint.Velocity = Sse.Add(recovery, constraint.Velocity);
            constraint.Plane = Sse.And(constraint.Plane, XyzMask);
        }
        return constraint;
    }

    public static void AddMaxSlopePlane(float maxSlopeCosine, Vector128<float> up, int index, List<HavokSurfaceConstraint> constraints)
    {
        var slope = Dot3Scalar(constraints[index].Plane, up);
        if (slope <= MinimumSlopeDot || slope >= maxSlopeCosine) return;
        var copy = constraints[index];
        var tilted = Sse.Add(Sse.Multiply(Sse.Subtract(Vector128<float>.Zero, Broadcast(slope)), up), copy.Plane);
        var lengthSquared = Dot3(tilted, tilted);
        var estimate = Sse.ReciprocalSqrt(lengthSquared);
        var inverseLength = Sse.AndNot(
            Sse.CompareLessThanOrEqual(lengthSquared, Vector128<float>.Zero),
            Sse.Multiply(Sse.Subtract(Broadcast(3f), Sse.Multiply(Sse.Multiply(estimate, lengthSquared), estimate)), Sse.Multiply(estimate, Broadcast(0.5f))));
        copy.Plane = Sse.Multiply(inverseLength, tilted);
        constraints.Add(copy);
    }

    public static float ApplyCastFraction(float keepDistance, in HavokSimplexOutput output, in HavokContactPoint hit, Vector128<float> target, ref Vector128<float> position)
    {
        var lengthSquared = Dot3(output.Position, output.Position);
        var estimate = Sse.ReciprocalSqrt(lengthSquared);
        var length = Sse.AndNot(
            Sse.CompareLessThanOrEqual(lengthSquared, Vector128<float>.Zero),
            Sse.Multiply(Sse.Multiply(Sse.Subtract(Broadcast(3f), Sse.Multiply(Sse.Multiply(lengthSquared, estimate), estimate)), Sse.Multiply(estimate, Broadcast(0.5f))), lengthSquared));
        var approach = Dot3(hit.Normal, output.Position);
        if ((Sse.MoveMask(Sse.CompareScalarEqual(approach, Vector128<float>.Zero)) & 1) != 0) approach = Broadcast(Epsilon);
        var lengthEstimate = Sse.Reciprocal(length);
        var inverseLength = Sse.Multiply(Sse.Subtract(Broadcast(2f), Sse.Multiply(lengthEstimate, length)), lengthEstimate);
        var cosine = Sse.Subtract(Vector128<float>.Zero, Sse.Multiply(inverseLength, approach));
        var cosineEstimate = Sse.Reciprocal(cosine);
        var inverseCosine = Sse.Multiply(Sse.Subtract(Broadcast(2f), Sse.Multiply(cosine, cosineEstimate)), cosineEstimate);
        var fraction = Sse.Min(Broadcast(1f), Sse.Max(Vector128<float>.Zero,
            Sse.Subtract(Splat(hit.Position, 3), Sse.Multiply(inverseLength, Sse.Multiply(inverseCosine, Broadcast(keepDistance))))));
        position = Sse.Add(Sse.Multiply(Sse.Subtract(target, position), fraction), position);
        return fraction.GetElement(0) * output.DeltaTime;
    }
}
