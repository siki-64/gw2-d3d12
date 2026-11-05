using System.Numerics;

namespace Gw2.Dat.Content;

public sealed class Gw2CollisionWater
{
    public const float NoSurface = 1e9f;
    public const uint CollisionFlagGlobalWater = 0x1;
    public const uint VolumeFlagInverted = 0x1;

    private readonly IReadOnlyList<Volume> _volumes;

    private Gw2CollisionWater(IReadOnlyList<Volume> volumes) => _volumes = volumes;

    public int VolumeCount => _volumes.Count;

    public static Gw2CollisionWater From(Gw2MapCollision collision)
    {
        var volumes = new List<Volume>();
        if ((collision.Flags & CollisionFlagGlobalWater) != 0)
            volumes.Add(new Volume(
                IsGlobal: true,
                IsInverted: false,
                [new(-float.MaxValue, -float.MaxValue), new(float.MaxValue, -float.MaxValue), new(float.MaxValue, float.MaxValue), new(-float.MaxValue, float.MaxValue)],
                new Vector3(-float.MaxValue, -float.MaxValue, collision.WaterSurfaceZ),
                new Vector3(float.MaxValue, float.MaxValue, float.MaxValue)));
        foreach (var source in collision.WaterVolumes)
        {
            var polygon = source.Vertices.ToArray();
            var minimum = new Vector2(16777215f);
            var maximum = new Vector2(-16777215f);
            foreach (var vertex in polygon)
            {
                minimum = Vector2.Min(minimum, vertex);
                maximum = Vector2.Max(maximum, vertex);
            }
            var top = source.VerticalRange.Length > 0 ? source.VerticalRange[0] : 0f;
            var bottom = source.VerticalRange.Length > 1 ? source.VerticalRange[1] : 0f;
            volumes.Add(new Volume(false, (source.Flags & VolumeFlagInverted) != 0, polygon, new Vector3(minimum, top), new Vector3(maximum, bottom)));
        }
        foreach (var volume in volumes)
            volume.Neighbours.AddRange(volumes.Where(other => other != volume && PolygonsOverlap(volume.Polygon, other.Polygon)));
        return new Gw2CollisionWater(volumes);
    }

    public float SurfaceZAt(Vector3 point)
    {
        if (_volumes.Count == 0) return NoSurface;
        var above = -float.MaxValue;
        var ceiling = -float.MaxValue;
        var below = float.MaxValue;
        var xy = new Vector2(point.X, point.Y);
        foreach (var volume in _volumes)
        {
            if (!volume.IsGlobal && !(volume.ContainsXy(xy) && Winding(xy, volume.Polygon) != 0)) continue;
            var surface = volume.IsInverted ? volume.Maximum.Z : volume.Minimum.Z;
            var floor = volume.IsInverted ? volume.Minimum.Z : volume.Maximum.Z;
            if (point.Z >= floor && floor > ceiling && floor > above && volume.IsWaterAt(new Vector3(xy, floor)))
                ceiling = floor;
            var rising = point.Z >= surface && surface > above && surface > ceiling;
            var sinking = surface >= point.Z && below > surface;
            if ((rising || sinking) && volume.IsWaterAt(new Vector3(xy, surface)))
            {
                if (rising) above = surface;
                if (sinking) below = surface;
            }
        }
        if (ceiling > above || (above == -float.MaxValue && ceiling == -float.MaxValue))
        {
            if (below < float.MaxValue) return below;
        }
        else if (above > -float.MaxValue)
        {
            return above;
        }
        return NoSurface;
    }

    private static int Winding(Vector2 point, IReadOnlyList<Vector2> polygon)
    {
        var winding = 0;
        for (var index = 0; index < polygon.Count; index++)
        {
            var start = polygon[index];
            var end = polygon[(index + 1) % polygon.Count];
            var side = (end.X - start.X) * (point.Y - start.Y) - (end.Y - start.Y) * (point.X - start.X);
            if (point.Y < start.Y)
            {
                if (point.Y >= end.Y && side != 0f && side <= 0f) winding--;
            }
            else if (end.Y > point.Y && side != 0f && !(side <= 0f))
            {
                winding++;
            }
        }
        return winding;
    }

