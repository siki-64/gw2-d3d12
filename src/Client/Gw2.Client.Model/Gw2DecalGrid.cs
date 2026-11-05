using System.Numerics;

namespace Gw2.Client.Model;

public sealed record Gw2DecalSurface(Vector3[,] Points, Vector3 Across, Vector3 Along);

public static class Gw2DecalGrid
{
    public const int CellsPerSide = 16;
    private const float LiftTowardEye = 2f;

    public static void Build(Matrix4x4 placement, Vector2 size, float depth, Func<Vector3, Vector3, Vector3?> surfaceBetween, Vector3 eye, Vector4 colour, List<Gw2ParticleCorner> corners) =>
        Build(Surface(placement, size, depth, surfaceBetween), eye, colour, corners);

    public static Gw2DecalSurface Surface(Matrix4x4 placement, Vector2 size, float depth, Func<Vector3, Vector3, Vector3?> surfaceBetween)
    {
        ArgumentNullException.ThrowIfNull(surfaceBetween);
        var centre = placement.Translation;
        var across = UnitOr(new Vector3(placement.M11, placement.M12, placement.M13), Vector3.UnitX);
        var projection = UnitOr(new Vector3(placement.M31, placement.M32, placement.M33), Vector3.UnitZ);
        if (projection.Z < 0f) projection = -projection;
        var along = UnitOr(Vector3.Cross(projection, across), new Vector3(-across.Y, across.X, 0f));
        across = Vector3.Cross(along, projection);
        var points = new Vector3[CellsPerSide + 1, CellsPerSide + 1];
        for (var row = 0; row <= CellsPerSide; row++)
            for (var column = 0; column <= CellsPerSide; column++)
            {
                var flat = centre + across * (size.X * ((float)column / CellsPerSide - 0.5f)) + along * (size.Y * ((float)row / CellsPerSide - 0.5f));
                var near = flat - projection * (depth * 0.5f);
                var far = flat + projection * (depth * 0.5f);
                points[row, column] = surfaceBetween(near, far) ?? flat;
            }
        return new Gw2DecalSurface(points, across, along);
    }

    public static void Build(Gw2DecalSurface surface, Vector3 eye, Vector4 colour, List<Gw2ParticleCorner> corners)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(corners);
        var (surfacePoints, across, along) = (surface.Points, surface.Across, surface.Along);
        var points = new Vector3[CellsPerSide + 1, CellsPerSide + 1];
        for (var row = 0; row <= CellsPerSide; row++)
            for (var column = 0; column <= CellsPerSide; column++)
            {
                var point = surfacePoints[row, column];
                var toEye = eye - point;
                points[row, column] = toEye.LengthSquared() > 0f ? point + Vector3.Normalize(toEye) * LiftTowardEye : point;
            }
        for (var row = 0; row < CellsPerSide; row++)
            for (var column = 0; column < CellsPerSide; column++)
            {
                AddCorner(corners, points, across, along, row, column, eye, colour);
                AddCorner(corners, points, across, along, row, column + 1, eye, colour);
                AddCorner(corners, points, across, along, row + 1, column + 1, eye, colour);
                AddCorner(corners, points, across, along, row + 1, column, eye, colour);
            }
    }

    private static void AddCorner(List<Gw2ParticleCorner> corners, Vector3[,] points, Vector3 across, Vector3 along, int row, int column, Vector3 eye, Vector4 colour)
    {
        var point = points[row, column];
        var toEye = eye - point;
        var normal = toEye.LengthSquared() > 0f ? Vector3.Normalize(toEye) : Vector3.UnitZ;
        corners.Add(new(point, normal, colour, new Vector2((float)column / CellsPerSide, (float)row / CellsPerSide)) { Tangent = across, Bitangent = along });
    }

    private static Vector3 UnitOr(Vector3 axis, Vector3 fallback) => axis.LengthSquared() > 1e-12f ? Vector3.Normalize(axis) : fallback;
}
