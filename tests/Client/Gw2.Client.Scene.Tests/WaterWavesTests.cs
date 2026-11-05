using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Scene.Tests;

[TestClass]
public sealed class WaterWavesTests
{
    private const uint DeepwoodMapFileId = 3776288;
    private const int Size = Gw2WaterWaves.TerrainScaleOneGridSize;

    [TestMethod]
    public void DeepwoodWavesMatchTheInverseTransformOfTheEvolvedSpectrum()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var water = reader.ReadMap(DeepwoodMapFileId)!.Environment!.Water!;
        Assert.IsTrue(water.AnimAmplitude > 0f && water.AnimWind != Vector2.Zero);

        var waves = new Gw2WaterWaves(Size);
        const float time = 3.25f;
        waves.Advance(time, water.AnimAmplitude, water.AnimWind, water.AnimChoppiness, 0f);

        Assert.IsTrue(waves.Height.Any(height => MathF.Abs(height) > 1e-3f));
        Assert.AreEqual(0f, waves.Height.Average(), 1e-3f);
        foreach (var (row, column) in new[] { (0, 0), (5, 17), (31, 32), (63, 1) })
        {
            var (height, displacementX, displacementY) = NaiveField(waves.Spectrum.ToArray(), time, row, column);
            var tolerance = 1e-3f * waves.Height.Max(MathF.Abs);
            Assert.AreEqual(height, waves.Height[row * Size + column], tolerance, $"h ({row},{column})");
            var choppiness = -Math.Clamp(water.AnimChoppiness, 0f, 1f);
            if (water.AnimChoppiness <= 0f) choppiness = 0f;
            Assert.AreEqual(displacementX * choppiness, waves.DisplacementX[row * Size + column], tolerance, $"dx ({row},{column})");
            Assert.AreEqual(displacementY * choppiness, waves.DisplacementY[row * Size + column], tolerance, $"dy ({row},{column})");
        }
    }

    [TestMethod]
    public void SpectrumIsSeededLikeTheExe()
    {
        var first = new Gw2WaterWaves(Size);
        var second = new Gw2WaterWaves(Size);
        first.Advance(0f, 1f, new Vector2(30f, 10f), 0f, 0f);
        second.Advance(0f, 1f, new Vector2(30f, 10f), 0f, 0f);
        CollectionAssert.AreEqual(first.Spectrum.ToArray(), second.Spectrum.ToArray());
        Assert.AreEqual(Vector2.Zero, first.Spectrum[Size / 2 * Size + Size / 2]);
    }

    [TestMethod]
    public void TaperIsZeroAtTheOuterTwoCellsAndNarrowsWithHeight()
    {
        var waves = new Gw2WaterWaves(Size);
        waves.Advance(0f, 0f, Vector2.Zero, 0f, 0f);
        Assert.AreEqual(0f, waves.Taper[0]);
        Assert.AreEqual(0f, waves.Taper[1]);
        Assert.AreEqual(0f, waves.Taper[Size - 1]);
        Assert.AreEqual(0f, waves.Taper[Size - 2]);
        var near = waves.Taper[4];
        waves.Advance(0f, 0f, Vector2.Zero, 0f, 2000f);
        Assert.IsTrue(waves.Taper[4] < near);
        Assert.AreEqual(MathF.Sin(3f / (Size - 1) * MathF.PI), waves.Taper[4], 1e-6f);
        Assert.AreEqual(255, waves.Alpha(0, 10, 0f));
    }

    [TestMethod]
    public void FieldOffsetRoundsHalfToEven()
    {
        Assert.AreEqual((Size << 10) + 2, Gw2WaterWaves.FieldOffset(2.5f * 48f, 48f, Size));
        Assert.AreEqual((Size << 10) + 4, Gw2WaterWaves.FieldOffset(3.5f * 48f, 48f, Size));
    }

    private static (float Height, float DisplacementX, float DisplacementY) NaiveField(Vector2[] spectrum, float time, int row, int column)
    {
        var step = 2f * MathF.PI / Size;
        var start = -(Size * 0.5f) * step;
        double height = 0, displacementX = 0, displacementY = 0;
        for (var i = 0; i < Size; i++)
        for (var j = 0; j < Size; j++)
        {
            var waveX = j * step + start;
            var waveY = i * step + start;
            var length = MathF.Sqrt(waveX * waveX + waveY * waveY);
            var phase = MathF.Sqrt(length * 9.81f) * time;
            var forward = spectrum[i * Size + j];
            var mirrored = spectrum[(Size - i) % Size * Size + (Size - j) % Size];
            var evolved = Multiply(forward, new Vector2(MathF.Cos(phase), MathF.Sin(phase)))
                + Multiply(new Vector2(mirrored.X, -mirrored.Y), new Vector2(MathF.Cos(phase), -MathF.Sin(phase)));
            var angle = 2.0 * Math.PI * (i * row + j * column) / Size;
            var basis = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
            var value = Multiply(evolved, basis);
            height += value.X;
            if (length == 0f) continue;
            displacementX += Multiply(new Vector2(0f, -waveX / length), value).X;
            displacementY += Multiply(new Vector2(0f, -waveY / length), value).X;
        }
        var sign = ((row + column) & 1) == 0 ? 1f : -1f;
        return ((float)height * sign, (float)displacementX * sign, (float)displacementY * sign);
    }

    private static Vector2 Multiply(Vector2 first, Vector2 second) =>
        new(first.X * second.X - first.Y * second.Y, first.X * second.Y + first.Y * second.X);
}