    private static bool PolygonsOverlap(IReadOnlyList<Vector2> first, IReadOnlyList<Vector2> second)
    {
        if (first.Any(vertex => Winding(vertex, second) != 0)) return true;
        if (second.Any(vertex => Winding(vertex, first) != 0)) return true;
        for (var index = 0; index < first.Count; index++)
        {
            var start = first[index];
            var end = first[(index + 1) % first.Count];
            for (var otherIndex = 0; otherIndex < second.Count; otherIndex++)
            {
                var otherStart = second[otherIndex];
                var otherEnd = second[(otherIndex + 1) % second.Count];
                if (EdgesCross(start, end, otherStart, otherEnd)) return true;
            }
        }
        return false;
    }

    private static bool EdgesCross(Vector2 start, Vector2 end, Vector2 otherStart, Vector2 otherEnd)
    {
        var acrossX = end.X - start.X;
        var acrossY = end.Y - start.Y;
        var startSide = (otherStart.Y - start.Y) * acrossX - (otherStart.X - start.X) * acrossY;
        var endSide = (otherEnd.Y - start.Y) * acrossX - (otherEnd.X - start.X) * acrossY;
        if (startSide < 0f == endSide < 0f) return false;
        var lengthSquared = acrossY * acrossY + acrossX * acrossX;
        if (lengthSquared < 1e-12f) return false;
        var length = MathF.Sqrt(lengthSquared);
        var normalX = -(acrossY / length) * 1f - 0f;
        var normalY = 0f - -(acrossX / length) * 1f;
        var otherAcrossX = otherEnd.X - otherStart.X;
        var otherAcrossY = otherEnd.Y - otherStart.Y;
        var hit = otherStart;
        var approach = MathF.Abs(otherAcrossY * normalY + otherAcrossX * normalX);
        if (approach > 0.000001f)
        {
            var travel = MathF.Abs((otherStart.Y - start.Y) * normalY + (otherStart.X - start.X) * normalX) / approach;
            hit = new Vector2(otherAcrossX * travel + otherStart.X, otherAcrossY * travel + otherStart.Y);
        }
        var limit = length * length;
        return Vector2.DistanceSquared(hit, start) <= limit && Vector2.DistanceSquared(hit, end) <= limit;
    }

    private sealed record Volume(bool IsGlobal, bool IsInverted, IReadOnlyList<Vector2> Polygon, Vector3 Minimum, Vector3 Maximum)
    {
        public List<Volume> Neighbours { get; } = [];

        public bool ContainsXy(Vector2 xy) =>
            xy.X >= Minimum.X && Maximum.X >= xy.X && xy.Y >= Minimum.Y && Maximum.Y >= xy.Y;

        public bool Contains(Vector3 point)
        {
            if (IsGlobal) return point.Z >= Minimum.Z;
            return ContainsXy(new Vector2(point.X, point.Y))
                && point.Z >= Minimum.Z && Maximum.Z >= point.Z
                && Winding(new Vector2(point.X, point.Y), Polygon) != 0;
        }

        public bool IsWaterAt(Vector3 point)
        {
            if (IsInverted)
            {
                var insideWater = false;
                foreach (var neighbour in Neighbours)
                {
                    if (!neighbour.Contains(point)) continue;
                    if (neighbour.IsInverted) return false;
                    insideWater = true;
                }
                return insideWater;
            }
            var onSurface = MathF.Abs(point.Z - Minimum.Z) <= 0.000001f;
            return !Neighbours.Any(neighbour => (neighbour.IsInverted || !onSurface || neighbour.Minimum.Z != Minimum.Z) && neighbour.Contains(point));
        }
    }
}
