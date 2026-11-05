using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using static Gw2.Client.Scene.Havok.HavokVector;

namespace Gw2.Client.Scene.Havok;

public sealed class HavokAllCollector : IHavokCdPointSink
{
    private static readonly float FarDistance = BitConverter.Int32BitsToSingle(0x7F7FFFEE);

    public List<HavokContactPoint> Hits { get; } = [];
    public long ProxyCollidable { get; set; }
    public long Body { get; set; }
    public int ShapeKey { get; private set; } = -1;
    public float EarlyOutDistance => FarDistance;

    public void Reset() => Hits.Clear();

    public void BeginChild(uint shapeKey) => ShapeKey = (int)shapeKey;

    public void Add(in HavokCdPoint point) => Hits.Add(new HavokContactPoint
    {
        Position = point.Position,
        Normal = point.Normal,
        ProxyCollidable = ProxyCollidable,
        ProxyShapeKey = -1,
        Body = Body,
        ShapeKey = ShapeKey,
    });

    public void SortByDistance()
    {
        if (Hits.Count > 1) QuickSort(Hits, 0, Hits.Count - 1);
    }

    private static void QuickSort(List<HavokContactPoint> hits, int first, int last)
    {
        var low = first;
        while (true)
        {
            var left = low;
            var right = last;
            var pivot = hits[(low + last) >> 1].Normal.GetElement(3);
            do
            {
                while (pivot > hits[left].Normal.GetElement(3)) left++;
                while (hits[right].Normal.GetElement(3) > pivot) right--;
                if (right < left) break;
                if (right != left) (hits[left], hits[right]) = (hits[right], hits[left]);
                left++;
                right--;
            }
            while (left <= right);
            if (low < right) QuickSort(hits, low, right);
            if (left >= last) return;
            low = left;
        }
    }
}

public interface IHavokPhantomWorld
{
    HavokBodyMotion? Motion(long body);
    void LinearCast(in HavokTransform phantomTransform, Vector128<float> to, float tolerance, float maxExtraPenetration, HavokAllCollector castCollector, HavokAllCollector? startCollector);
    float CollisionTolerance { get; }
    void ClosestPoints(in HavokTransform phantomTransform, float tolerance, HavokAllCollector collector);
}

public enum HavokSupportedState
{
    Unsupported = 0,
    Sliding = 1,
    Supported = 2,
}

public struct HavokSurfaceInfo
{
    public HavokSupportedState SupportedState;
    public Vector128<float> SurfaceNormal;
    public Vector128<float> SurfaceVelocity;
    public bool SurfaceIsDynamic;
}

public struct HavokCharacterProxyConfig
{
    public HavokProxySettings Settings;
    public float MaxSlopeCosine;
    public int UserPlanes;
}

public sealed class HavokCharacterProxy(HavokCharacterProxyConfig config, HavokTransform transform)
{
    private const float MaxExtraPenetration = 0.01f;
    private const float CastMoveThreshold = 0.001f;
    private const float SupportProbeTime = 1f / 60f;
    private const float SupportVelocityTolerance = 0.001f;
    private const float SupportNormalLimit = -0.08f;

    private readonly HavokAllCollector _castCollector = new();
    private readonly HavokAllCollector _startCollector = new();
    private readonly List<HavokSurfaceConstraint> _constraints = [];

    public readonly HavokCharacterProxyConfig Config = config;
    public HavokTransform Transform = transform;
    public Vector128<float> Velocity;
    public Vector128<float> OldDisplacement;
    public List<HavokContactPoint> Manifold { get; } = [];
    public bool RefreshManifoldInCheckSupport;

    public Vector128<float> Position
    {
        get => Transform.Translation;
        set => Transform.Translation = value;
    }