[TestClass]
public sealed class GlobalWaterGridTests
{
    private const int Size = Gw2WaterWaves.TerrainScaleOneGridSize;
    private const float SurfaceZ = 100f;
    private const float CellSize = 48f;

    [TestMethod]
    public void GridSizeDoublesOnSixtyFourTileChunks()
    {
        Assert.AreEqual(64, Gw2WaterWaves.GridSizeForTilesPerChunk(32));
        Assert.AreEqual(128, Gw2WaterWaves.GridSizeForTilesPerChunk(64));
        Assert.AreEqual(3072f, Gw2WaterBodies.GlobalInnerHalfExtent(128, 3072f / 64));
    }

    [TestMethod]
    public void DeepWaterRisesAndFallsInsideAndStaysFlatAtTheEdges()
    {
        var waves = new Gw2WaterWaves(Size);
        waves.Advance(2f, 0.5f, new Vector2(30f, 10f), 0f, 0f);
        var grid = new Gw2GlobalWaterGrid(waves, SurfaceZ, CellSize, 50000f, _ => 1f);
        var positions = new Vector3[grid.VertexCount];
        var normals = new Vector3[grid.VertexCount];
        var alphas = new byte[grid.VertexCount];
        grid.Fill(new Vector3(1000f, 2000f, 0f), new Vector2(1f, 0f), positions, normals, alphas);

        Assert.AreEqual(SurfaceZ, positions[0].Z);
        Assert.AreEqual(SurfaceZ, positions[Size * Size - 1].Z);
        Assert.IsTrue(positions.Take(Size * Size).Any(position => MathF.Abs(position.Z - SurfaceZ) > 1e-3f));
        Assert.IsTrue(alphas.All(alpha => alpha == 255));
        Assert.IsTrue(positions.Skip(Size * Size).All(position => position.Z == SurfaceZ));
        var center = positions[Size / 2 * Size + Size / 2];
        Assert.IsTrue(center.X > 1000f + grid.HalfExtent * 0.8f, "the grid leads the eye along the view");
        Assert.AreEqual(new Vector3(0f, 0f, -6f), normals[0]);
        Assert.AreEqual(new Vector3(0f, 0f, -3f), normals[Size * Size]);
        Assert.IsTrue(normals.Take(Size * Size).Any(normal => MathF.Abs(normal.X) > 1e-3f));
    }

