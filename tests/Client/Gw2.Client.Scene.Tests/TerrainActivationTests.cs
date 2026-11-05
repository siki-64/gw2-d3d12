using Gw2.Formats;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Scene.Tests;

[TestClass]
public sealed class TerrainActivationTests
{
    private const uint DeepwoodMapFileId = 3776288;

    [TestMethod]
    public void MovingTheViewOneChunkEastReleasesTheWestColumnAndActivatesTheEastColumn()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var map = reader.ReadMap(DeepwoodMapFileId)!;
        var terrain = map.Terrain!;
        var bounds = map.Info.Bounds;
        var activation = new Gw2TerrainActivation(terrain, bounds);
        var span = activation.Span;
        var columns = Gw2MapTerrainGeometry.ChunkColumns(terrain);
        var rows = Gw2MapTerrainGeometry.ChunkRows(terrain);
        var startColumn = Math.Min(span, columns - 1);
        var startRow = rows / 2;
        var view = new Vector3(bounds[0] + (startColumn + 0.5f) * Gw2MapTerrain.ChunkWorldSize, bounds[3] - (startRow + 0.5f) * Gw2MapTerrain.ChunkWorldSize, 0f);
        var released = new List<(int Column, int Row)>();
        var activated = new List<(int Column, int Row)>();

        Assert.IsTrue(activation.Update(view, released, activated));
        Assert.AreEqual(0, released.Count);
        Assert.AreEqual(activation.Active.Chunks().Count(), activated.Count);
        Assert.AreEqual(Math.Max(0, startColumn - span), activation.Active.MinColumn);
        Assert.AreEqual(Math.Min(columns, startColumn + span + 1), activation.Active.EndColumn);

        released.Clear();
        activated.Clear();
        Assert.IsFalse(activation.Update(view + new Vector3(100f, 0f, 0f), released, activated));

        var before = activation.Active;
        activation.Update(view + new Vector3(Gw2MapTerrain.ChunkWorldSize, 0f, 0f), released, activated);
        var after = activation.Active;
        CollectionAssert.AreEquivalent(before.Chunks().Where(chunk => !after.Contains(chunk.Column, chunk.Row)).ToList(), released);
        CollectionAssert.AreEquivalent(after.Chunks().Where(chunk => !before.Contains(chunk.Column, chunk.Row)).ToList(), activated);
        Assert.IsTrue(released.All(chunk => chunk.Column == before.MinColumn) || before.MinColumn == after.MinColumn);
        Assert.IsTrue(activated.All(chunk => chunk.Column == after.EndColumn - 1));
    }
}