    public void Integrate(float deltaTime, IHavokPhantomWorld world)
    {
        var settings = Config.Settings;
        var startPointTolerance = settings.KeepContactTolerance + settings.KeepDistance;
        var position = Transform.Translation;
        var remaining = deltaTime;
        var output = new HavokSimplexOutput();
        HavokBodyMotionLookup motions = world.Motion;
        for (var iteration = 0; remaining > Epsilon && iteration < settings.MaxCastIterations; iteration++)
        {
            var target = Sse.Add(position, OldDisplacement);
            _castCollector.Reset();
            _startCollector.Reset();
            Transform.Translation = position;
            world.LinearCast(Transform, target, startPointTolerance, MaxExtraPenetration, _castCollector, _startCollector);

            var castHits = _castCollector.Hits;
            if (castHits.Count > 0)
            {
                _castCollector.SortByDistance();
                HavokCharacterProxyContacts.ConvertFractionToDistance(CollectionsMarshal.AsSpan(castHits), OldDisplacement);
            }
            HavokCharacterManifold.Update(settings.ContactAngleSensitivity, Manifold,
                CollectionsMarshal.AsSpan(_startCollector.Hits),
                CollectionsMarshal.AsSpan(castHits), motions);

            _constraints.Clear();
            var extractTime = deltaTime - remaining;
            for (var index = 0; index < Manifold.Count; index++) _constraints.Add(default);
            for (var index = 0; index < Manifold.Count; index++)
            {
                var point = Manifold[index];
                _constraints[index] = HavokCharacterProxyContacts.Extract(settings, point, motions(point.Body), extractTime);
                HavokCharacterProxyContacts.AddMaxSlopePlane(Config.MaxSlopeCosine, settings.Up, index, _constraints);
            }

            var input = new HavokSimplexInput
            {
                Position = Vector128<float>.Zero,
                Velocity = Velocity,
                MaxSurfaceVelocity = Vector128.Create(settings.MaxCharacterSpeedForSolver, settings.MaxCharacterSpeedForSolver, settings.MaxCharacterSpeedForSolver, 0f),
                UpVector = settings.Up,
                DeltaTime = remaining,
                MinDeltaTime = MinimumDeltaTime(Velocity, settings.KeepDistance),
            };
            Span<HavokSurfaceInteraction> interactions = stackalloc HavokSurfaceInteraction[_constraints.Count];
            output = default;
            HavokSimplexSolver.Solve(input, CollectionsMarshal.AsSpan(_constraints), interactions, ref output);

            var displacement = output.Position;
            if (!AnyXyzGreater(Sse.Subtract(OldDisplacement, displacement), Broadcast(CastMoveThreshold)))
            {
                remaining -= output.DeltaTime;
                position = Sse.Add(position, displacement);
                OldDisplacement = displacement;
                continue;
            }

            target = Sse.Add(position, displacement);
            _castCollector.Reset();
            Transform.Translation = position;
            world.LinearCast(Transform, target, startPointTolerance, MaxExtraPenetration, _castCollector, null);
            if (castHits.Count == 0 || !TakeNewSurface(castHits, displacement, motions, out var hit))
            {
                remaining -= output.DeltaTime;
                position = Sse.Add(position, displacement);
                OldDisplacement = displacement;
                continue;
            }

            Manifold.Add(hit);
            remaining -= HavokCharacterProxyContacts.ApplyCastFraction(settings.KeepDistance, output, hit, target, ref position);
            OldDisplacement = displacement;
        }
        Velocity = output.Velocity;
        Transform.Translation = position;
    }