    [TestMethod]
    public void LowQualityLerpsFromThePreviousRefreshToTheLatest()
    {
        var wind = new Vector2(30f, 10f);
        var highQuality = new Gw2WaterWaves(Size);
        var lowQuality = new Gw2WaterWaves(Size, lowQuality: true);
        highQuality.Advance(1f, 0.5f, wind, 1f, 0f);
        var first = highQuality.Height.ToArray();
        highQuality.Advance(0.25f, 0.5f, wind, 1f, 0f);
        var second = highQuality.Height.ToArray();

        lowQuality.Advance(1f, 0.5f, wind, 1f, 0f);
        Assert.IsTrue(lowQuality.Height.All(height => height == 0f), "the first refresh starts from a flat field");
        lowQuality.Advance(0.25f, 0.5f, wind, 1f, 0f);
        CollectionAssert.AreEqual(first, lowQuality.Height);
        lowQuality.Advance(0.05f, 0.5f, wind, 1f, 0f);
        var blend = 0.05f / Gw2WaterWaves.LowQualityRefreshSeconds;
        for (var index = 0; index < first.Length; index++)
            Assert.AreEqual(first[index] * (1f - blend) + second[index] * blend, lowQuality.Height[index], 1e-4f);
    }

    [TestMethod]
    public void ShallowWaterStaysFlatAndFadesOut()
    {
        var waves = new Gw2WaterWaves(Size);
        waves.Advance(2f, 0.5f, new Vector2(30f, 10f), 1f, 0f);
        var grid = new Gw2GlobalWaterGrid(waves, SurfaceZ, CellSize, 50000f, _ => 0f);
        var positions = new Vector3[grid.VertexCount];
        var normals = new Vector3[grid.VertexCount];
        var alphas = new byte[grid.VertexCount];
        grid.Fill(Vector3.Zero, Vector2.Zero, positions, normals, alphas);

        Assert.IsTrue(positions.Take(Size * Size).All(position => position.Z == SurfaceZ));
        Assert.AreEqual(0, alphas[Size / 2 * Size + Size / 2]);
        Assert.AreEqual(255, alphas[0]);
    }

    [TestMethod]
    public void VertexNormalSumsTheSixFaceNormalsAroundTheVertex()
    {
        const int planeSize = 4;
        var positions = new Vector3[planeSize * planeSize];
        for (var row = 0; row < planeSize; row++)
        for (var column = 0; column < planeSize; column++)
            positions[row * planeSize + column] = new Vector3(column * CellSize, row * CellSize, column * CellSize * 0.5f);

        var normal = Gw2GlobalWaterGrid.VertexNormal(positions, planeSize, 1, 1);

        var expected = 6f * Vector3.Normalize(new Vector3(0.5f, 0f, -1f));
        Assert.AreEqual(expected.X, normal.X, 1e-4f);
        Assert.AreEqual(expected.Y, normal.Y, 1e-4f);
        Assert.AreEqual(expected.Z, normal.Z, 1e-4f);
    }

    [TestMethod]
    public void GlobalPlaneSplitsEachCellAlongTheRisingDiagonalLikeTheExe()
    {
        var mesh = Gw2WaterBodies.GlobalPlane(Size, SurfaceZ, 1536f, 50000f);

        Assert.AreEqual(Size * (Size + 4) - 4, mesh.Vertices.Length);
        Assert.AreEqual(6 * (Size - 1) * (Size + 3), mesh.Indices.Length);
        CollectionAssert.AreEqual(new[] { 0, Size + 1, 1, 0, Size, Size + 1 }, mesh.Indices.Take(6).ToArray());
        var firstSkirt = 6 * (Size - 1) * (Size - 1);
        CollectionAssert.AreEqual(new[] { Size * Size, 0, Size * Size + 1, 0, 1, Size * Size + 1 }, mesh.Indices.Skip(firstSkirt).Take(6).ToArray());
    }
}
