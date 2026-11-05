using System.Numerics;

namespace Gw2.Dat.Content;

public readonly record struct Gw2MapTerrainVertex(
    Vector3 Position,
    Vector3 Normal,
    Vector3 Tangent,
    Vector3 Bitangent,
    Vector2 AtlasUv,
    Vector2 SurfaceUv,
    Vector2 DetailUv);

public sealed record Gw2MapTerrainChunkGeometry(int Column, int Row, Vector3 Origin, Gw2MapTerrainVertex[] Vertices, int[] Indices);

public static class Gw2MapTerrainGeometry
{
    public const int AtlasQuadrantsPerPage = 4;
    public const uint FullDetailChunkFlag = 0x2000;
    public const uint PlanarSurfaceUvChunkFlag = 0x1;
    private const int SurfaceUvSet = 1;
    private const float ProjectedHeightBias = 65535f;
    private const float TopDownNormalLimit = 0.5f;
    private const float DegenerateNormalLengthSquared = 1e-12f;
    private const float AtlasQuadrant = 1f / AtlasQuadrantsPerPage;
    private const float AtlasGuardBand = 127f / 128f;
    private const float AtlasHalfTexel = 1f / 1024f;
    private const float NormalVertical = -2f;
    private const int BlockBorder = Gw2MapTerrain.HeightBlockBorder;
    public static int ChunkColumns(Gw2MapTerrain terrain) => (int)(terrain.Dims[0] / terrain.TilesPerChunk);

    public static int ChunkRows(Gw2MapTerrain terrain) => (int)(terrain.Dims[1] / terrain.TilesPerChunk);

    public static Vector3 ChunkOrigin(IReadOnlyList<float> mapBounds, int column, int row) =>
        new(mapBounds[0] + column * Gw2MapTerrain.ChunkWorldSize, mapBounds[3] - row * Gw2MapTerrain.ChunkWorldSize, 0f);

