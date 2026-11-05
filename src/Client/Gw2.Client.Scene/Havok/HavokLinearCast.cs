using System;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using static Gw2.Client.Scene.Havok.HavokVector;

namespace Gw2.Client.Scene.Havok;

[StructLayout(LayoutKind.Sequential)]
public struct HavokCdPoint
{
    public Vector128<float> Position;
    public Vector128<float> Normal;
    public Vector128<float> UnweldedNormal;
}

public struct HavokClosestCollector
{
    public float EarlyOutDistance;
    public bool HasHit;
    public HavokCdPoint Hit;

    public static HavokClosestCollector Create() => new()
    {
        EarlyOutDistance = BitConverter.Int32BitsToSingle(0x7F7FFFEE),
        Hit = new HavokCdPoint { Normal = Vector128.Create(0f, 0f, 0f, BitConverter.Int32BitsToSingle(0x7F7FFFEE)) },
    };

    public void Add(in HavokCdPoint point)
    {
        var distance = point.Normal.GetElement(3);
        if (HasHit && !(Hit.Normal.GetElement(3) > distance)) return;
        Hit = point;
        HasHit = true;
        EarlyOutDistance = distance;
    }
}

public struct HavokLinearCastSettings
{
    public float Tolerance;
    public Vector128<float> Path;
    public float MaxExtraPenetration;
    public float CastToleranceExtension;
    public float IterationEarlyOutDistance;
    public int MaxIterations;
}

public delegate void HavokClosestPointsQuery(in HavokTransform transformA, float tolerance, ref HavokClosestCollector collector);

public interface IHavokCdPointSink
{
    float EarlyOutDistance { get; }
    void BeginChild(uint shapeKey);
    void Add(in HavokCdPoint point);
}

public static class HavokLinearCast
{
    private static readonly float FarDistance = BitConverter.Int32BitsToSingle(0x7F7FFFEE);

    public static void CapsuleTriangle(ReadOnlySpan<Vector128<float>> capsuleLocal, float capsuleRadius, in HavokTransform capsuleTransform,
        ReadOnlySpan<Vector128<float>> triangleLocal, float triangleRadius, in HavokTransform triangleTransform, float tolerance, ref HavokClosestCollector collector)
    {
        var planes = HavokCapsuleTriangle.PrepareTriangle(triangleLocal[0], triangleLocal[1], triangleLocal[2]);
        Span<Vector128<float>> capsule = stackalloc Vector128<float>[2];
        Span<Vector128<float>> triangle = stackalloc Vector128<float>[3];
        for (var index = 1; index >= 0; index--) capsule[index] = capsuleTransform.Apply(capsuleLocal[index]);
        for (var index = 2; index >= 0; index--) triangle[index] = triangleTransform.Apply(triangleLocal[index]);
        Span<HavokContact> results = stackalloc HavokContact[3];
        HavokCapsuleTriangle.ClosestPoints(capsule, capsuleRadius, triangle, triangleRadius, planes, tolerance, results);
        var distance0 = results[0].Normal.GetElement(3);
        var distance1 = results[1].Normal.GetElement(3);
        int chosen;
        if (distance0 < tolerance && distance0 < distance1) chosen = 0;
        else if (!(distance0 < distance1) && distance1 < tolerance) chosen = 1;
        else return;
        collector.Add(new HavokCdPoint { Position = results[chosen].Position, Normal = results[chosen].Normal, UnweldedNormal = results[chosen].Normal });
    }

    public static void Cast<TSink>(in HavokTransform transformA, in HavokLinearCastSettings settings, HavokClosestPointsQuery closestPoints, ref TSink castSink, ref TSink startSink, bool hasStartSink)
        where TSink : IHavokCdPointSink
    {
        var first = HavokClosestCollector.Create();
        closestPoints(transformA, settings.Tolerance + settings.CastToleranceExtension, ref first);
        if (!first.HasHit) return;
        var normal = first.Hit.Normal;
        var position = first.Hit.Position;
        var startDistance = normal.GetElement(3);
        if (settings.Tolerance > startDistance && hasStartSink) startSink.Add(first.Hit);

        var path = settings.Path;
        var pathDot = Dot3(first.Hit.UnweldedNormal, path);
        var endDistance = Sse.Add(pathDot, Splat(normal, 3));
        if (!(endDistance.ToScalar() <= 0f && settings.MaxExtraPenetration + pathDot.ToScalar() < 0f)) return;

        if (!(settings.IterationEarlyOutDistance < startDistance))
        {
            if (startDistance <= 0f || startDistance <= castSink.EarlyOutDistance * (startDistance - endDistance.ToScalar()))
                castSink.Add(new HavokCdPoint { Position = position, Normal = WithW(normal, 0f), UnweldedNormal = WithW(normal, 0f) });
            return;
        }

        var span = Sse.Subtract(Splat(normal, 3), endDistance);
        var estimate = Sse.Reciprocal(span);
        var fraction = Sse.Multiply(Sse.Multiply(Sse.Subtract(Broadcast(2f), Sse.Multiply(span, estimate)), estimate), Splat(normal, 3));
        var reported = normal;
        var weldedNormal = WithW(Vector128<float>.Zero, FarDistance);
        var moved = transformA;
        for (var remaining = settings.MaxIterations - 1; remaining >= 0; remaining--)
        {
            moved.Translation = Sse.Add(Sse.Multiply(fraction, path), transformA.Translation);
            var collector = HavokClosestCollector.Create();
            closestPoints(moved, settings.Tolerance + settings.CastToleranceExtension, ref collector);
            if (!collector.HasHit) return;
            position = collector.Hit.Position;
            var stepDot = Dot3(collector.Hit.UnweldedNormal, path);
            reported = Sse.Shuffle(collector.Hit.UnweldedNormal, Sse.UnpackHigh(collector.Hit.UnweldedNormal, fraction), 196);
            if (stepDot.ToScalar() >= 0f) return;
            weldedNormal = collector.Hit.Normal;
            var approach = Sse.Subtract(Vector128<float>.Zero, stepDot);
            var distance = weldedNormal.GetElement(3);
            if (reported.GetElement(3) * approach.ToScalar() + distance > approach.ToScalar()) return;
            if (settings.IterationEarlyOutDistance >= distance) break;
            var approachEstimate = Sse.Reciprocal(approach);
            fraction = Sse.Add(fraction, Sse.Multiply(Sse.Multiply(Sse.Subtract(Broadcast(2f), Sse.Multiply(approach, approachEstimate)), approachEstimate), Splat(weldedNormal, 3)));
            if (fraction.ToScalar() > castSink.EarlyOutDistance) return;
        }
        castSink.Add(new HavokCdPoint { Position = position, Normal = reported, UnweldedNormal = reported });
    }

    private static Vector128<float> WithW(Vector128<float> xyz, float w) => xyz.WithElement(3, w);
}