    public HavokSurfaceInfo CheckSupport(Vector128<float> direction, IHavokPhantomWorld world)
    {
        var settings = Config.Settings;
        HavokBodyMotionLookup motions = world.Motion;
        if (RefreshManifoldInCheckSupport) RefreshManifold(world);

        _constraints.Clear();
        for (var index = 0; index < Manifold.Count; index++) _constraints.Add(default);
        for (var index = 0; index < Manifold.Count; index++)
        {
            var point = Manifold[index];
            _constraints[index] = HavokCharacterProxyContacts.Extract(settings, point, motions(point.Body), 0f);
            HavokCharacterProxyContacts.AddMaxSlopePlane(Config.MaxSlopeCosine, settings.Up, index, _constraints);
        }

        Span<Vector128<float>> surfaceVelocities = stackalloc Vector128<float>[_constraints.Count];
        var constraints = CollectionsMarshal.AsSpan(_constraints);
        for (var index = 0; index < constraints.Length; index++)
        {
            surfaceVelocities[index] = constraints[index].Velocity;
            constraints[index].Velocity = Vector128<float>.Zero;
        }

        var input = new HavokSimplexInput
        {
            Position = Vector128<float>.Zero,
            Velocity = direction,
            MaxSurfaceVelocity = Broadcast(settings.MaxCharacterSpeedForSolver),
            UpVector = settings.Up,
            DeltaTime = SupportProbeTime,
            MinDeltaTime = SupportProbeTime,
        };
        Span<HavokSurfaceInteraction> interactions = stackalloc HavokSurfaceInteraction[_constraints.Count];
        var output = new HavokSimplexOutput();
        HavokSimplexSolver.Solve(input, constraints, interactions, ref output);

        var surface = new HavokSurfaceInfo();
        if (!AnyXyzGreater(Sse.Subtract(output.Velocity, direction), Broadcast(SupportVelocityTolerance))) return surface;
        var lengthSquared = Dot3(output.Velocity, output.Velocity);
        if (lengthSquared.ToScalar() >= SupportVelocityTolerance)
        {
            var normalized = Sse.Multiply(output.Velocity, Sse.AndNot(Sse.CompareLessThanOrEqual(lengthSquared, Vector128<float>.Zero), InverseSquareRoot(lengthSquared)));
            var sine = Dot3Scalar(direction, normalized);
            var maxSlopeSquared = Config.MaxSlopeCosine * Config.MaxSlopeCosine;
            surface.SupportedState = maxSlopeSquared > 1f - sine * sine ? HavokSupportedState.Sliding : HavokSupportedState.Supported;
        }
        else
        {
            surface.SupportedState = HavokSupportedState.Supported;
        }

        var touching = 0;
        for (var index = 0; index < _constraints.Count; index++)
        {
            if (interactions[index].Touched == 0) continue;
            if (Dot3Scalar(_constraints[index].Plane, direction) >= SupportNormalLimit) continue;
            touching++;
            surface.SurfaceNormal = Sse.Add(_constraints[index].Plane, surface.SurfaceNormal);
            surface.SurfaceVelocity = Sse.Add(surface.SurfaceVelocity, surfaceVelocities[index]);
        }
        if (touching <= 0) return new HavokSurfaceInfo();
        var normalSum = surface.SurfaceNormal;
        var normalLengthSquared = Dot3(normalSum, normalSum);
        surface.SurfaceNormal = Sse.Multiply(Sse.AndNot(Sse.CompareLessThanOrEqual(normalLengthSquared, Vector128<float>.Zero), InverseSquareRoot(normalLengthSquared)), normalSum);
        surface.SurfaceVelocity = Sse.Multiply(Reciprocal(Broadcast((float)touching)), surface.SurfaceVelocity);
        return surface;
    }

    private void RefreshManifold(IHavokPhantomWorld world)
    {
        var settings = Config.Settings;
        _startCollector.Reset();
        world.ClosestPoints(Transform, world.CollisionTolerance + (settings.KeepContactTolerance + settings.KeepDistance), _startCollector);
        _castCollector.Reset();
        HavokCharacterManifold.Update(settings.ContactAngleSensitivity, Manifold,
            CollectionsMarshal.AsSpan(_startCollector.Hits), CollectionsMarshal.AsSpan(_castCollector.Hits), world.Motion);
    }

    private bool TakeNewSurface(List<HavokContactPoint> castHits, Vector128<float> displacement, HavokBodyMotionLookup motions, out HavokContactPoint hit)
    {
        _castCollector.SortByDistance();
        HavokCharacterProxyContacts.ConvertFractionToDistance(CollectionsMarshal.AsSpan(castHits), displacement);
        while (castHits.Count > 0)
        {
            if (HavokCharacterManifold.FindSurface(Config.Settings.ContactAngleSensitivity, castHits[0], Manifold, motions) == -1)
            {
                hit = castHits[0];
                return true;
            }
            castHits.RemoveAt(0);
        }
        hit = default;
        return false;
    }

