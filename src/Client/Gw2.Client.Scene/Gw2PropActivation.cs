using System;
using System.Numerics;

namespace Gw2.Client.Scene;

public sealed class Gw2PropActivation
{
    public const float DefaultRadius = 15360f;
    public const float RecenterDistance = 121f;

    private readonly float _radius;
    private Vector3? _center;
    private float _appliedRadius;

    public Gw2PropActivation(float radius = DefaultRadius) => _radius = radius;

    public Vector2 Min { get; private set; }

    public Vector2 Max { get; private set; }

    public bool Contains(Vector3 position) =>
        position.X >= Min.X && position.Y >= Min.Y && position.X < Max.X && position.Y < Max.Y;

    public bool Update(Vector3 view, float viewDistance)
    {
        var radius = MathF.Min(_radius, viewDistance);
        if (_center is { } center && Vector3.DistanceSquared(center, view) <= RecenterDistance * RecenterDistance && MathF.Abs(radius - _appliedRadius) <= RecenterDistance)
            return false;
        _center = view;
        _appliedRadius = radius;
        Min = new Vector2(view.X - radius, view.Y - radius);
        Max = new Vector2(view.X + radius, view.Y + radius);
        return true;
    }
}
