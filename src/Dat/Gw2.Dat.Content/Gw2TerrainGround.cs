using System.Numerics;

namespace Gw2.Dat.Content;

public sealed class Gw2TerrainGround : IGw2ZoneScatterGround
{
    private readonly float[] _heights;
    private readonly int _width;
    private readonly int _height;
    private readonly Vector2 _origin;
    private readonly float _worldUnitsPerSample;
    private readonly IReadOnlyList<uint> _chunkFlags;
    private readonly int _chunksX;
    private readonly int _chunksY;
    private readonly int _tilesPerChunk;

    private Gw2TerrainGround(float[] heights, int width, int height, Vector2 origin, float worldUnitsPerSample, Gw2MapTerrain terrain)
    {
        _tilesPerChunk = terrain.TilesPerChunk;
        _chunksX = (int)terrain.Dims[0] / _tilesPerChunk;
        _chunksY = (int)terrain.Dims[1] / _tilesPerChunk;
        _chunkFlags = terrain.Chunks.Count == _chunksX * _chunksY ? terrain.Chunks.Select(chunk => chunk.Flags).ToArray() : [];
        _worldUnitsPerSample = worldUnitsPerSample;
        _heights = heights;
        _width = width;
        _height = height;
        _origin = origin;
    }

    public static Gw2TerrainGround? From(Gw2MapTerrain terrain, Vector2 origin) =>
        terrain.TryBuildHeightGrid(out var width, out var height, out var heights) && width > 1 && height > 1
            ? new Gw2TerrainGround(heights, width, height, origin, terrain.WorldUnitsPerHeightSample, terrain)
            : null;

    public float HeightAt(Vector2 position)
    {
        var (left, bottom, across, up) = Cell(position);
        var lower = float.Lerp(Sample(left, bottom), Sample(left + 1, bottom), across);
        var upper = float.Lerp(Sample(left, bottom + 1), Sample(left + 1, bottom + 1), across);
        return float.Lerp(lower, upper, up);
    }

    public Vector3 NormalAt(Vector2 position)
    {
        var (left, bottom, across, up) = Cell(position);
        var topLeft = Sample(left, bottom + 1);
        var topRight = Sample(left + 1, bottom + 1);
        var bottomLeft = Sample(left, bottom);
        var bottomRight = Sample(left + 1, bottom);
        var slope = across <= 1f - up
            ? new Vector2(topRight - topLeft, topLeft - bottomLeft)
            : new Vector2(bottomRight - bottomLeft, topRight - bottomRight);
        return Vector3.Normalize(new Vector3(slope / _worldUnitsPerSample, -1f));
    }

    public uint ChunkFlagsAt(Vector2 position)
    {
        if (_chunkFlags.Count == 0) return 0;
        var tileX = (position.X - _origin.X) / _worldUnitsPerSample;
        var tileY = (position.Y - _origin.Y) / _worldUnitsPerSample;
        var chunkColumn = Math.Clamp((int)MathF.Floor(tileX / _tilesPerChunk), 0, _chunksX - 1);
        var chunkRowFromBottom = Math.Clamp((int)MathF.Floor(tileY / _tilesPerChunk), 0, _chunksY - 1);
        return _chunkFlags[(_chunksY - 1 - chunkRowFromBottom) * _chunksX + chunkColumn];
    }

    private (int Left, int Bottom, float Across, float Up) Cell(Vector2 position)
    {
        var column = Math.Clamp((position.X - _origin.X) / _worldUnitsPerSample + Gw2MapTerrain.HeightBlockBorder, 0f, _width - 1);
        var row = Math.Clamp((position.Y - _origin.Y) / _worldUnitsPerSample + Gw2MapTerrain.HeightBlockBorder, 0f, _height - 1);
        var left = Math.Min((int)column, _width - 2);
        var bottom = Math.Min((int)row, _height - 2);
        return (left, bottom, column - left, row - bottom);
    }

    private float Sample(int column, int row) => _heights[row * _width + column];
}