    private static float MinimumDeltaTime(Vector128<float> velocity, float keepDistance)
    {
        var lengthSquared = Dot3(velocity, velocity);
        if ((Sse.MoveMask(Sse.CompareScalarEqual(lengthSquared, Vector128<float>.Zero)) & 1) != 0) return 0f;
        var inverseLength = Sse.AndNot(Sse.CompareLessThanOrEqual(lengthSquared, Vector128<float>.Zero), InverseSquareRoot(lengthSquared));
        return inverseLength.GetElement(0) * keepDistance * 0.5f;
    }
}

public struct HavokWorldBody
{
    public long Id;
    public IHavokTriangleCollection Collection;
    public HavokTransform Transform;
    public HavokBodyMotion? Motion;
    public int Layer;
}

public sealed class HavokCapsulePhantomWorld(Vector128<float>[] capsuleLocal, float capsuleRadius, long phantomCollidable) : IHavokPhantomWorld
{
    private readonly List<uint> _keys = [];

    public List<HavokWorldBody> Overlapping { get; } = [];
    public float IterationEarlyOutDistance { get; init; }
    public int MaxIterations { get; init; }

    public HavokBodyMotion? Motion(long body)
    {
        foreach (var candidate in Overlapping)
            if (candidate.Id == body) return candidate.Motion;
        return null;
    }

    public float CollisionTolerance { get; init; }

    public void ClosestPoints(in HavokTransform phantomTransform, float tolerance, HavokAllCollector collector)
    {
        var capsule = capsuleLocal;
        Span<Vector128<float>> triangle = stackalloc Vector128<float>[3];
        for (var index = Overlapping.Count - 1; index >= 0; index--)
        {
            var body = Overlapping[index];
            collector.ProxyCollidable = phantomCollidable;
            collector.Body = body.Id;
            var relative = HavokBvTreeCast.MultiplyInverse(body.Transform, phantomTransform);
            HavokBvTreeCast.CapsuleAabb(capsule, capsuleRadius, relative, tolerance, out var min, out var max);
            _keys.Clear();
            body.Collection.QueryAabb(min, max, _keys);
            foreach (var key in _keys)
            {
                body.Collection.Triangle(key, triangle);
                collector.BeginChild(key);
                var closest = HavokClosestCollector.Create();
                HavokLinearCast.CapsuleTriangle(capsule, capsuleRadius, phantomTransform, triangle, body.Collection.TriangleRadius, body.Transform, tolerance, ref closest);
                if (closest.HasHit) collector.Add(closest.Hit);
            }
        }
    }

    private static float CachedPathLength(Vector128<float> path)
    {
        var lengthSquared = Dot3(path, path);
        var estimate = Sse.ReciprocalSqrt(lengthSquared);
        var refined = Sse.Multiply(Sse.Multiply(Sse.Subtract(Broadcast(3f), Sse.Multiply(Sse.Multiply(estimate, lengthSquared), estimate)), Sse.Multiply(estimate, Broadcast(0.5f))), lengthSquared);
        return Sse.AndNot(Sse.CompareLessThanOrEqual(lengthSquared, Vector128<float>.Zero), refined).ToScalar();
    }

    public void LinearCast(in HavokTransform phantomTransform, Vector128<float> to, float tolerance, float maxExtraPenetration, HavokAllCollector castCollector, HavokAllCollector? startCollector)
    {
        var path = Sse.Subtract(to, phantomTransform.Translation);
        var settings = new HavokLinearCastSettings
        {
            Tolerance = tolerance,
            Path = path,
            MaxExtraPenetration = maxExtraPenetration,
            CastToleranceExtension = CachedPathLength(path),
            IterationEarlyOutDistance = IterationEarlyOutDistance,
            MaxIterations = MaxIterations,
        };
        var start = startCollector ?? castCollector;
        for (var index = Overlapping.Count - 1; index >= 0; index--)
        {
            var body = Overlapping[index];
            castCollector.ProxyCollidable = phantomCollidable;
            castCollector.Body = body.Id;
            start.ProxyCollidable = phantomCollidable;
            start.Body = body.Id;
            HavokBvTreeCast.CapsuleCast(capsuleLocal, capsuleRadius, phantomTransform, body.Collection, body.Transform, settings, _keys,
                ref castCollector, ref start, startCollector is not null);
        }
    }
}
