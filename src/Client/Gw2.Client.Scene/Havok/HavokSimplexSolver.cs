using System;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using static Gw2.Client.Scene.Havok.HavokVector;

namespace Gw2.Client.Scene.Havok;

[StructLayout(LayoutKind.Explicit, Size = 64)]
public struct HavokSurfaceConstraint
{
    [FieldOffset(0)] public Vector128<float> Plane;
    [FieldOffset(16)] public Vector128<float> Velocity;
    [FieldOffset(32)] public float StaticFriction;
    [FieldOffset(36)] public float ExtraUpStaticFriction;
    [FieldOffset(40)] public float ExtraDownStaticFriction;
    [FieldOffset(44)] public float DynamicFriction;
    [FieldOffset(48)] public int Priority;
}

[StructLayout(LayoutKind.Explicit, Size = 16)]
public struct HavokSurfaceInteraction
{
    [FieldOffset(0)] public byte Touched;
    [FieldOffset(1)] public byte Stopped;
    [FieldOffset(4)] public float SurfaceTime;
    [FieldOffset(8)] public float PenaltyDistance;
    [FieldOffset(12)] public int Status;
}

public struct HavokSimplexInput
{
    public Vector128<float> Position;
    public Vector128<float> Velocity;
    public Vector128<float> MaxSurfaceVelocity;
    public Vector128<float> UpVector;
    public float DeltaTime;
    public float MinDeltaTime;
}

public struct HavokSimplexOutput
{
    public Vector128<float> Position;
    public Vector128<float> Velocity;
    public float DeltaTime;
}

public static class HavokSimplexSolver
{
    private const float MinimumTimeStep = 1e-4f;
    private const float ViolationTolerance = -0.001f;
    private const float RelativeSpeedTolerance = 0.001f;
    private const float PenaltyGrowth = 2.3841858e-7f;

    public static void Solve(in HavokSimplexInput input, ReadOnlySpan<HavokSurfaceConstraint> constraints, Span<HavokSurfaceInteraction> interactions, ref HavokSimplexOutput output)
    {
        output.Position = input.Position;
        output.Velocity = input.Velocity;
        interactions[..constraints.Length].Clear();
        var simplex = new Simplex(input, constraints, interactions);
        var remaining = Broadcast(input.DeltaTime);
        var elapsed = 0f;
        if (remaining.GetElement(0) < 0f) return;
        while (true)
        {
            var hit = -1;
            var hitTime = remaining;
            for (var index = 0; index < constraints.Length; index++)
            {
                if (simplex.Contains(index) || interactions[index].Status != 0) continue;
                ref readonly var constraint = ref constraints[index];
                var approach = Sse.Subtract(Vector128<float>.Zero, Dot3(Sse.Subtract(output.Velocity, constraint.Velocity), constraint.Plane));
                if (!(approach.GetElement(0) > 0f)) continue;
                var moved = Sse.Multiply(Sse.Subtract(output.Position, Sse.Multiply(Broadcast(elapsed), constraint.Velocity)), constraint.Plane);
                var withPlaneDistance = Sse.Shuffle(moved, Sse.UnpackHigh(moved, constraint.Plane), 196);
                var pairs = Sse.Add(Sse.Shuffle(withPlaneDistance, withPlaneDistance, 78), withPlaneDistance);
                var distance = Sse.Add(Sse.Shuffle(pairs, pairs, 177), pairs);
                var gap = Sse.Add(Sse.AndNot(Sse.CompareLessThanOrEqual(distance, Broadcast(Epsilon)), distance), Broadcast(interactions[index].PenaltyDistance));
                if (gap.GetElement(0) < hitTime.GetElement(0) * approach.GetElement(0))
                {
                    hit = index;
                    hitTime = Sse.Multiply(Reciprocal(approach), gap);
                }
            }

            if (MinimumTimeStep < hitTime.GetElement(0))
            {
                var total = elapsed + hitTime.GetElement(0);
                output.Position = Sse.Add(Sse.Multiply(hitTime, output.Velocity), output.Position);
                remaining = Sse.Subtract(remaining, hitTime);
                elapsed = total;
                for (var entry = 0; entry < simplex.Count; entry++)
                {
                    ref var touched = ref interactions[simplex.ConstraintAt(entry)];
                    touched.SurfaceTime += hitTime.GetElement(0);
                    touched.Touched = 1;
                }
                output.DeltaTime = total;
                if (total > input.MinDeltaTime) return;
            }
            if (hit < 0)
            {
                output.DeltaTime = input.DeltaTime;
                return;
            }
            simplex.Add(hit);
            interactions[hit].PenaltyDistance = interactions[hit].PenaltyDistance * 2f + PenaltyGrowth;
            simplex.SolveVelocity(ref output.Velocity);
            if (remaining.GetElement(0) < 0f) return;
        }
    }

