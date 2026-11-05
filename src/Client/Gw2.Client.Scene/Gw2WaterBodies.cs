using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Gw2.Dat.Content;

namespace Gw2.Client.Scene;

public readonly record struct Gw2WaterVertex(Vector3 Position, byte Alpha);

public sealed record Gw2WaterMesh(Gw2WaterVertex[] Vertices, int[] Indices);

public sealed record Gw2WaterBody(int Index, bool IsGlobal, bool TessellatesShore, float SurfaceZ, Vector2 Minimum, Vector2 Maximum, IReadOnlyList<Vector2> Polygon)
{
    public bool IsBelowWater(Vector3 eye) => SurfaceZ <= eye.Z;

    public (Vector3 Minimum, Vector3 Maximum) VisibilityBox(Vector3 eye, float farClip) =>
        IsGlobal
            ? (new Vector3(eye.X - farClip, eye.Y - farClip, SurfaceZ - 1f), new Vector3(eye.X + farClip, eye.Y + farClip, SurfaceZ + 1f))
            : (new Vector3(Minimum - Vector2.One, SurfaceZ - 1f), new Vector3(Maximum + Vector2.One, SurfaceZ + 1f));
}

public static class Gw2WaterBodies
{
    public const uint GlobalPlaneFlag = 0x1;
    public const uint SurfaceShoreTessellationFlag = 0x2;
    public const byte OpaqueAlpha = 255;

    public static IReadOnlyList<Gw2WaterBody> From(Gw2MapWater water)
    {
        ArgumentNullException.ThrowIfNull(water);
        var bodies = new List<Gw2WaterBody>();
        if ((water.Flags & GlobalPlaneFlag) != 0)
            bodies.Add(new Gw2WaterBody(0, true, false, water.PlaneZ, new Vector2(-float.MaxValue), new Vector2(float.MaxValue), []));
        foreach (var surface in water.Surfaces.Where(surface => surface.Vertices.Count >= 3))
        {
            var minimum = surface.Vertices.Aggregate(new Vector2(16777215f), Vector2.Min);
            var maximum = surface.Vertices.Aggregate(new Vector2(-16777215f), Vector2.Max);
            bodies.Add(new Gw2WaterBody(bodies.Count, false, (surface.Flags & SurfaceShoreTessellationFlag) != 0, surface.SurfaceZ, minimum, maximum, surface.Vertices));
        }
        return bodies;
    }

    public static float GlobalInnerHalfExtent(int gridSize, float worldUnitsPerTile) => gridSize * worldUnitsPerTile / 2f;

    public static float ShoreGridDensity(float worldUnitsPerTile) => worldUnitsPerTile / 2f;

    public static Gw2WaterMesh GlobalPlane(int size, float surfaceZ, float innerHalfExtent, float outerHalfExtent)
    {
        var vertices = new List<Gw2WaterVertex>(size * size + 4 * size);
        var indices = new List<int>();
        var innerStep = 2f * innerHalfExtent / (size - 1);
        for (var row = 0; row < size; row++)
        for (var column = 0; column < size; column++)
            vertices.Add(new Gw2WaterVertex(new Vector3(column * innerStep - innerHalfExtent, row * innerStep - innerHalfExtent, surfaceZ), 0));
        for (var row = 0; row < size - 1; row++)
        for (var column = 0; column < size - 1; column++)
        {
            var vertex = row * size + column;
            indices.AddRange([vertex, vertex + size + 1, vertex + 1, vertex, vertex + size, vertex + size + 1]);
        }
        var outerStep = 2f * outerHalfExtent / (size - 1);
        var ring = new int[size, size];
        for (var row = 0; row < size; row++)
        for (var column = 0; column < size; column++)
        {
            if (row != 0 && row != size - 1 && column != 0 && column != size - 1) continue;
            ring[row, column] = vertices.Count;
            vertices.Add(new Gw2WaterVertex(new Vector3(column * outerStep - outerHalfExtent, row * outerStep - outerHalfExtent, surfaceZ), 0));
        }
        for (var index = 0; index < size - 1; index++)
        {
            indices.AddRange([ring[0, index], index, ring[0, index + 1], index, index + 1, ring[0, index + 1]]);
            indices.AddRange([(size - 1) * size + index, ring[size - 1, index], (size - 1) * size + index + 1, ring[size - 1, index], ring[size - 1, index + 1], (size - 1) * size + index + 1]);
            indices.AddRange([ring[index, 0], ring[index + 1, 0], index * size, ring[index + 1, 0], (index + 1) * size, index * size]);
            indices.AddRange([index * size + size - 1, (index + 1) * size + size - 1, ring[index, size - 1], (index + 1) * size + size - 1, ring[index + 1, size - 1], ring[index, size - 1]]);
        }
        return new Gw2WaterMesh(vertices.ToArray(), indices.ToArray());
    }

