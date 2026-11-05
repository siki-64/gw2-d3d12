using System;
using System.Numerics;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Gw2.Client.Scene.Havok;

public readonly record struct Gw2CameraEyeSphere(float Radius, float Lead);

public sealed class Gw2CameraCollision(Gw2HavokMapWorld world)
{
    public const float MaximumEyeRadius = 22f;
    public const float MinimumEyeLead = 10f;
    public const float EyeRadiusMargin = 1f;
    private const float StartTouchingFraction = 0.02f;
    private const float StartTouchingRestFraction = 0.01f;
    private const float IterationEarlyOutDistance = 0.01f;
    private const int MaxIterations = 20;
    private static readonly float MaxExtraPenetration = BitConverter.Int32BitsToSingle(0x34000000);
    private static readonly Vector128<float>[] SphereCentre = [Vector128<float>.Zero, Vector128<float>.Zero];

    private readonly HavokAllCollector _hits = new();
    private HavokCapsulePhantomWorld? _phantom;
    private float _phantomRadius = float.NaN;

    private HavokCapsulePhantomWorld PhantomOf(float collisionRadius)
    {
        if (_phantom is null || _phantomRadius != collisionRadius)
            (_phantom, _phantomRadius) = (new HavokCapsulePhantomWorld(SphereCentre, collisionRadius, 0)
            {
                IterationEarlyOutDistance = IterationEarlyOutDistance,
                MaxIterations = MaxIterations,
            }, collisionRadius);
        return _phantom;
    }

    public static Gw2CameraEyeSphere EyeSphereOf(float verticalFieldOfView, float aspectRatio, float nearPlane)
    {
        var halfHeight = MathF.Atan(verticalFieldOfView) * nearPlane;
        var corner = new Vector3(halfHeight * aspectRatio, halfHeight, nearPlane);
        var opposite = new Vector3(-halfHeight * aspectRatio, -halfHeight, nearPlane);
        var (centre, radius) = BoundingSphereOf(corner, opposite, Vector3.Zero);
        var sphereRadius = MathF.Min(radius + EyeRadiusMargin, MaximumEyeRadius);
        var lead = sphereRadius - centre.Z < MinimumEyeLead ? MinimumEyeLead : centre.Z;
        return new Gw2CameraEyeSphere(sphereRadius, lead);
    }

    public static (Vector3 Centre, float Radius) BoundingSphereOf(Vector3 first, Vector3 second, Vector3 third)
    {
        var toSecond = second - first;
        var toThird = third - first;
        var secondSquared = Vector3.Dot(toSecond, toSecond);
        var cross = Vector3.Dot(toThird, toSecond);
        var thirdSquared = Vector3.Dot(toThird, toThird);
        var product = thirdSquared * secondSquared;
        var determinant = (product - cross * cross) + (product - cross * cross);
        var secondWeight = (product - thirdSquared * cross) / determinant;
        var thirdWeight = (product - cross * secondSquared) / determinant;
        Vector3 centre;
        Vector3 onSphere;
        if (secondWeight <= 0f) (centre, onSphere) = ((first + third) * 0.5f, first);
        else if (thirdWeight <= 0f) (centre, onSphere) = ((first + second) * 0.5f, first);
        else if (secondWeight + thirdWeight < 1f) (centre, onSphere) = (first + toSecond * secondWeight + toThird * thirdWeight, first);
        else (centre, onSphere) = ((second + third) * 0.5f, second);
        return (centre, Vector3.Distance(centre, onSphere));
    }

    public const float CentreRadius = 22f;

    public Vector3 CollideCentre(Vector3 agentPosition, float agentRadius, float agentHeight, Vector3 desiredCentre)
    {
        if (agentRadius <= CentreRadius) return desiredCentre;
        var axisBottom = agentPosition with { Z = agentPosition.Z - CentreRadius };
        var axisTop = axisBottom with { Z = axisBottom.Z - (agentHeight - 2f * CentreRadius) };
        return SweepSphere(ClosestOnSegment(desiredCentre, axisBottom, axisTop), desiredCentre, CentreRadius);
    }

    public static Vector3 ClosestOnSegment(Vector3 point, Vector3 start, Vector3 end)
    {
        var along = end - start;
        var projection = Vector3.Dot(point - start, along);
        if (projection <= 0f) return start;
        var lengthSquared = along.LengthSquared();
        return projection < lengthSquared ? start + along * (projection / lengthSquared) : end;
    }