    public static Func<int, int, float> ChunkHeights(Gw2MapTerrain terrain, int column, int row)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        var blockSize = terrain.HeightBlockSize;
        var blockOffset = (row * ChunkColumns(terrain) + column) * blockSize * blockSize;
        return (x, y) => terrain.Heights[blockOffset + (y + BlockBorder) * blockSize + x + BlockBorder];
    }

    public static List<Gw2MapTerrainBox> ChunkLeaves(Gw2MapTerrain terrain, IReadOnlyList<float> mapBounds, int column, int row, Vector3 eye)
    {
        var height = ChunkHeights(terrain, column, row);
        var origin = ChunkOrigin(mapBounds, column, row);
        var tile = terrain.WorldUnitsPerHeightSample;
        Vector3 Corner(int x, int y) => origin + new Vector3(x * tile, -y * tile, height(x, y));
        var last = terrain.TilesPerChunk;
        return Gw2MapTerrainLod.SelectLeaves(Gw2MapTerrainLod.BoxErrors(height, last, tile), last, eye, Corner(0, 0), Corner(last, 0), Corner(0, last), Corner(last, last));
    }

    public static Gw2MapTerrainChunkGeometry BuildChunk(Gw2MapTerrain terrain, IReadOnlyList<float> mapBounds, int column, int row, Vector3 eye)
    {
        var geometry = BuildGrid(terrain, mapBounds, column, row);
        if (terrain.Chunks.ElementAtOrDefault(row * ChunkColumns(terrain) + column) is { } chunk && (chunk.Flags & FullDetailChunkFlag) != 0) return ProjectSteepFaces(terrain, geometry);
        var leaves = ChunkLeaves(terrain, mapBounds, column, row, eye);
        var borderVertices = new HashSet<(int X, int Y)>();
        var last = terrain.TilesPerChunk;
        void AddNeighbour(int neighbourColumn, int neighbourRow, Func<int, int, (int X, int Y)?> toLocal)
        {
            if (neighbourColumn < 0 || neighbourRow < 0 || neighbourColumn >= ChunkColumns(terrain) || neighbourRow >= ChunkRows(terrain)) return;
            foreach (var corner in ChunkLeaves(terrain, mapBounds, neighbourColumn, neighbourRow, eye).SelectMany(BoxCorners))
                if (toLocal(corner.X, corner.Y) is { } local) borderVertices.Add(local);
        }

        AddNeighbour(column - 1, row, (x, y) => x == last ? (0, y) : null);
        AddNeighbour(column + 1, row, (x, y) => x == 0 ? (last, y) : null);
        AddNeighbour(column, row - 1, (x, y) => y == last ? (x, 0) : null);
        AddNeighbour(column, row + 1, (x, y) => y == 0 ? (x, last) : null);
        return ProjectSteepFaces(terrain, geometry with { Indices = LeafIndices(leaves, borderVertices, last + 1) });
    }

    private static IEnumerable<(int X, int Y)> BoxCorners(Gw2MapTerrainBox box)
    {
        yield return (box.X, box.Y);
        yield return (box.X + box.Size, box.Y);
        yield return (box.X, box.Y + box.Size);
        yield return (box.X + box.Size, box.Y + box.Size);
    }

    private static int[] LeafIndices(List<Gw2MapTerrainBox> leaves, HashSet<(int X, int Y)> borderVertices, int verticesPerSide)
    {
        var used = new HashSet<(int X, int Y)>(borderVertices);
        foreach (var leaf in leaves)
            used.UnionWith(BoxCorners(leaf));
        int Vertex(int x, int y) => y * verticesPerSide + x;
        var indices = new List<int>();
        var perimeter = new List<(int X, int Y)>();
        foreach (var leaf in leaves)
        {
            perimeter.Clear();
            var right = leaf.X + leaf.Size;
            var bottom = leaf.Y + leaf.Size;
            for (var y = leaf.Y; y < bottom; y++) if (used.Contains((leaf.X, y))) perimeter.Add((leaf.X, y));
            for (var x = leaf.X; x < right; x++) if (used.Contains((x, bottom))) perimeter.Add((x, bottom));
            for (var y = bottom; y > leaf.Y; y--) if (used.Contains((right, y))) perimeter.Add((right, y));
            for (var x = right; x > leaf.X; x--) if (used.Contains((x, leaf.Y))) perimeter.Add((x, leaf.Y));
            if (perimeter.Count == 4)
            {
                indices.AddRange([Vertex(leaf.X, leaf.Y), Vertex(leaf.X, bottom), Vertex(right, leaf.Y), Vertex(right, leaf.Y), Vertex(leaf.X, bottom), Vertex(right, bottom)]);
                continue;
            }

            var centre = Vertex(leaf.X + leaf.Size / 2, leaf.Y + leaf.Size / 2);
            for (var corner = 0; corner < perimeter.Count; corner++)
            {
                var next = perimeter[(corner + 1) % perimeter.Count];
                indices.AddRange([centre, Vertex(perimeter[corner].X, perimeter[corner].Y), Vertex(next.X, next.Y)]);
            }
        }

        return indices.ToArray();
    }

    public static Gw2MapTerrainChunkGeometry BuildChunk(Gw2MapTerrain terrain, IReadOnlyList<float> mapBounds, int column, int row) =>
        ProjectSteepFaces(terrain, BuildGrid(terrain, mapBounds, column, row));

    public static Gw2MapTerrainChunkGeometry ProjectSteepFaces(Gw2MapTerrain terrain, Gw2MapTerrainChunkGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(geometry);
        var chunkIndex = geometry.Row * ChunkColumns(terrain) + geometry.Column;
        if (terrain.Chunks.ElementAtOrDefault(chunkIndex) is { } chunk && (chunk.Flags & PlanarSurfaceUvChunkFlag) != 0) return geometry;
        var tiling = terrain.ChunkMaterials.ElementAtOrDefault(chunkIndex)?.Tiling ?? [];
        var heightScale = tiling.ElementAtOrDefault(SurfaceUvSet) / (float)terrain.TilesPerChunk;
        var inverseTile = 1f / terrain.WorldUnitsPerHeightSample;
        var source = geometry.Vertices;
        var projected = new List<Gw2MapTerrainVertex>(source);
        var emitted = new Dictionary<(int Vertex, Gw2TerrainProjectionAxis Axis), int>();
        var indices = new int[geometry.Indices.Length];
        for (var corner = 0; corner + 2 < indices.Length; corner += 3)
        {
            var first = source[geometry.Indices[corner]].Position;
            var second = source[geometry.Indices[corner + 1]].Position;
            var third = source[geometry.Indices[corner + 2]].Position;
            var (axis, sign) = ProjectionAxis(Vector3.Cross(first - second, third - second));
            for (var offset = 0; offset < 3; offset++)
            {
                var vertexIndex = geometry.Indices[corner + offset];
                var index = vertexIndex;
                if (axis != Gw2TerrainProjectionAxis.TopDown && !emitted.TryGetValue((vertexIndex, axis), out index))
                {
                    var vertex = source[vertexIndex];
                    var heightCoordinate = MathF.Truncate((vertex.Position.Z + ProjectedHeightBias) * inverseTile) * heightScale * sign;
                    var surfaceUv = axis switch
                    {
                        Gw2TerrainProjectionAxis.NegativeX or Gw2TerrainProjectionAxis.PositiveX => vertex.SurfaceUv with { X = heightCoordinate },
                        Gw2TerrainProjectionAxis.NegativeY or Gw2TerrainProjectionAxis.PositiveY => vertex.SurfaceUv with { Y = heightCoordinate },
                        _ => vertex.SurfaceUv,
                    };
                    index = projected.Count;
                    projected.Add(vertex with { SurfaceUv = surfaceUv });
                    emitted[(vertexIndex, axis)] = index;
                }

                indices[corner + offset] = index;
            }
        }

        return geometry with { Vertices = projected.ToArray(), Indices = indices };
    }

    private static (Gw2TerrainProjectionAxis Axis, float Sign) ProjectionAxis(Vector3 cross)
    {
        var lengthSquared = cross.LengthSquared();
        var normal = lengthSquared >= DegenerateNormalLengthSquared ? cross / MathF.Sqrt(lengthSquared) : Vector3.Zero;
        if (MathF.Abs(normal.Z) >= TopDownNormalLimit) return (Gw2TerrainProjectionAxis.TopDown, 1f);
        if (MathF.Abs(normal.X) > MathF.Abs(normal.Y))
            return normal.X > 0f ? (Gw2TerrainProjectionAxis.PositiveX, 1f) : (Gw2TerrainProjectionAxis.NegativeX, -1f);
        return normal.Y > 0f ? (Gw2TerrainProjectionAxis.PositiveY, -1f) : (Gw2TerrainProjectionAxis.NegativeY, 1f);
    }

    private static Gw2MapTerrainChunkGeometry BuildGrid(Gw2MapTerrain terrain, IReadOnlyList<float> mapBounds, int column, int row)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(mapBounds);
        var chunkIndex = row * ChunkColumns(terrain) + column;
        var tiling = terrain.ChunkMaterials.ElementAtOrDefault(chunkIndex)?.Tiling ?? [];
        var height = ChunkHeights(terrain, column, row);

        var origin = ChunkOrigin(mapBounds, column, row);
        var tile = terrain.WorldUnitsPerHeightSample;
        var perVertex = 1f / terrain.TilesPerChunk;
        var verticesPerSide = terrain.TilesPerChunk + 1;
        var atlasOffset = new Vector2((column & 3) * AtlasQuadrant + AtlasHalfTexel, (row & 3) * AtlasQuadrant + AtlasHalfTexel);
        var vertices = new Gw2MapTerrainVertex[verticesPerSide * verticesPerSide];
        for (var y = 0; y < verticesPerSide; y++)
        for (var x = 0; x < verticesPerSide; x++)
        {
            var normal = Vector3.Normalize(new Vector3(
                (height(x + 1, y) - height(x - 1, y)) / tile,
                (height(x, y - 1) - height(x, y + 1)) / tile,
                NormalVertical));
            var bitangent = new Vector3(0f, normal.Z, -normal.Y);
            var sample = new Vector2(x, y);
            vertices[y * verticesPerSide + x] = new Gw2MapTerrainVertex(
                origin + new Vector3(x * tile, -y * tile, height(x, y)),
                normal,
                Vector3.Cross(bitangent, normal),
                bitangent,
                sample * (AtlasGuardBand * perVertex * AtlasQuadrant) + atlasOffset,
                TiledUv(sample, perVertex, tiling.ElementAtOrDefault(1)),
                TiledUv(sample, perVertex, tiling.ElementAtOrDefault(2)));
        }

        return new Gw2MapTerrainChunkGeometry(column, row, origin, vertices, GridIndices(terrain.TilesPerChunk));
    }

    private static Vector2 TiledUv(Vector2 sample, float perVertex, byte tiling) =>
        sample * (perVertex * tiling) - new Vector2(tiling * 0.5f);

    private static int[] GridIndices(int tilesPerChunk)
    {
        var verticesPerSide = tilesPerChunk + 1;
        var indices = new int[tilesPerChunk * tilesPerChunk * 6];
        var next = 0;
        for (var y = 0; y < tilesPerChunk; y++)
        for (var x = 0; x < tilesPerChunk; x++)
        {
            var topLeft = y * verticesPerSide + x;
            var bottomLeft = topLeft + verticesPerSide;
            indices[next++] = topLeft;
            indices[next++] = bottomLeft;
            indices[next++] = topLeft + 1;
            indices[next++] = topLeft + 1;
            indices[next++] = bottomLeft;
            indices[next++] = bottomLeft + 1;
        }

        return indices;
    }
}

public enum Gw2TerrainProjectionAxis
{
    TopDown = 0,
    NegativeY = 1,
    PositiveY = 2,
    NegativeX = 3,
    PositiveX = 4,
}
