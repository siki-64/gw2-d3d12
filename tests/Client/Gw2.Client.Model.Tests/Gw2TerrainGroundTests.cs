using Gw2.Formats;
using System;
using System.IO;
using System.Numerics;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2TerrainGroundTests
{
    private const uint WayfarerFoothillsMapFileId = 187611;

    [TestMethod]
    public void NormalMatchesTheLiveTerrainQueryAtScatterPoints()
    {
        var datPath = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(datPath) || !File.Exists(datPath)) Assert.Inconclusive("Gw2.dat is not installed.");
        using var reader = Gw2ContentReader.Open(datPath, verifyChecksums: false);
        var map = reader.ReadMap(WayfarerFoothillsMapFileId)!;
        var ground = Gw2TerrainGround.From(map.Terrain!, new Vector2(map.Info.Bounds[0], map.Info.Bounds[1]))!;

        AssertNormal(new Vector3(0.2958f, -0.0209f, -0.9550f), ground.NormalAt(new Vector2(-9512f, -25392.27f)));
        AssertNormal(new Vector3(0.2891f, -0.0210f, -0.9571f), ground.NormalAt(new Vector2(-9500.03f, -25420.75f)));
        Assert.AreEqual(-1535f, ground.HeightAt(new Vector2(502.4f, -30543f)), 20f);
    }

    private static void AssertNormal(Vector3 live, Vector3 ours)
    {
        Assert.AreEqual(live.X, ours.X, 0.001f);
        Assert.AreEqual(live.Y, ours.Y, 0.001f);
        Assert.AreEqual(live.Z, ours.Z, 0.001f);
    }
}
