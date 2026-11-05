using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Scene.Tests;

[TestClass]
public sealed class WaterBodiesTests
{
    private const uint BeachMapFileBaseId = 3719751;
    private const uint DeepwoodMapFileId = 3776288;

    [TestMethod]
    public void DeepwoodWaterSurfacesBecomeBodiesThatCoverTheirPolygons()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var map = reader.ReadMap(DeepwoodMapFileId)!;
        var water = map.Water!;
        var terrain = map.Terrain!;
        var ground = Gw2TerrainGround.From(terrain, new Vector2(map.Info.Bounds[0], map.Info.Bounds[1]))!;
        var bodies = Gw2WaterBodies.From(water);

        var hasGlobal = (water.Flags & Gw2WaterBodies.GlobalPlaneFlag) != 0;
        Assert.AreEqual(water.Surfaces.Count(surface => surface.Vertices.Count >= 3) + (hasGlobal ? 1 : 0), bodies.Count);
        Assert.AreEqual(hasGlobal, bodies.Count > 0 && bodies[0].IsGlobal);

        foreach (var body in bodies.Where(body => !body.IsGlobal))
        {
            var mesh = Gw2WaterBodies.Surface(body, ground.HeightAt, terrain.WorldUnitsPerHeightSample, 0f);
            Assert.IsTrue(mesh.Indices.Length > 0 && mesh.Indices.Length % 3 == 0, $"body {body.Index}");
            Assert.AreEqual(Area(body.Polygon.ToArray()), MeshArea(mesh), Area(body.Polygon.ToArray()) * 0.01f + 1f, $"body {body.Index}");
            Assert.IsTrue(mesh.Vertices.All(vertex => vertex.Position.Z == body.SurfaceZ));
            if (!body.TessellatesShore) Assert.IsTrue(mesh.Vertices.All(vertex => vertex.Alpha == Gw2WaterBodies.OpaqueAlpha));
        }
    }

    [TestMethod]
    public void DeepwoodWaterTexturesKeepTheirPositionsIncludingTheEmptyEngineSlot()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var water = reader.ReadMap(DeepwoodMapFileId)!.Environment!.Water!;
        CollectionAssert.AreEqual(new uint?[] { null, 1794545, 187578, 187574, 187576 }, water.TextureFiles.Select(file => file?.FileId).ToArray());
    }

    [TestMethod]
    public void DeepwoodDuskWaterBlendsBumpAndColorsAndTakesTheHeavierEntryForTheRest()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var environment = reader.ReadMap(DeepwoodMapFileId)!.Environment!;
        var day = environment.Water!;
        var night = environment.NightWater!;
        Assert.AreSame(day, Gw2EnvironmentWaterBlend.DayNight(environment, 1f));
        Assert.AreSame(night, Gw2EnvironmentWaterBlend.DayNight(environment, 0f));

        var dusk = Gw2EnvironmentWaterBlend.DayNight(environment, 0.25f)!;
        Assert.AreEqual(day.BumpAmount * 0.25f + night.BumpAmount * 0.75f, dusk.BumpAmount, 1e-4f);
        Assert.AreEqual(day.SurfaceFresnel * 0.25f + night.SurfaceFresnel * 0.75f, dusk.SurfaceFresnel, 1e-5f);
        Assert.AreEqual((byte)MathF.Round(day.DeepColor[0] * 0.25f + night.DeepColor[0] * 0.75f), dusk.DeepColor[0], 1);
        Assert.AreEqual(night.DepthAttenuation, dusk.DepthAttenuation);
        Assert.AreEqual(night.PatternTile, dusk.PatternTile);
        Assert.AreSame(night.TextureFiles, dusk.TextureFiles);
    }


    [TestMethod]
    public void DuskWaterTakesTheNightMaterialAndTexturesEvenWhenDayIsHeavier()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var environment = reader.ReadMap(BeachMapFileBaseId)!.Environment!;
        var day = environment.Water!;
        var night = environment.NightWater!;
        var dusk = Gw2EnvironmentWaterBlend.DayNight(environment, 0.75f)!;
        Assert.AreSame(night.TextureFiles, dusk.TextureFiles);
        Assert.AreEqual(night.MaterialFile, dusk.MaterialFile);
        Assert.AreEqual(day.PatternTile, dusk.PatternTile);
        Assert.AreEqual(68, dusk.PatternColor[3]);
    }
    [TestMethod]
    public void TerrainCutFollowsTheChunkFlagsAndTheDeepCutoff()
    {
        const float surfaceZ = 0f;
        const float shoreDepth = 25f;
        var cut = new Gw2WaterTerrainCut(true, Gw2WaterTerrainCut.DeepCutoffOf(0.1f, new Vector2(3f, 4f)), _ => 0);
        Assert.AreEqual(500f, cut.DeepCutoff);
        Assert.AreEqual(Gw2WaterTerrainCut.MinimumDeepCutoff, Gw2WaterTerrainCut.DeepCutoffOf(0f, Vector2.Zero));
        Assert.AreEqual(shoreDepth, cut.Depth(shoreDepth, surfaceZ, 0));
        Assert.AreEqual(Gw2WaterTerrainCut.OpaqueDepth, cut.Depth(shoreDepth, surfaceZ, Gw2WaterTerrainCut.ChunkOpaqueWaterFlag));
        Assert.AreEqual(0f, cut.Depth(shoreDepth, surfaceZ, Gw2WaterTerrainCut.ChunkOpaqueWaterFlag | Gw2WaterTerrainCut.ChunkClearWaterFlag));
        Assert.AreEqual(Gw2WaterTerrainCut.OpaqueDepth, cut.Depth(-600f, surfaceZ, 0));
        Assert.AreEqual(-600f, cut.Depth(-600f, surfaceZ, Gw2WaterTerrainCut.ChunkClearWaterFlag));
        Assert.AreEqual(shoreDepth, (cut with { CutsOpaqueWater = false }).Depth(shoreDepth, surfaceZ, Gw2WaterTerrainCut.ChunkOpaqueWaterFlag));
    }

    [TestMethod]
    public void GlobalWaterCellsKeepOnlyCellsWhereGroundDropsBelowTheSurfaceAndEverythingOutsideTheMap()
    {
        const float cell = Gw2GlobalWaterCells.CellSize;
        static float GroundBelowSurfaceOnlyInTheFirstColumn(Vector2 position) => position.X < cell ? 10f : -10f;
        var cells = Gw2GlobalWaterCells.From(Vector2.Zero, new Vector2(4f * cell), 0f, 96f, GroundBelowSurfaceOnlyInTheFirstColumn, null);
        Assert.AreEqual(16, cells.CellCount);
        Assert.AreEqual(4, cells.ShownCellCount);

        var insideReach = cells.ShownBoxes(new Vector3(3.5f * cell, 2f * cell, -100f), cell * 0.4f).ToArray();
        Assert.AreEqual(0, insideReach.Length);
        var nearEdge = cells.ShownBoxes(new Vector3(3.5f * cell, 2f * cell, -100f), cell * 0.75f).ToArray();
        Assert.AreEqual(1, nearEdge.Length);
        Assert.AreEqual(4f * cell, nearEdge[0].Minimum.X);
    }

    [TestMethod]
    public void ClearWaterChunksHideTheGlobalWaterEvenWhereGroundIsBelowTheSurface()
    {
        var cut = new Gw2WaterTerrainCut(true, Gw2WaterTerrainCut.MinimumDeepCutoff, _ => Gw2WaterTerrainCut.ChunkOpaqueWaterFlag | Gw2WaterTerrainCut.ChunkClearWaterFlag);
        Assert.IsFalse(Gw2GlobalWaterCells.IsWaterShown(Vector2.Zero, 0f, _ => 50f, cut));
        Assert.IsTrue(Gw2GlobalWaterCells.IsWaterShown(Vector2.Zero, 0f, _ => 50f, null));
        Assert.IsFalse(Gw2GlobalWaterCells.IsWaterShown(Vector2.Zero, 0f, _ => -50f, null));
    }

    [TestMethod]
    public void DeepwoodGlobalWaterIsShownInSomeCellsButNotAll()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var map = reader.ReadMap(DeepwoodMapFileId)!;
        var terrain = map.Terrain!;
        var mapMinimum = new Vector2(map.Info.Bounds[0], map.Info.Bounds[1]);
        var ground = Gw2TerrainGround.From(terrain, mapMinimum)!;
        var cells = Gw2GlobalWaterCells.From(mapMinimum, new Vector2(map.Info.Bounds[2], map.Info.Bounds[3]), map.Water!.PlaneZ, terrain.WorldUnitsPerHeightSample, ground.HeightAt, null);
        Assert.IsTrue(cells.ShownCellCount > 0 && cells.ShownCellCount < cells.CellCount, $"{cells.ShownCellCount} of {cells.CellCount}");
    }

    [TestMethod]
    public void ShoreAlphaGrowsAsTheGroundDropsBelowTheSurface()
    {
        Assert.AreEqual(0, Gw2WaterBodies.ShoreAlpha(-50f, 0f, 1f / 100f));
        Assert.AreEqual(128, Gw2WaterBodies.ShoreAlpha(50f, 0f, 1f / 100f));
        Assert.AreEqual(255, Gw2WaterBodies.ShoreAlpha(500f, 0f, 1f / 100f));
    }

    [TestMethod]
    public void GlobalPlaneVisibilityBoxFollowsTheEye()
    {
        var body = new Gw2WaterBody(0, true, false, 10f, new Vector2(-float.MaxValue), new Vector2(float.MaxValue), []);
        var (minimum, maximum) = body.VisibilityBox(new Vector3(100f, 200f, -50f), 1000f);
        Assert.AreEqual(new Vector3(-900f, -800f, 9f), minimum);
        Assert.AreEqual(new Vector3(1100f, 1200f, 11f), maximum);
        Assert.IsFalse(body.IsBelowWater(new Vector3(0f, 0f, 5f)));
        Assert.IsTrue(body.IsBelowWater(new Vector3(0f, 0f, 10f)));
    }

    [TestMethod]
    public void ConcavePolygonClipsToGridWithoutLosingArea()
    {
        Vector2[] polygon = [new(0, 0), new(300, 0), new(300, 300), new(150, 100), new(0, 300)];
        var body = new Gw2WaterBody(0, false, true, 0f, Vector2.Zero, new Vector2(300f), polygon);
        var mesh = Gw2WaterBodies.Surface(body, _ => 100f, 96f, 100f);
        Assert.AreEqual(Area(polygon), MeshArea(mesh), 1f);
        Assert.IsTrue(mesh.Vertices.All(vertex => vertex.Alpha == 255));
    }

    private static float MeshArea(Gw2WaterMesh mesh)
    {
        var area = 0f;
        for (var index = 0; index < mesh.Indices.Length; index += 3)
        {
            var a = mesh.Vertices[mesh.Indices[index]].Position;
            var b = mesh.Vertices[mesh.Indices[index + 1]].Position;
            var c = mesh.Vertices[mesh.Indices[index + 2]].Position;
            area += MathF.Abs((b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X)) / 2f;
        }
        return area;
    }

    [TestMethod]
    public void GlobalAlphaFollowsTerrainInsideTheInnerGridAndIsOpaqueOnTheRing()
    {
        static float GroundBelowSurfaceByWorldX(Vector2 position) => position.X;
        const float innerHalfExtent = 3072f;
        const float inverseShoreDepth = 1f / 50f;
        var shore = Gw2WaterBodies.GlobalAlpha(new Vector3(0f, 0f, 0f), new Vector3(25f, 0f, 0f), innerHalfExtent, GroundBelowSurfaceByWorldX, inverseShoreDepth);
        var aboveSurface = Gw2WaterBodies.GlobalAlpha(new Vector3(0f, 0f, 0f), new Vector3(-10f, 0f, 0f), innerHalfExtent, GroundBelowSurfaceByWorldX, inverseShoreDepth);
        var ring = Gw2WaterBodies.GlobalAlpha(new Vector3(innerHalfExtent * 2f, 0f, 0f), new Vector3(-10f, 0f, 0f), innerHalfExtent, GroundBelowSurfaceByWorldX, inverseShoreDepth);
        Assert.AreEqual((byte)128, shore);
        Assert.AreEqual((byte)0, aboveSurface);
        Assert.AreEqual(Gw2WaterBodies.OpaqueAlpha, ring);
    }

    private static float Area(Vector2[] polygon)
    {
        var area = 0f;
        for (var index = 0; index < polygon.Length; index++)
        {
            var next = polygon[(index + 1) % polygon.Length];
            area += polygon[index].X * next.Y - polygon[index].Y * next.X;
        }
        return MathF.Abs(area) / 2f;
    }
}
