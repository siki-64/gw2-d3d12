using System;
using System.Collections.Generic;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using static Gw2.Client.Scene.Havok.HavokVector;

namespace Gw2.Client.Scene.Havok;

public delegate HavokBodyMotion? HavokBodyMotionLookup(long body);

public static class HavokCharacterManifold
{
    private const float SameSurfaceThreshold = 0.1f;
    private const float KeepPointThreshold = 1.1f;
    private const float VelocityWeight = 0.1f;
    private const float AngleWeight = 10f;
    private static readonly float FirstMinimumDistance = BitConverter.Int32BitsToSingle(0x7F7FFFEE);

    public static Vector128<float> PointVelocity(in HavokContactPoint point, HavokBodyMotionLookup motions)
    {
        if (motions(point.Body) is not { } motion) return Vector128<float>.Zero;
        var arm = Sse.Subtract(point.Position, motion.CenterOfMass);
        var spin = Sse.Subtract(Sse.Multiply(YZX(arm), motion.AngularVelocity), Sse.Multiply(YZX(motion.AngularVelocity), arm));
        return Sse.Add(YZX(spin), motion.LinearVelocity);
    }

    public static float SurfaceDistance(float contactAngleSensitivity, in HavokContactPoint first, in HavokContactPoint second, HavokBodyMotionLookup motions)
    {
        var distanceDifference = Sse.Subtract(Splat(first.Normal, 3), Splat(second.Normal, 3));
        var distanceTerm = Sse.Multiply(distanceDifference, distanceDifference);
        var angleTerm = Sse.Multiply(Sse.Subtract(Broadcast(1f), Dot3(second.Normal, first.Normal)), Broadcast(contactAngleSensitivity * contactAngleSensitivity));
        var velocityDifference = Sse.Subtract(PointVelocity(first, motions), PointVelocity(second, motions));
        var velocityTerm = Dot3(velocityDifference, velocityDifference);
        return Sse.Add(Sse.Add(Sse.Multiply(velocityTerm, Broadcast(VelocityWeight)), Sse.Multiply(Broadcast(AngleWeight), angleTerm)), distanceTerm).GetElement(0);
    }

    public static int FindSurface(float contactAngleSensitivity, in HavokContactPoint point, List<HavokContactPoint> manifold, HavokBodyMotionLookup motions)
    {
        var best = -1;
        var bestScore = SameSurfaceThreshold;
        var sensitivitySquared = contactAngleSensitivity * contactAngleSensitivity;
        var pointDistance = point.Normal.GetElement(3);
        var pointVelocity = PointVelocity(point, motions);
        for (var index = 0; index < manifold.Count; index++)
        {
            var candidate = manifold[index];
            var distanceDifference = pointDistance - candidate.Normal.GetElement(3);
            var distanceTerm = distanceDifference * distanceDifference;
            var cosine = Dot3Scalar(candidate.Normal, point.Normal);
            var velocityDifference = Sse.Subtract(pointVelocity, PointVelocity(candidate, motions));
            var velocityTerm = Dot3Scalar(velocityDifference, velocityDifference);
            var score = velocityTerm * VelocityWeight + AngleWeight * ((1f - cosine) * sensitivitySquared) + distanceTerm;
            if (score < bestScore)
            {
                bestScore = score;
                best = index;
            }
        }
        return best;
    }

    public static void Update(float contactAngleSensitivity, List<HavokContactPoint> manifold, ReadOnlySpan<HavokContactPoint> startHits, ReadOnlySpan<HavokContactPoint> castHits, HavokBodyMotionLookup motions)
    {
        var fresh = new List<HavokContactPoint>(startHits.Length);
        var minimumDistance = FirstMinimumDistance;
        foreach (var hit in startHits)
        {
            fresh.Add(hit);
            var distance = hit.Normal.GetElement(3);
            if (minimumDistance > distance) minimumDistance = distance;
        }

        for (var index = manifold.Count - 1; index >= 0; index--)
        {
            var best = -1;
            var bestScore = KeepPointThreshold;
            for (var candidate = 0; candidate < fresh.Count; candidate++)
            {
                var score = SurfaceDistance(contactAngleSensitivity, fresh[candidate], manifold[index], motions);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }
            if (best < 0)
            {
                RemoveBySwap(manifold, index);
                continue;
            }
            manifold[index] = fresh[best];
            RemoveBySwap(fresh, best);
        }

        foreach (var hit in fresh)
        {
            if (hit.Normal.GetElement(3) != minimumDistance) continue;
            var surface = FindSurface(contactAngleSensitivity, hit, manifold, motions);
            if (surface < 0) manifold.Add(hit);
            else manifold[surface] = hit;
        }

        if (castHits.Length > 0 && FindSurface(contactAngleSensitivity, castHits[0], manifold, motions) == -1) manifold.Add(castHits[0]);

        for (var index = manifold.Count - 1; index > 0; index--)
        {
            for (var earlier = index - 1; earlier >= 0; earlier--)
            {
                if (SurfaceDistance(contactAngleSensitivity, manifold[index], manifold[earlier], motions) >= SameSurfaceThreshold) continue;
                RemoveBySwap(manifold, index);
                break;
            }
        }
    }

    private static void RemoveBySwap(List<HavokContactPoint> points, int index)
    {
        var last = points.Count - 1;
        if (index != last) points[index] = points[last];
        points.RemoveAt(last);
    }
}