    public const int DefaultCollisionSensitivity = 1;
    public const int ProbeRows = 5;
    public const int ProbeColumns = 3;
    public const int ProbeCount = ProbeRows * ProbeColumns;
    public const int CentreProbe = 6;
    public const float ProbeSpacing = 22f;
    private const int BentSensitivityBonus = 6;

    private readonly Vector3[] _probeStarts = new Vector3[ProbeCount];
    private readonly Vector3[] _probeEnds = new Vector3[ProbeCount];
    private readonly bool[] _probeBlocked = new bool[ProbeCount];

    public Vector3 PlaceEye(Vector3 centre, Vector3 collidedCentre, Vector3 desiredEye, in Gw2CameraEyeSphere sphere, int collisionSensitivity = DefaultCollisionSensitivity)
    {
        var towardCentre = centre - desiredEye;
        var lengthSquared = towardCentre.LengthSquared();
        var direction = lengthSquared >= 1e-12f ? towardCentre / MathF.Sqrt(lengthSquared) : Vector3.Zero;
        var lead = direction * sphere.Lead;
        if (collisionSensitivity == 0) return SweepSphere(collidedCentre + lead, desiredEye + lead, sphere.Radius) - lead;
        var bends = collisionSensitivity is >= 1 and <= 9;
        var blockedNeeded = bends ? collisionSensitivity + BentSensitivityBonus : collisionSensitivity;
        var side = NormalizedOrZero(new Vector3(-towardCentre.Y, towardCentre.X, 0f));
        var flat = NormalizedOrZero(new Vector3(towardCentre.X, towardCentre.Y, 0f));
        for (var probe = 0; probe < ProbeCount; probe++)
        {
            var row = probe / ProbeColumns - 2f;
            var column = probe % ProbeColumns;
            var across = row * ProbeSpacing * 2f;
            var bend = bends ? (MathF.Abs(row) * MathF.Abs(row) * 0.25f + MathF.Abs(row) * 0.5f) * ProbeSpacing : 0f;
            var down = column * -ProbeSpacing * 2f - bend;
            _probeEnds[probe] = desiredEye + lead;
            _probeStarts[probe] = new Vector3(side.X * across - flat.X * bend, side.Y * across - flat.Y * bend, down) + collidedCentre + lead;
            _probeBlocked[probe] = false;
        }
        var blocked = 0;
        var centreHit = SweepSphere(_probeStarts[CentreProbe], _probeEnds[CentreProbe], sphere.Radius);
        if (centreHit != _probeEnds[CentreProbe]) { blocked++; _probeBlocked[CentreProbe] = true; }
        _probeEnds[CentreProbe] = centreHit - lead;
        if (_probeBlocked[CentreProbe])
        {
            var origin = collidedCentre + lead;
            for (var probe = 0; probe < ProbeCount; probe++)
            {
                if (probe == CentreProbe) continue;
                if (world.CastGameRay(origin, _probeStarts[probe], Gw2HavokMapWorld.CameraSweepMask) is not { } hit) continue;
                blocked++;
                _probeBlocked[probe] = true;
                _probeStarts[probe] = origin;
                _probeEnds[probe] = hit.Position;
                if (IsDecided(blocked, probe, blockedNeeded)) break;
            }
            if (blocked < blockedNeeded)
            {
                for (var probe = 0; probe < ProbeCount; probe++)
                {
                    if (probe == CentreProbe || _probeBlocked[probe]) continue;
                    var swept = SweepSphere(_probeStarts[probe], _probeEnds[probe], sphere.Radius);
                    if (swept != _probeEnds[probe]) { blocked++; _probeBlocked[probe] = true; }
                    _probeEnds[probe] = swept - lead;
                    if (IsDecided(blocked, probe, blockedNeeded)) break;
                }
            }
        }
        return blocked < blockedNeeded ? desiredEye : _probeEnds[CentreProbe];
    }

    private static bool IsDecided(int blocked, int probe, int blockedNeeded) =>
        blocked >= blockedNeeded || blocked - probe + (ProbeCount - 1) < blockedNeeded;

    internal static Vector3 NormalizedOrZero(Vector3 value) => value.LengthSquared() >= 1e-12f ? Vector3.Normalize(value) : Vector3.Zero;

    public const float CoincidentEyeDistanceSquared = 0.00015625001f;
    public const float CoincidentEyeNudge = 0.0125f;

