using System;
using System.Numerics;

namespace Gw2.Client.Scene;

public sealed class Gw2GlobalWaterGrid
{
    private static readonly Vector3 SkirtNormal = new(0f, 0f, -3f);
    private static readonly (int Row, int Column)[] NeighbourRing = [(-1, -1), (-1, 0), (0, 1), (1, 1), (1, 0), (0, -1)];

    private readonly Gw2WaterWaves _waves;
    private readonly float _surfaceZ;
    private readonly float _cellSize;
    private readonly float _halfExtent;
    private readonly float _outerHalfExtent;
    private readonly Func<Vector2, float> _depthWeight;
    private readonly float[] _depthWeights;
    private Vector2? _depthWeightsOrigin;

    public Gw2GlobalWaterGrid(Gw2WaterWaves waves, float surfaceZ, float cellSize, float outerHalfExtent, Func<Vector2, float> depthWeight)
    {
        _waves = waves;
        _surfaceZ = surfaceZ;
        _cellSize = cellSize;
        _halfExtent = waves.Size * cellSize / 2f;
        _outerHalfExtent = outerHalfExtent;
        _depthWeight = depthWeight;
        _depthWeights = new float[waves.Size * waves.Size];
    }

    public int VertexCount => _waves.Size * _waves.Size + 4 * (_waves.Size - 1);

    public float HalfExtent => _halfExtent;

    public float EyeHeightAboveSurface(Vector3 eye) => _surfaceZ - eye.Z;

    public void Fill(Vector3 eye, Vector2 viewDirection, Vector3[] positions, Vector3[] normals, byte[] alphas)
    {
        var size = _waves.Size;
        var origin = Gw2WaterWaves.GridOrigin(new Vector2(eye.X, eye.Y), viewDirection, _halfExtent, _cellSize);
        var offsetX = Gw2WaterWaves.FieldOffset(origin.X, _cellSize, size);
        var offsetY = Gw2WaterWaves.FieldOffset(origin.Y, _cellSize, size);
        var step = 2f * _halfExtent / size;
        if (_depthWeightsOrigin != origin)
        {
            _depthWeightsOrigin = origin;
            for (var row = 0; row < size; row++)
            for (var column = 0; column < size; column++)
                _depthWeights[row * size + column] = _depthWeight(new Vector2(column * step - _halfExtent + origin.X, (row + 1) * step - _halfExtent + origin.Y));
        }
        for (var row = 0; row < size; row++)
        for (var column = 0; column < size; column++)
        {
            var vertex = row * size + column;
            var field = _waves.FieldIndex(row, column, offsetX, offsetY);
            var weight = _waves.WaveWeight(row, column, _depthWeights[vertex]);
            positions[vertex] = new Vector3(
                column * step - _halfExtent + weight * _waves.DisplacementX[field] + origin.X,
                (row + 1) * step - _halfExtent + weight * _waves.DisplacementY[field] + origin.Y,
                _surfaceZ - weight * _waves.Height[field]);
            alphas[vertex] = _waves.Alpha(row, column, _depthWeights[vertex]);
        }
        System.Threading.Tasks.Parallel.For(0, size, row =>
        {
            for (var column = 0; column < size; column++)
                normals[row * size + column] = VertexNormal(positions, size, row, column);
        });
        var ringStep = 2f * _outerHalfExtent / (size - 1);
        var ringVertex = size * size;
        for (var row = 0; row < size; row++)
        for (var column = 0; column < size; column++)
        {
            if (row != 0 && row != size - 1 && column != 0 && column != size - 1) continue;
            positions[ringVertex] = new Vector3(column * ringStep - _outerHalfExtent + eye.X, row * ringStep - _outerHalfExtent + eye.Y, _surfaceZ);
            normals[ringVertex] = SkirtNormal;
            alphas[ringVertex++] = Gw2WaterBodies.OpaqueAlpha;
        }
    }

    public static Vector3 VertexNormal(Vector3[] positions, int size, int row, int column)
    {
        var center = positions[row * size + column];
        Span<Vector3> towardsNeighbours = stackalloc Vector3[NeighbourRing.Length];
        var interior = row > 0 && row < size - 1 && column > 0 && column < size - 1;
        for (var index = 0; index < NeighbourRing.Length; index++)
        {
            var (rowOffset, columnOffset) = NeighbourRing[index];
            towardsNeighbours[index] = interior
                ? positions[(row + rowOffset) * size + column + columnOffset] - center
                : Neighbour(positions, size, row, column, NeighbourRing[index], center) - center;
        }
        var normal = Vector3.Zero;
        for (var index = 0; index < NeighbourRing.Length; index++)
        {
            var next = towardsNeighbours[index + 1 == NeighbourRing.Length ? 0 : index + 1];
            normal += Vector3.Normalize(Vector3.Cross(next, towardsNeighbours[index]));
        }
        return normal;
    }

    private static Vector3 Neighbour(Vector3[] positions, int size, int row, int column, (int Row, int Column) offset, Vector3 center)
    {
        var neighbourRow = row + offset.Row;
        var neighbourColumn = column + offset.Column;
        if (neighbourRow < 0 || neighbourRow >= size || neighbourColumn < 0 || neighbourColumn >= size)
            return center + new Vector3(offset.Column, offset.Row, 0f);
        return positions[neighbourRow * size + neighbourColumn];
    }
}
