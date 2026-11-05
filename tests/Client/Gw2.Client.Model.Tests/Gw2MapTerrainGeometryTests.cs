using Gw2.Formats;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2MapTerrainGeometryTests
{
    private const uint WayfarerFoothillsMapFileId = 187611;
    private static readonly Vector3 HerosMootGround = new(598.2f, -30356.2f, -1566.2f);
    private const uint DeepwoodMapFileId = 3776288;
    private static readonly Vector3 DeepwoodWaypointGround = new(-492.7552f / 0.0254f, 298.3665f / 0.0254f, -55.16562f / 0.0254f);

    [TestMethod]
    public void BuildsSixtyFourTileChunksAndPlacesTheGroundUnderTheDeepwoodWaypoint()
    {
        var datPath = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(datPath) || !File.Exists(datPath)) Assert.Inconclusive("Gw2.dat is not installed.");
        using var reader = Gw2ContentReader.Open(datPath, verifyChecksums: false);
        var map = reader.ReadMap(DeepwoodMapFileId)!;
        var terrain = map.Terrain!;
        var bounds = map.Info.Bounds;
        Assert.AreEqual(64, terrain.TilesPerChunk);
        Assert.AreEqual(terrain.Chunks.Count, Gw2MapTerrainGeometry.ChunkColumns(terrain) * Gw2MapTerrainGeometry.ChunkRows(terrain));
        Assert.IsTrue(terrain.TryBuildHeightGrid(out _, out _, out _));

        var column = (int)((DeepwoodWaypointGround.X - bounds[0]) / Gw2MapTerrain.ChunkWorldSize);
        var row = (int)((bounds[3] - DeepwoodWaypointGround.Y) / Gw2MapTerrain.ChunkWorldSize);
        var chunk = Gw2MapTerrainGeometry.BuildChunk(terrain, bounds, column, row);
        Assert.AreEqual(65 * 65, chunk.Vertices.Length);
        var nearest = chunk.Vertices.MinBy(vertex => new Vector2(vertex.Position.X - DeepwoodWaypointGround.X, vertex.Position.Y - DeepwoodWaypointGround.Y).Length());
        Console.WriteLine($"ground {nearest.Position.Z} live {DeepwoodWaypointGround.Z}");
        Assert.AreEqual(DeepwoodWaypointGround.Z, nearest.Position.Z, 30f);
    }

    [TestMethod]
    public void PlacesTheGroundUnderHerosMootAtTheLiveHeightAndSharesChunkEdges()
    {
        var datPath = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(datPath) || !File.Exists(datPath)) Assert.Inconclusive("Gw2.dat is not installed.");
        using var reader = Gw2ContentReader.Open(datPath, verifyChecksums: false);
        var map = reader.ReadMap(WayfarerFoothillsMapFileId)!;
        var terrain = map.Terrain!;
        var bounds = map.Info.Bounds;

        var column = (int)((HerosMootGround.X - bounds[0]) / Gw2MapTerrain.ChunkWorldSize);
        var row = (int)((bounds[3] - HerosMootGround.Y) / Gw2MapTerrain.ChunkWorldSize);
        var chunk = Gw2MapTerrainGeometry.BuildChunk(terrain, bounds, column, row);
        var nearest = chunk.Vertices.MinBy(vertex => new Vector2(vertex.Position.X - HerosMootGround.X, vertex.Position.Y - HerosMootGround.Y).Length());
        Console.WriteLine($"chunk {column},{row} nearest {nearest.Position} normal {nearest.Normal}");
        Assert.AreEqual(HerosMootGround.Z, nearest.Position.Z, 64f);

        var right = Gw2MapTerrainGeometry.BuildChunk(terrain, bounds, column + 1, row);
        var below = Gw2MapTerrainGeometry.BuildChunk(terrain, bounds, column, row + 1);
        var side = terrain.TilesPerChunk + 1;
        for (var index = 0; index < side; index++)
        {
            Assert.AreEqual(chunk.Vertices[index * side + side - 1].Position, right.Vertices[index * side].Position);
            Assert.AreEqual(chunk.Vertices[(side - 1) * side + index].Position, below.Vertices[index].Position);
        }
        }

    [TestMethod]
    public void DecimatesFarChunksAndKeepsChunkEdgesWatertight()
    {
        var datPath = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(datPath) || !File.Exists(datPath)) Assert.Inconclusive("Gw2.dat is not installed.");
        using var reader = Gw2ContentReader.Open(datPath, verifyChecksums: false);
        var map = reader.ReadMap(WayfarerFoothillsMapFileId)!;
        var terrain = map.Terrain!;
        var bounds = map.Info.Bounds;
        var column = (int)((HerosMootGround.X - bounds[0]) / Gw2MapTerrain.ChunkWorldSize);
        var row = (int)((bounds[3] - HerosMootGround.Y) / Gw2MapTerrain.ChunkWorldSize);
        var eye = HerosMootGround + new Vector3(0f, 0f, 300f);
        var fullGrid = terrain.TilesPerChunk * terrain.TilesPerChunk * 6;

        var near = Gw2MapTerrainGeometry.BuildChunk(terrain, bounds, column, row, eye);
        var far = Gw2MapTerrainGeometry.BuildChunk(terrain, bounds, column + 6, row - 6, eye);
        Console.WriteLine($"near {near.Indices.Length / 3} far {far.Indices.Length / 3} full {fullGrid / 3}");
        Assert.IsTrue(far.Indices.Length < near.Indices.Length);
        Assert.IsTrue(far.Indices.Length < fullGrid);

        for (var step = 0; step < 6; step++)
        {
            var left = Gw2MapTerrainGeometry.BuildChunk(terrain, bounds, column + step, row - step, eye);
            var right = Gw2MapTerrainGeometry.BuildChunk(terrain, bounds, column + step + 1, row - step, eye);
            CollectionAssert.AreEquivalent(EdgePositions(left, vertex => vertex.Position.X == left.Origin.X + Gw2MapTerrain.ChunkWorldSize), EdgePositions(right, vertex => vertex.Position.X == right.Origin.X));
            Assert.AreEqual(0, OpenInteriorEdges(left, terrain.TilesPerChunk + 1));
        }
    }

    private static Vector3[] EdgePositions(Gw2MapTerrainChunkGeometry chunk, Func<Gw2MapTerrainVertex, bool> onEdge) =>
        chunk.Indices.Select(index => chunk.Vertices[index]).Where(onEdge).Select(vertex => vertex.Position).Distinct().ToArray();

    private static int OpenInteriorEdges(Gw2MapTerrainChunkGeometry chunk, int side)
    {
        var gridIndexAt = Enumerable.Range(0, side * side).ToDictionary(index => chunk.Vertices[index].Position);
        var counts = new Dictionary<(int, int), int>();
        for (var triangle = 0; triangle < chunk.Indices.Length; triangle += 3)
        for (var corner = 0; corner < 3; corner++)
        {
            var from = gridIndexAt[chunk.Vertices[chunk.Indices[triangle + corner]].Position];
            var to = gridIndexAt[chunk.Vertices[chunk.Indices[triangle + (corner + 1) % 3]].Position];
            var key = (Math.Min(from, to), Math.Max(from, to));
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }

        bool OnBorder(int index) => index % side == 0 || index % side == side - 1 || index / side == 0 || index / side == side - 1;
        return counts.Count(pair => pair.Value != 2 && !(OnBorder(pair.Key.Item1) && OnBorder(pair.Key.Item2)));
    }

    [TestMethod]
    public void ProjectsTheCliffUnderTheHoelbrakPlatformAlongItsHeight()
    {
        var datPath = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(datPath) || !File.Exists(datPath)) Assert.Inconclusive("Gw2.dat is not installed.");
        using var reader = Gw2ContentReader.Open(datPath, verifyChecksums: false);
        var map = reader.ReadMap(WayfarerFoothillsMapFileId)!;
        var terrain = map.Terrain!;
        var bounds = map.Info.Bounds;
        var cliff = new Vector2(-4900f, -25850f);
        var column = (int)((cliff.X - bounds[0]) / Gw2MapTerrain.ChunkWorldSize);
        var row = (int)((bounds[3] - cliff.Y) / Gw2MapTerrain.ChunkWorldSize);

        var chunk = Gw2MapTerrainGeometry.BuildChunk(terrain, bounds, column, row);
        var side = terrain.TilesPerChunk + 1;
        var tiling = terrain.ChunkMaterials[row * Gw2MapTerrainGeometry.ChunkColumns(terrain) + column].Tiling[1];
        var step = tiling / (float)terrain.TilesPerChunk;
        var sideProjected = chunk.Vertices.Skip(side * side).ToList();
        Console.WriteLine($"chunk {column},{row} tiling {tiling} side-projected vertices {sideProjected.Count}");

        Assert.IsTrue(sideProjected.Count > 0);
        Assert.IsTrue(chunk.Indices.Any(index => index >= side * side));
        foreach (var vertex in sideProjected)
        {
            var planar = vertex.SurfaceUv;
            var heightRows = MathF.Truncate((vertex.Position.Z + 65535f) / terrain.WorldUnitsPerHeightSample);
            var matchesU = MathF.Abs(MathF.Abs(planar.X) - heightRows * step) < 1e-3f;
            var matchesV = MathF.Abs(MathF.Abs(planar.Y) - heightRows * step) < 1e-3f;
            Assert.IsTrue(matchesU || matchesV, $"{vertex.Position} {planar}");
        }
    }
}