    public (Vector3 Eye, Vector3 Centre) PlaceEyeAndCentre(Vector3 centre, Vector3 collidedCentre, Vector3 desiredEye, in Gw2CameraEyeSphere sphere, float maximumDistance, int collisionSensitivity = DefaultCollisionSensitivity)
    {
        var eye = PlaceEye(centre, collidedCentre, desiredEye, sphere, collisionSensitivity);
        return SettleEyeAndCentre(centre, collidedCentre, desiredEye, eye, maximumDistance);
    }

    public static (Vector3 Eye, Vector3 Centre) SettleEyeAndCentre(Vector3 centre, Vector3 collidedCentre, Vector3 desiredEye, Vector3 eye, float maximumDistance)
    {
        var settledCentre = centre;
        var collidedReach = Vector3.Distance(collidedCentre, desiredEye);
        if (collidedReach != 0f)
        {
            var reached = Vector3.Distance(eye, desiredEye) / collidedReach;
            var offset = eye - (desiredEye + (centre - desiredEye) * reached);
            var allowed = Vector3.Distance(collidedCentre, centre);
            if (offset.Length() > allowed) offset = NormalizedOrZero(offset) * allowed;
            settledCentre = centre + offset;
        }
        if (Vector3.DistanceSquared(eye, settledCentre) < CoincidentEyeDistanceSquared)
        {
            eye -= NormalizedOrZero(centre - desiredEye) * CoincidentEyeNudge;
        }
        var fromCentre = eye - settledCentre;
        var distance = fromCentre.LengthSquared() >= 1e-12f ? fromCentre.Length() : 0f;
        if (distance > maximumDistance) eye = settledCentre + fromCentre / distance * maximumDistance;
        return (eye, settledCentre);
    }

    public Vector3 SweepSphere(Vector3 from, Vector3 to, float radius)
    {
        var start = Gw2CharacterStep.CollisionOf(from);
        var end = Gw2CharacterStep.CollisionOf(to);
        var collisionRadius = radius * Gw2HavokMapWorld.CollisionUnitsPerWorldUnit;
        var phantom = PhantomOf(collisionRadius);
        var padding = Vector128.Create(collisionRadius, collisionRadius, collisionRadius, 0f);
        world.CollectOverlapping(Sse.Subtract(Sse.Min(start, end), padding), Sse.Add(Sse.Max(start, end), padding), phantom.Overlapping, Gw2HavokMapWorld.CameraSweepMask);
        _hits.Reset();
        var transform = HavokTransform.Identity;
        transform.Translation = start;
        phantom.LinearCast(transform, end, 0f, MaxExtraPenetration, _hits, null);
        if (_hits.Hits.Count == 0) return to;
        var fraction = float.MaxValue;
        foreach (var hit in _hits.Hits) fraction = MathF.Min(fraction, hit.Normal.GetElement(3));
        if (fraction <= StartTouchingFraction) return Vector3.Lerp(from, to, StartTouchingRestFraction);
        return Vector3.Lerp(from, to, Math.Clamp(fraction, 0f, 1f));
    }
}

public static class Gw2CameraCentreLineOfSight
{
    public const float BlockedCheckDistance = 180f;
    public const float ThroughWallLead = 22f;

    public static Vector3 FollowCentre(Gw2HavokMapWorld? world, Vector3 centre, Vector3 desiredCentre, Vector3 eye, Vector3 desiredEye, float snapDistance, float ratePerSecond, float elapsedSeconds)
    {
        var distanceSquared = Vector3.DistanceSquared(centre, desiredCentre);
        if (distanceSquared >= snapDistance * snapDistance) return desiredCentre;
        if (world is not null && distanceSquared > BlockedCheckDistance * BlockedCheckDistance
            && world.CastGameRay(eye, desiredEye, Gw2HavokMapWorld.CameraLineOfSightMask) is { } forward
            && world.CastGameRay(desiredEye, eye, Gw2HavokMapWorld.CameraLineOfSightMask) is { } backward
            && Vector3.DistanceSquared(forward.Position, backward.Position) > BlockedCheckDistance * BlockedCheckDistance)
        {
            var backwardDistance = backward.Fraction * Vector3.Distance(eye, desiredEye);
            if (backwardDistance < ThroughWallLead) return desiredCentre;
            var currentWeight = backwardDistance * backwardDistance / distanceSquared;
            return centre * currentWeight + desiredCentre * (1f - currentWeight) + Gw2CameraCollision.NormalizedOrZero(desiredCentre - centre) * ThroughWallLead;
        }
        return Gw2WorldCameraMotion.Ease(centre, desiredCentre, ratePerSecond, elapsedSeconds);
    }
}
