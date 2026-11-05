using System.Numerics;

namespace Gw2.Dat.Content;

public sealed record Gw2EnvironmentZoneWeight(Gw2MapEnvZone Zone, float Weight);

public static class Gw2EnvironmentZoneWeights
{
    private const byte SphereShape = 0;
    private const byte CylinderShape = 1;
    private const byte PolygonShape = 2;

    public static IReadOnlyList<Gw2EnvironmentZoneWeight> At(Gw2MapEnvironment environment, Vector3 position)
    {
        ArgumentNullException.ThrowIfNull(environment);
        var weights = new List<Gw2EnvironmentZoneWeight>();
        foreach (var zone in environment.Zones)
            if (ZoneWeight(zone, position) is { } weight)
                weights.Add(new(zone, weight));
        return weights;
    }

    public static float? ZoneWeight(Gw2MapEnvZone zone, Vector3 position)
    {
        ArgumentNullException.ThrowIfNull(zone);
        if (zone.Shapes.Count == 1) return ShapeWeight(zone.Shapes[0], position);
        var weight = 0f;
        foreach (var shape in zone.Shapes)
            if (ShapeWeight(shape, position) is { } shapeWeight)
                weight = MathF.Max(weight, shapeWeight);
        return weight != 0f ? weight : null;
    }

    public static float? ShapeWeight(Gw2MapEnvShape shape, Vector3 position) => shape.ShapeType switch
    {
        SphereShape => RadialWeight(Vector3.DistanceSquared(position, shape.Center), shape.FadeHorizInner, shape.FadeHorizOuter),
        CylinderShape => MathF.Abs(position.Z - shape.Center.Z) > shape.Height
            ? null
            : RadialWeight(Vector2.DistanceSquared(Horizontal(position), Horizontal(shape.Center)), shape.FadeHorizInner, shape.FadeHorizOuter),
        PolygonShape => PolygonWeight(shape, position),
        _ => null,
    };

    private static float? RadialWeight(float distanceSquared, float innerRadius, float outerRadius)
    {
        var outerSquared = outerRadius * outerRadius;
        var innerSquared = innerRadius * innerRadius;
        if (distanceSquared > outerSquared) return null;
        return innerSquared <= distanceSquared ? (outerSquared - distanceSquared) / (outerSquared - innerSquared) : 1f;
    }

    private static float? PolygonWeight(Gw2MapEnvShape shape, Vector3 position)
    {
        var halfHeight = shape.Height * 0.5f;
        var bottom = shape.Center.Z - halfHeight;
        var top = shape.Center.Z + halfHeight;
        if (position.Z < bottom || position.Z > top) return null;
        var point = Horizontal(position);
        var vertices = shape.Vertices;
        if (vertices.Count == 0 || !Contains(vertices, point)) return null;
        var horizontalFade = shape.FadeHorizInner;
        var verticalFade = shape.FadeHorizOuter;
        var verticalDistance = MathF.Min(MathF.Abs(bottom - position.Z), MathF.Abs(top - position.Z));
        if (horizontalFade == 0f && verticalDistance != 0f) return 1f;
        var edgeDistanceSquared = float.PositiveInfinity;
        for (var index = 0; index < vertices.Count; index++)
            edgeDistanceSquared = MathF.Min(edgeDistanceSquared, SegmentDistanceSquared(point, vertices[index], vertices[(index + 1) % vertices.Count]));
        var horizontal = MathF.Min(edgeDistanceSquared / (horizontalFade * horizontalFade), 1f);
        var vertical = MathF.Min(verticalDistance / verticalFade, 1f);
        return MathF.Min(horizontal, vertical);
    }

    private static bool Contains(IReadOnlyList<Vector2> vertices, Vector2 point)
    {
        var inside = false;
        for (int index = 0, previous = vertices.Count - 1; index < vertices.Count; previous = index++)
        {
            var a = vertices[index];
            var b = vertices[previous];
            if ((a.Y > point.Y) != (b.Y > point.Y) && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }

    private static float SegmentDistanceSquared(Vector2 point, Vector2 start, Vector2 end)
    {
        var segment = end - start;
        var lengthSquared = segment.LengthSquared();
        var along = lengthSquared == 0f ? 0f : Math.Clamp(Vector2.Dot(point - start, segment) / lengthSquared, 0f, 1f);
        return Vector2.DistanceSquared(point, start + segment * along);
    }

    private static Vector2 Horizontal(Vector3 value) => new(value.X, value.Y);
}
