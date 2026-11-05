using System;
using System.Collections.Generic;
using System.Numerics;
using Gw2.Dat.Content;

namespace Gw2.Client.Scene;

public readonly record struct Gw2TerrainChunkRect(int MinColumn, int MinRow, int EndColumn, int EndRow)
{
    public bool IsEmpty => EndColumn <= MinColumn || EndRow <= MinRow;

    public bool Contains(int column, int row) => column >= MinColumn && column < EndColumn && row >= MinRow && row < EndRow;

    public IEnumerable<(int Column, int Row)> Chunks()
    {
        for (var row = MinRow; row < EndRow; row++)
        for (var column = MinColumn; column < EndColumn; column++)
            yield return (column, row);
    }
}

public sealed class Gw2TerrainActivation
{
    private const int MinimumSpan = 3;

    private readonly IReadOnlyList<float> _mapBounds;
    private readonly int _columns;
    private readonly int _rows;
    private (int Column, int Row)? _center;

    public Gw2TerrainActivation(Gw2MapTerrain terrain, IReadOnlyList<float> mapBounds)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        _mapBounds = mapBounds ?? throw new ArgumentNullException(nameof(mapBounds));
        _columns = Gw2MapTerrainGeometry.ChunkColumns(terrain);
        _rows = Gw2MapTerrainGeometry.ChunkRows(terrain);
        Span = SpanOf(terrain);
    }

    public int Span { get; }

    public Gw2TerrainChunkRect Active { get; private set; }

    public (int Column, int Row)? Center => _center;

    public static int SpanOf(Gw2MapTerrain terrain) =>
        Math.Max(MinimumSpan, (int)MathF.Round(terrain.SwapDistance / Gw2MapTerrain.ChunkWorldSize));

    public bool Update(Vector3 view, ICollection<(int Column, int Row)> released, ICollection<(int Column, int Row)> activated)
    {
        var center = ((int)MathF.Floor((view.X - _mapBounds[0]) / Gw2MapTerrain.ChunkWorldSize),
            (int)MathF.Floor((_mapBounds[3] - view.Y) / Gw2MapTerrain.ChunkWorldSize));
        if (_center == center) return false;
        _center = center;
        var next = new Gw2TerrainChunkRect(
            Math.Clamp(center.Item1 - Span, 0, _columns), Math.Clamp(center.Item2 - Span, 0, _rows),
            Math.Clamp(center.Item1 + Span + 1, 0, _columns), Math.Clamp(center.Item2 + Span + 1, 0, _rows));
        if (next.IsEmpty) next = default;
        if (next == Active) return false;
        foreach (var chunk in Active.Chunks()) if (!next.Contains(chunk.Column, chunk.Row)) released.Add(chunk);
        foreach (var chunk in next.Chunks()) if (!Active.Contains(chunk.Column, chunk.Row)) activated.Add(chunk);
        Active = next;
        return true;
    }
}