    private ref struct Simplex
    {
        private readonly HavokSimplexInput _input;
        private readonly ReadOnlySpan<HavokSurfaceConstraint> _constraints;
        private readonly Span<HavokSurfaceInteraction> _interactions;
        private int _first;
        private int _second;
        private int _third;
        private int _fourth;

        public Simplex(in HavokSimplexInput input, ReadOnlySpan<HavokSurfaceConstraint> constraints, Span<HavokSurfaceInteraction> interactions)
        {
            _input = input;
            _constraints = constraints;
            _interactions = interactions;
        }

        public int Count { get; private set; }

        public readonly bool Contains(int index) =>
            (Count >= 1 && _first == index) || (Count >= 2 && _second == index) || (Count >= 3 && _third == index);

        public readonly int ConstraintAt(int entry) => entry switch { 0 => _first, 1 => _second, 2 => _third, _ => _fourth };

        private void Set(int entry, int index)
        {
            switch (entry)
            {
                case 0: _first = index; break;
                case 1: _second = index; break;
                case 2: _third = index; break;
                default: _fourth = index; break;
            }
        }

        public void Add(int index) => Set(Count++, index);

        public void SolveVelocity(ref Vector128<float> output)
        {
            var velocity = _input.Velocity;
            while (true)
            {
                switch (Count)
                {
                    case 1:
                        output = OnePlane(_first, velocity);
                        return;
                    case 2:
                    {
                        var along = OnePlane(_second, velocity);
                        if (Violates(_first, along))
                        {
                            output = TwoPlanes(_first, _second, velocity);
                            return;
                        }
                        output = along;
                        _first = _second;
                        Count = 1;
                        return;
                    }
                    case 3:
                    {
                        var along = OnePlane(_third, velocity);
                        if (!Violates(_first, along) && !Violates(_second, along))
                        {
                            output = along;
                            _first = _third;
                            Count = 1;
                            continue;
                        }
                        var kept = -1;
                        for (var candidate = 0; candidate < 2; candidate++)
                        {
                            var pair = TwoPlanes(ConstraintAt(candidate), _third, velocity);
                            if (Violates(ConstraintAt(1 - candidate), pair)) continue;
                            kept = candidate;
                            break;
                        }
                        if (kept < 0)
                        {
                            output = ThreePlanes(_first, _second, _third, sortOnFailure: true, velocity);
                            return;
                        }
                        _first = ConstraintAt(kept);
                        _second = _third;
                        Count = 2;
                        continue;
                    }
                    case 4:
                    {
                        Sort();
                        var replaced = false;
                        for (var dropped = 0; dropped < 3; dropped++)
                        {
                            var trial = ThreePlanes(ConstraintAt((dropped + 1) % 3), ConstraintAt((dropped + 2) % 3), _fourth, sortOnFailure: false, velocity);
                            if (Violates(ConstraintAt(dropped), trial)) continue;
                            Set(dropped, _third);
                            _third = _fourth;
                            Count = 3;
                            replaced = true;
                            break;
                        }
                        if (replaced) continue;
                        var fallback = velocity;
                        fallback = ThreePlanes(_first, _second, _third, sortOnFailure: false, fallback);
                        fallback = ThreePlanes(_first, _second, _fourth, sortOnFailure: false, fallback);
                        fallback = ThreePlanes(_first, _third, _fourth, sortOnFailure: false, fallback);
                        fallback = ThreePlanes(_second, _third, _fourth, sortOnFailure: false, fallback);
                        output = fallback;
                        var highest = Math.Max(Math.Max(Math.Max(Math.Max(StatusOf(_first), 0), StatusOf(_second)), StatusOf(_third)), StatusOf(_fourth));
                        for (var entry = 0; entry < 4; entry++)
                        {
                            if (StatusOf(ConstraintAt(entry)) != highest) continue;
                            Set(entry, _fourth);
                            break;
                        }
                        Count = 3;
                        _interactions[_first].Status = 0;
                        _interactions[_second].Status = 0;
                        _interactions[_third].Status = 0;
                        continue;
                    }
                    default:
                        return;
                }
            }
        }

        private readonly int StatusOf(int index) => _interactions[index].Status;

        private readonly bool Violates(int index, Vector128<float> velocity)
        {
            ref readonly var constraint = ref _constraints[index];
            return Dot3Scalar(Sse.Subtract(velocity, constraint.Velocity), constraint.Plane) < ViolationTolerance;
        }

        private void Sort()
        {
            for (var first = 0; first < Count - 1; first++)
            for (var second = first + 1; second < Count; second++)
            {
                var a = ConstraintAt(first);
                var b = ConstraintAt(second);
                var priorityA = _constraints[a].Priority;
                var priorityB = _constraints[b].Priority;
                if (priorityA < priorityB) continue;
                if (priorityA == priorityB && Dot3Scalar(_constraints[a].Velocity, _constraints[a].Velocity) < Dot3Scalar(_constraints[b].Velocity, _constraints[b].Velocity)) continue;
                Set(first, b);
                Set(second, a);
            }
        }

        private readonly Vector128<float> OnePlane(int index, Vector128<float> velocity)
        {
            ref readonly var constraint = ref _constraints[index];
            var plane = constraint.Plane;
            var surfaceVelocity = constraint.Velocity;
            var relative = Sse.Subtract(velocity, surfaceVelocity);
            var dynamicFriction = Broadcast(constraint.DynamicFriction);
            var staticFriction = constraint.StaticFriction;
            var normalSpeed = Dot3(relative, plane);
            var normalSpeedSquared = normalSpeed.GetElement(0) * normalSpeed.GetElement(0);
            var speedSquared = Dot3(relative, relative).GetElement(0);
            var tangent = Sse.Subtract(relative, Sse.Multiply(normalSpeed, plane));
            var extraFriction = Dot3Scalar(_input.UpVector, tangent) <= 0f ? constraint.ExtraDownStaticFriction : constraint.ExtraUpStaticFriction;
            if (extraFriction > 0f)
            {
                var sideSpeed = Vector128<float>.Zero;
                var side = Cross(_input.UpVector, plane);
                var sideLengthSquared = Dot3(side, side);
                if (Epsilon < sideLengthSquared.GetElement(0))
                {
                    side = Sse.Multiply(side, InverseSquareRoot(sideLengthSquared));
                    sideSpeed = Dot3(side, tangent);
                    if (sideSpeed.GetElement(0) * sideSpeed.GetElement(0) <= staticFriction * staticFriction * normalSpeedSquared)
                    {
                        tangent = Sse.Subtract(tangent, Sse.Multiply(sideSpeed, side));
                        sideSpeed = Vector128<float>.Zero;
                    }
                }
                var combined = extraFriction + staticFriction;
                if (!(speedSquared - sideSpeed.GetElement(0) * sideSpeed.GetElement(0) - normalSpeedSquared > combined * combined * normalSpeedSquared))
                {
                    if (sideSpeed.GetElement(0) == 0f) return surfaceVelocity;
                    tangent = Sse.Multiply(sideSpeed, side);
                }
            }
            else if (speedSquared <= (staticFriction * staticFriction + 1f) * normalSpeedSquared)
            {
                return surfaceVelocity;
            }

            if (dynamicFriction.GetElement(0) < 1f)
            {
                var tangentSquared = Dot3(tangent, tangent);
                if (Epsilon <= tangentSquared.GetElement(0) && RelativeSpeedTolerance * speedSquared < tangentSquared.GetElement(0))
                {
                    var ratio = Sse.Multiply(Reciprocal(tangentSquared), Broadcast(speedSquared));
                    var scaled = Sse.Multiply(tangent, Sse.Add(Sse.Multiply(SquareRoot(ratio), Sse.Subtract(Broadcast(1f), dynamicFriction)), dynamicFriction));
                    tangent = Sse.Subtract(scaled, Sse.Multiply(Dot3(scaled, plane), plane));
                }
            }
            return Sse.Add(tangent, surfaceVelocity);
        }

        private readonly Vector128<float> TwoPlanes(int firstIndex, int secondIndex, Vector128<float> velocity)
        {
            ref readonly var first = ref _constraints[firstIndex];
            ref readonly var second = ref _constraints[secondIndex];
            var firstPlane = first.Plane;
            var secondPlane = second.Plane;
            var firstRotated = YZX(firstPlane);
            var secondRotated = YZX(secondPlane);
            var unnormalisedCross = Sse.Subtract(Sse.Multiply(secondRotated, firstPlane), Sse.Multiply(firstRotated, secondPlane));
            var axis = YZX(unnormalisedCross);
            var axisLengthSquared = Dot3(axis, axis);
            if (axisLengthSquared.GetElement(0) > Epsilon)
            {
                var inverseLength = InverseSquareRoot(axisLengthSquared);
                var direction = Sse.Multiply(axis, inverseLength);
                var directionRotated = YZX(direction);
                var secondCross = Sse.Subtract(Sse.Multiply(secondPlane, directionRotated), Sse.Multiply(secondRotated, direction));
                var firstCross = Sse.Subtract(Sse.Multiply(firstRotated, direction), Sse.Multiply(firstPlane, directionRotated));
                var speeds = Sse.MoveLowToHigh(
                    Sse.UnpackLow(Sse.Multiply(Dot3(Sse.Add(first.Velocity, second.Velocity), direction), Broadcast(0.5f)), Dot3(firstPlane, first.Velocity)),
                    Sse.UnpackLow(Dot3(secondPlane, second.Velocity), Vector128<float>.Zero));
                var crease = Sse.Multiply(
                    Sse.Add(Sse.Add(Sse.Multiply(Splat(speeds, 1), YZX(secondCross)), Sse.Multiply(Splat(speeds, 0), YZX(unnormalisedCross))), Sse.Multiply(Splat(speeds, 2), YZX(firstCross))),
                    inverseLength);
                if (!AnyXyzGreater(crease, _input.MaxSurfaceVelocity))
                {
                    var relative = Sse.Subtract(velocity, crease);
                    var relativeSquared = Dot3(relative, relative);
                    var along = Dot3(relative, direction);
                    var upAlong = Dot3Scalar(_input.UpVector, direction);
                    var staticFriction = first.StaticFriction + second.StaticFriction;
                    var extraFriction = along.GetElement(0) * upAlong <= 0f
                        ? first.ExtraDownStaticFriction + second.ExtraDownStaticFriction
                        : first.ExtraUpStaticFriction + second.ExtraUpStaticFriction;
                    var alongSquared = along.GetElement(0) * along.GetElement(0);
                    var dynamicFriction = Sse.Multiply(Broadcast(first.DynamicFriction + second.DynamicFriction), Broadcast(0.5f));
                    var limit = staticFriction + extraFriction * upAlong;
                    if (!(alongSquared > (relativeSquared.GetElement(0) - alongSquared) * (limit * 0.5f * (limit * 0.5f)))) return crease;
                    if (dynamicFriction.GetElement(0) < 1f && RelativeSpeedTolerance * relativeSquared.GetElement(0) < alongSquared)
                        along = Sse.Multiply(along, Sse.Add(Sse.Multiply(Sse.Multiply(SquareRoot(relativeSquared), Absolute(Reciprocal(along))), Sse.Subtract(Broadcast(1f), dynamicFriction)), dynamicFriction));
                    return Sse.Add(Sse.Multiply(along, direction), crease);
                }
            }
            _interactions[firstIndex].Status = 2;
            _interactions[secondIndex].Status = 2;
            if (first.Priority <= second.Priority) return OnePlane(secondIndex, OnePlane(firstIndex, velocity));
            return OnePlane(firstIndex, OnePlane(secondIndex, velocity));
        }

        private Vector128<float> ThreePlanes(int firstIndex, int secondIndex, int thirdIndex, bool sortOnFailure, Vector128<float> velocity)
        {
            var first = _constraints[firstIndex].Plane;
            var second = _constraints[secondIndex].Plane;
            var third = _constraints[thirdIndex].Plane;
            var thirdRotated = YZX(third);
            var secondRotated = YZX(second);
            var firstRotated = YZX(first);
            var secondCrossThird = YZX(Sse.Subtract(Sse.Multiply(thirdRotated, second), Sse.Multiply(secondRotated, third)));
            var firstCrossSecond = YZX(Sse.Subtract(Sse.Multiply(secondRotated, first), Sse.Multiply(firstRotated, second)));
            var thirdCrossFirst = YZX(Sse.Subtract(Sse.Multiply(firstRotated, third), Sse.Multiply(thirdRotated, first)));
            var determinant = Dot3(first, secondCrossThird);
            if (!(Absolute(determinant).GetElement(0) < Epsilon))
            {
                var speeds = Sse.MoveLowToHigh(
                    Sse.UnpackLow(Dot3(_constraints[firstIndex].Velocity, first), Dot3(_constraints[secondIndex].Velocity, second)),
                    Sse.UnpackLow(Dot3(_constraints[thirdIndex].Velocity, third), Vector128<float>.Zero));
                var corner = Sse.Multiply(
                    Sse.Add(Sse.Add(Sse.Multiply(Splat(speeds, 1), thirdCrossFirst), Sse.Multiply(Splat(speeds, 0), secondCrossThird)), Sse.Multiply(Splat(speeds, 2), firstCrossSecond)),
                    Reciprocal(determinant));
                if (!AnyXyzGreater(corner, _input.MaxSurfaceVelocity)) return corner;
            }
            if (sortOnFailure)
            {
                Sort();
                firstIndex = _first;
                secondIndex = _second;
                thirdIndex = _third;
            }
            _interactions[firstIndex].Status = 1;
            _interactions[secondIndex].Status = 1;
            _interactions[thirdIndex].Status = 1;
            var result = TwoPlanes(firstIndex, secondIndex, velocity);
            result = TwoPlanes(firstIndex, thirdIndex, result);
            return TwoPlanes(secondIndex, thirdIndex, result);
        }
    }
}
