using System;
using System.Collections.Generic;
using System.Numerics;

namespace Gw2.Client.Scene;

public sealed class Gw2PlacementGrid<T>
{
    public const float DefaultCellSize = 1024f;

    private readonly float _cellSize;
    private readonly Dictionary<(int X, int Y), List<T>> _cells = new();

    public Gw2PlacementGrid(IEnumerable<T> items, Func<T, Vector2> positionOf, float cellSize = DefaultCellSize)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(positionOf);
        _cellSize = cellSize;
        foreach (var item in items)
        {
            var position = positionOf(item);
            var cell = ((int)MathF.Floor(position.X / cellSize), (int)MathF.Floor(position.Y / cellSize));
            if (!_cells.TryGetValue(cell, out var bucket)) _cells[cell] = bucket = new List<T>();
            bucket.Add(item);
        }
    }

    public void CollectChanged(Gw2PlacementRect? previous, Gw2PlacementRect current, List<T> candidates)
    {
        var bounds = previous is { } before ? Gw2PlacementRect.Union(before, current) : current;
        var firstX = (int)MathF.Floor(bounds.Min.X / _cellSize);
        var firstY = (int)MathF.Floor(bounds.Min.Y / _cellSize);
        var lastX = (int)MathF.Floor(bounds.Max.X / _cellSize);
        var lastY = (int)MathF.Floor(bounds.Max.Y / _cellSize);
        for (var y = firstY; y <= lastY; y++)
        for (var x = firstX; x <= lastX; x++)
        {
            if (!_cells.TryGetValue((x, y), out var bucket)) continue;
            var cell = new Gw2PlacementRect(new Vector2(x * _cellSize, y * _cellSize), new Vector2((x + 1) * _cellSize, (y + 1) * _cellSize));
            var insideBefore = previous is { } prior && prior.Covers(cell);
            var outsideBefore = previous is not { } other || !other.Overlaps(cell);
            if (insideBefore && current.Covers(cell)) continue;
            if (outsideBefore && !current.Overlaps(cell)) continue;
            candidates.AddRange(bucket);
        }
    }
}

public readonly record struct Gw2PlacementRect(Vector2 Min, Vector2 Max)
{
    public bool Covers(Gw2PlacementRect other) =>
        other.Min.X >= Min.X && other.Min.Y >= Min.Y && other.Max.X <= Max.X && other.Max.Y <= Max.Y;

    public bool Overlaps(Gw2PlacementRect other) =>
        other.Min.X < Max.X && other.Min.Y < Max.Y && other.Max.X > Min.X && other.Max.Y > Min.Y;

    public static Gw2PlacementRect Union(Gw2PlacementRect left, Gw2PlacementRect right) =>
        new(Vector2.Min(left.Min, right.Min), Vector2.Max(left.Max, right.Max));
}
