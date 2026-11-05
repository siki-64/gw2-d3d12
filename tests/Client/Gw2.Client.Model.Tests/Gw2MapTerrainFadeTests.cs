using Gw2.Formats;
using System;
using System.IO;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2MapTerrainFadeTests
{
    private const uint TerrainV14FileId = 187611;
    private const uint TerrainV15FileId = 192711;
    private const uint DenseTerrainV15FileId = 3718508;

    [TestMethod]
    public void ReadsChunkVertexCountAndFadesWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var older = Gw2MapTerrain.TryParse(reader.ReadDocument(TerrainV14FileId))!;
        Assert.AreEqual(32u, older.VerticesPerChunkSide);
        Assert.AreEqual(2, older.MidFade.Count);
        Assert.AreEqual(2, older.FarFade.Count);

        Assert.AreEqual(32u, Gw2MapTerrain.TryParse(reader.ReadDocument(TerrainV15FileId))!.VerticesPerChunkSide);
        Assert.AreEqual(64u, Gw2MapTerrain.TryParse(reader.ReadDocument(DenseTerrainV15FileId))!.VerticesPerChunkSide);
    }
}