    public static Gw2WaterMesh Surface(Gw2WaterBody body, Func<Vector2, float> groundHeight, float worldUnitsPerTile, float shoreDepth)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(groundHeight);
        if (!body.TessellatesShore)
        {
            var polygon = body.Polygon.ToArray();
            return new Gw2WaterMesh(
                polygon.Select(point => new Gw2WaterVertex(new Vector3(point, body.SurfaceZ), OpaqueAlpha)).ToArray(),
                Triangulate(polygon).ToArray());
        }
        var scale = shoreDepth == 0f ? 1f : 1f / shoreDepth;
        var vertices = new List<Gw2WaterVertex>();
        var indices = new List<int>();
        foreach (var piece in ClipToGrid(body.Polygon, ShoreGridDensity(worldUnitsPerTile)))
        {
            var start = vertices.Count;
            foreach (var point in piece)
                vertices.Add(new Gw2WaterVertex(new Vector3(point, body.SurfaceZ), ShoreAlpha(groundHeight(point), body.SurfaceZ, scale)));
            indices.AddRange(Triangulate(piece).Select(index => start + index));
        }
        return new Gw2WaterMesh(vertices.ToArray(), indices.ToArray());
    }

    public static byte GlobalAlpha(Vector3 localPosition, Vector3 worldPosition, float innerHalfExtent, Func<Vector2, float> groundHeight, float inverseShoreDepth, Gw2WaterTerrainCut? terrainCut = null)
    {
        var insideInnerGrid = MathF.Abs(localPosition.X) <= innerHalfExtent && MathF.Abs(localPosition.Y) <= innerHalfExtent;
        if (!insideInnerGrid) return OpaqueAlpha;
        var position = new Vector2(worldPosition.X, worldPosition.Y);
        var depth = groundHeight(position) - worldPosition.Z;
        if (terrainCut is { } cut) depth = cut.Depth(depth, worldPosition.Z, cut.ChunkFlags(position));
        return DepthAlpha(depth, inverseShoreDepth);
    }

    public static byte ShoreAlpha(float groundZ, float surfaceZ, float inverseShoreDepth) => DepthAlpha(groundZ - surfaceZ, inverseShoreDepth);

    private static byte DepthAlpha(float depth, float inverseShoreDepth) =>
        (byte)MathF.Round(Math.Clamp(depth * inverseShoreDepth, 0f, 1f) * 255f);

    public static IEnumerable<Vector2[]> ClipToGrid(IReadOnlyList<Vector2> polygon, float gridDensity)
    {
        if (gridDensity <= 0f) throw new ArgumentOutOfRangeException(nameof(gridDensity));
        var minimum = polygon.Aggregate(new Vector2(16777215f), Vector2.Min);
        var maximum = polygon.Aggregate(new Vector2(-16777215f), Vector2.Max);
        var columns = Math.Max(1, (int)MathF.Ceiling((maximum.X - minimum.X) / gridDensity));
        var rows = Math.Max(1, (int)MathF.Ceiling((maximum.Y - minimum.Y) / gridDensity));
        for (var row = 0; row < rows; row++)
        for (var column = 0; column < columns; column++)
        {
            var cellMinimum = minimum + new Vector2(column, row) * gridDensity;
            var piece = ClipToRectangle(polygon, cellMinimum, cellMinimum + new Vector2(gridDensity));
            if (piece.Count >= 3 && MathF.Abs(SignedArea(piece)) > 1e-3f) yield return piece.ToArray();
        }
    }

    private static List<Vector2> ClipToRectangle(IReadOnlyList<Vector2> polygon, Vector2 minimum, Vector2 maximum)
    {
        var points = polygon.ToList();
        points = ClipToEdge(points, point => point.X - minimum.X, (start, end) => Across(start, end, (minimum.X - start.X) / (end.X - start.X)));
        points = ClipToEdge(points, point => maximum.X - point.X, (start, end) => Across(start, end, (maximum.X - start.X) / (end.X - start.X)));
        points = ClipToEdge(points, point => point.Y - minimum.Y, (start, end) => Across(start, end, (minimum.Y - start.Y) / (end.Y - start.Y)));
        points = ClipToEdge(points, point => maximum.Y - point.Y, (start, end) => Across(start, end, (maximum.Y - start.Y) / (end.Y - start.Y)));
        return points;
    }

    private static Vector2 Across(Vector2 start, Vector2 end, float fraction) => Vector2.Lerp(start, end, fraction);

    private static List<Vector2> ClipToEdge(List<Vector2> points, Func<Vector2, float> inside, Func<Vector2, Vector2, Vector2> crossing)
    {
        var clipped = new List<Vector2>(points.Count + 4);
        for (var index = 0; index < points.Count; index++)
        {
            var current = points[index];
            var previous = points[(index + points.Count - 1) % points.Count];
            var currentInside = inside(current) >= 0f;
            var previousInside = inside(previous) >= 0f;
            if (currentInside != previousInside) clipped.Add(crossing(previous, current));
            if (currentInside) clipped.Add(current);
        }
        return clipped;
    }

    public static List<int> Triangulate(IReadOnlyList<Vector2> polygon)
    {
        var triangles = new List<int>();
        var remaining = Enumerable.Range(0, polygon.Count).ToList();
        if (SignedArea(polygon) < 0f) remaining.Reverse();
        var guard = remaining.Count * remaining.Count;
        while (remaining.Count > 3 && guard-- > 0)
        {
            var clipped = false;
            for (var index = 0; index < remaining.Count; index++)
            {
                var previous = remaining[(index + remaining.Count - 1) % remaining.Count];
                var current = remaining[index];
                var next = remaining[(index + 1) % remaining.Count];
                if (!IsEar(polygon, remaining, previous, current, next)) continue;
                triangles.AddRange([previous, current, next]);
                remaining.RemoveAt(index);
                clipped = true;
                break;
            }
            if (!clipped) break;
        }
        if (remaining.Count == 3) triangles.AddRange(remaining);
        return triangles;
    }

    private static bool IsEar(IReadOnlyList<Vector2> polygon, List<int> remaining, int previous, int current, int next)
    {
        var a = polygon[previous];
        var b = polygon[current];
        var c = polygon[next];
        if (Cross(b - a, c - b) <= 0f) return false;
        foreach (var other in remaining)
        {
            if (other == previous || other == current || other == next) continue;
            if (InsideTriangle(polygon[other], a, b, c)) return false;
        }
        return true;
    }

    private static bool InsideTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c) =>
        Cross(b - a, point - a) >= 0f && Cross(c - b, point - b) >= 0f && Cross(a - c, point - c) >= 0f;

    private static float Cross(Vector2 first, Vector2 second) => first.X * second.Y - first.Y * second.X;

    private static float SignedArea(IReadOnlyList<Vector2> polygon)
    {
        var area = 0f;
        for (var index = 0; index < polygon.Count; index++)
            area += Cross(polygon[index], polygon[(index + 1) % polygon.Count]);
        return area / 2f;
    }
}

public sealed record Gw2WaterTerrainCut(bool CutsOpaqueWater, float DeepCutoff, Func<Vector2, uint> ChunkFlags)
{
    public const uint MapWaterTerrainCutFlag = 0x2;
    public const uint ChunkOpaqueWaterFlag = 0x2;
    public const uint ChunkClearWaterFlag = 0x20;
    public const float MinimumDeepCutoff = 400f;
    public const float OpaqueDepth = 30000f;
    private const float WaveCutoffScale = 1000f;

    public static float DeepCutoffOf(float animAmplitude, Vector2 animWind) => MathF.Max(MinimumDeepCutoff, animAmplitude * WaveCutoffScale * animWind.Length());

    public float Depth(float depth, float surfaceZ, uint chunkFlags)
    {
        var opaque = (chunkFlags & ChunkOpaqueWaterFlag) != 0;
        var clear = (chunkFlags & ChunkClearWaterFlag) != 0;
        if (opaque && clear) depth = 0f;
        if (CutsOpaqueWater && (opaque || surfaceZ - DeepCutoff >= depth) && !clear) depth = OpaqueDepth;
        return depth;
    }
}
