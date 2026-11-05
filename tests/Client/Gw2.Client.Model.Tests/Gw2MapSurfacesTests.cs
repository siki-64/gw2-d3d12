using Gw2.Formats;
using System;
using System.IO;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2MapSurfacesTests
{
    private const uint MapFileId = 187611;

    [TestMethod]
    public void ReadsPropAndTerrainSurfaceOverridesWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var surfaces = Gw2MapSurfaces.TryParse(reader.ReadDocument(MapFileId))!;

        Assert.AreEqual(8, surfaces.PropOverrides.Count);
        var prop = surfaces.PropOverrides[1];
        Assert.AreEqual(1162951396442571787ul, prop.PropId);
        Assert.AreEqual(13, prop.Overrides.Count);
        Assert.AreEqual(5225ul, prop.Overrides[0].SurfaceId);
        CollectionAssert.AreEqual(new byte[] { 64 }, prop.Overrides[0].TriangleMask);

        Assert.AreEqual(26, surfaces.TerrainOverrides.Count);
        var terrain = surfaces.TerrainOverrides[0];
        Assert.AreEqual(4u, terrain.ChunkX);
        Assert.AreEqual(17u, terrain.ChunkY);
        Assert.AreEqual(1152921504625905719ul, terrain.Overrides[0].SurfaceId);
        Assert.AreEqual(13, terrain.Overrides[0].TriangleMask.Length);
    }
}
