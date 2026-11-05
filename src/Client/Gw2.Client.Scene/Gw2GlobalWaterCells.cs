using System;
using System.Collections.Generic;
using System.Numerics;
using Gw2.Dat.Content;

namespace Gw2.Client.Scene;

public sealed class Gw2GlobalWaterCells
{
    public const float CellSize = Gw2MapTerrain.ChunkWorldSize;
    private const float BoxHalfThickness = 1f;

    private readonly Vector2 _minimum;
    private readonly Vector2 _maximum;
    private readonly int _columns;
    private readonly int _rows;
    private readonly bool[] _shown;
    private readonly float _surfaceZ;

    private Gw2GlobalWaterCells(Vector2 minimum, Vector2 maximum, int columns, int rows, bool[] shown, float surfaceZ)
    {
        _minimum = minimum;
        _maximum = maximum;
        _columns = columns;
        _rows = rows;
        _shown = shown;
        _surfaceZ = surfaceZ;
    }

    public int ShownCellCount => Array.FindAll(_shown, shown => shown).Length;

    public int CellCount => _shown.Length;

    public static Gw2GlobalWaterCells From(Vector2 mapMinimum, Vector2 mapMaximum, float surfaceZ, float sampleSpacing, Func<Vector2, float> groundHeight, Gw2WaterTerrainCut? terrainCut)
    {
        if (sampleSpacing <= 0f) throw new ArgumentOutOfRangeException(nameof(sampleSpacing));
        var columns = Math.Max(1, (int)MathF.Ceiling((mapMaximum.X - mapMinimum.X) / CellSize));
        var rows = Math.Max(1, (int)MathF.Ceiling((mapMaximum.Y - mapMinimum.Y) / CellSize));
        var samplesPerCell = (int)MathF.Ceiling(CellSize / sampleSpacing);
        var shown = new bool[columns * rows];
        for (var row = 0; row < rows; row++)
        for (var column = 0; column < columns; column++)
        {
            var cellMinimum = mapMinimum + new Vector2(column, row) * CellSize;
            shown[row * columns + column] = AnyWaterShown(cellMinimum, samplesPerCell, sampleSpacing, mapMaximum, surfaceZ, groundHeight, terrainCut);
        }
        return new Gw2GlobalWaterCells(mapMinimum, mapMaximum, columns, rows, shown, surfaceZ);
    }

    public static bool IsWaterShown(Vector2 position, float surfaceZ, Func<Vector2, float> groundHeight, Gw2WaterTerrainCut? terrainCut)
    {
        var depth = groundHeight(position) - surfaceZ;
        if (depth <= 0f) return false;
        return terrainCut is not { } cut || cut.Depth(depth, surfaceZ, cut.ChunkFlags(position)) > 0f;
    }

    public IEnumerable<(Vector3 Minimum, Vector3 Maximum)> ShownBoxes(Vector3 eye, float farClip)
    {
        var reachMinimum = new Vector2(eye.X - farClip, eye.Y - farClip);
        var reachMaximum = new Vector2(eye.X + farClip, eye.Y + farClip);
        var firstColumn = Math.Max(0, (int)MathF.Floor((reachMinimum.X - _minimum.X) / CellSize));
        var lastColumn = Math.Min(_columns - 1, (int)MathF.Floor((reachMaximum.X - _minimum.X) / CellSize));
        var firstRow = Math.Max(0, (int)MathF.Floor((reachMinimum.Y - _minimum.Y) / CellSize));
        var lastRow = Math.Min(_rows - 1, (int)MathF.Floor((reachMaximum.Y - _minimum.Y) / CellSize));
        for (var row = firstRow; row <= lastRow; row++)
        for (var column = firstColumn; column <= lastColumn; column++)
        {
            if (!_shown[row * _columns + column]) continue;
            var cellMinimum = _minimum + new Vector2(column, row) * CellSize;
            yield return Box(cellMinimum, cellMinimum + new Vector2(CellSize));
        }
        foreach (var (minimum, maximum) in OutsideMapStrips())
        {
            var clippedMinimum = Vector2.Max(minimum, reachMinimum);
            var clippedMaximum = Vector2.Min(maximum, reachMaximum);
            if (clippedMinimum.X < clippedMaximum.X && clippedMinimum.Y < clippedMaximum.Y) yield return Box(clippedMinimum, clippedMaximum);
        }
    }

    private IEnumerable<(Vector2 Minimum, Vector2 Maximum)> OutsideMapStrips()
    {
        var far = float.MaxValue;
        yield return (new Vector2(-far, -far), new Vector2(_minimum.X, far));
        yield return (new Vector2(_maximum.X, -far), new Vector2(far, far));
        yield return (new Vector2(_minimum.X, -far), new Vector2(_maximum.X, _minimum.Y));
        yield return (new Vector2(_minimum.X, _maximum.Y), new Vector2(_maximum.X, far));
    }

    private (Vector3 Minimum, Vector3 Maximum) Box(Vector2 minimum, Vector2 maximum) =>
        (new Vector3(minimum, _surfaceZ - BoxHalfThickness), new Vector3(maximum, _surfaceZ + BoxHalfThickness));

    private static bool AnyWaterShown(Vector2 cellMinimum, int samplesPerCell, float sampleSpacing, Vector2 mapMaximum, float surfaceZ, Func<Vector2, float> groundHeight, Gw2WaterTerrainCut? terrainCut)
    {
        for (var sampleY = 0; sampleY <= samplesPerCell; sampleY++)
        for (var sampleX = 0; sampleX <= samplesPerCell; sampleX++)
        {
            var position = Vector2.Min(cellMinimum + new Vector2(sampleX, sampleY) * sampleSpacing, mapMaximum);
            if (IsWaterShown(position, surfaceZ, groundHeight, terrainCut)) return true;
        }
        return false;
    }
}
