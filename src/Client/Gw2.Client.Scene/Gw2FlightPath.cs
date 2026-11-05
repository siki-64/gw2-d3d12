using System;
using System.Collections.Generic;
using System.Numerics;

namespace Gw2.Client.Scene;

public sealed class Gw2FlightPath
{
    private readonly Vector3[] _waypoints;
    private readonly float[] _distanceAtWaypoint;

    public Gw2FlightPath(IReadOnlyList<Vector3> waypoints, float speed)
    {
        ArgumentNullException.ThrowIfNull(waypoints);
        if (waypoints.Count < 2) throw new ArgumentException("A flight needs at least two waypoints.", nameof(waypoints));
        if (speed <= 0f) throw new ArgumentOutOfRangeException(nameof(speed), "Flight speed must be positive.");
        _waypoints = [.. waypoints];
        _distanceAtWaypoint = new float[_waypoints.Length];
        for (var index = 1; index < _waypoints.Length; index++)
            _distanceAtWaypoint[index] = _distanceAtWaypoint[index - 1] + Vector3.Distance(_waypoints[index - 1], _waypoints[index]);
        Speed = speed;
    }

    public float Speed { get; }

    public float Length => _distanceAtWaypoint[^1];

    public float DurationSeconds => Length / Speed;

    public Vector3 Eye(float seconds)
    {
        var (segment, fraction) = SegmentAt(seconds);
        return Vector3.Lerp(_waypoints[segment], _waypoints[segment + 1], fraction);
    }

    public Vector3 Front(float seconds)
    {
        var (segment, _) = SegmentAt(seconds);
        var direction = _waypoints[segment + 1] - _waypoints[segment];
        return direction == Vector3.Zero ? Vector3.UnitX : Vector3.Normalize(direction);
    }

    private (int Segment, float Fraction) SegmentAt(float seconds)
    {
        var distance = Math.Clamp(seconds * Speed, 0f, Length);
        var segment = 0;
        while (segment < _waypoints.Length - 2 && distance > _distanceAtWaypoint[segment + 1]) segment++;
        var segmentLength = _distanceAtWaypoint[segment + 1] - _distanceAtWaypoint[segment];
        return (segment, segmentLength <= 0f ? 1f : (distance - _distanceAtWaypoint[segment]) / segmentLength);
    }
}
