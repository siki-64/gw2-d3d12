using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2BroadphaseTests
{
    private const uint MapFileId = 187611;

    [TestMethod]
    public void DecodesSortedEdgesIntoBoxes()
    {
        var data = new byte[Gw2Broadphase.SerializedSize(1)];
        BitConverter.TryWriteBytes(data.AsSpan(0), 1u);
        WriteEdge(data, 0, 7u, -1f);
        WriteEdge(data, 1, 7u | 0x80000000, 3f);
        WriteEdge(data, 2, uint.MaxValue, float.MaxValue);
        WriteEdge(data, 3, uint.MaxValue, float.MaxValue);
        WriteEdge(data, 4, 7u, 10f);
        WriteEdge(data, 5, 7u | 0x80000000, 20f);
        WriteEdge(data, 6, uint.MaxValue, float.MaxValue);
        WriteEdge(data, 7, uint.MaxValue, float.MaxValue);

        var broadphase = Gw2Broadphase.TryParse(data)!;

        Assert.AreEqual(new Gw2BroadphaseBox(7, -1, 10, 3, 20), broadphase.Boxes.Single());
        Assert.IsTrue(broadphase.XEdges[2].IsSentinel);
        Assert.IsNull(Gw2Broadphase.TryParse(data.AsSpan(0, data.Length - 1)));
    }

    [TestMethod]
    public void DecodesLightZoneAndPropBroadphasesWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var document = reader.ReadDocument(MapFileId);

        var lights = Gw2MapLights.TryParse(document)!;
        var lightBoxes = lights.Broadphase!.Decoded!.Boxes;
        Assert.AreEqual(669, lightBoxes.Count);
        Assert.IsTrue(lightBoxes.All(box => box.BroadId > 0 && box.BroadId < lights.Broadphase.MaxBroadId));
        Assert.IsTrue(lightBoxes.All(box => box.MinX <= box.MaxX && box.MinY <= box.MaxY));
        Assert.IsTrue(lights.PointLights.All(group => lightBoxes.Any(box => box.BroadId == group.BroadId)));

        var zones = Gw2MapZones.TryParse(document)!;
        var zoneBoxes = zones.Broadphase!.Decoded!.Boxes;
        Assert.AreEqual(zones.Zones.Count, zoneBoxes.Count);
        Assert.IsTrue(zones.Zones.All(zone => zoneBoxes.Any(box => box.BroadId == zone.BroadId)));

        var props = Gw2MapProps.TryParse(document)!;
        var propBoxes = props.BroadPhase!.Boxes;
        Assert.IsTrue(propBoxes.Count > 0);
        Assert.IsTrue(propBoxes.All(box => box.BroadId < props.NextBroadId));
        AssertSorted(props.BroadPhase.XEdges);
        AssertSorted(props.BroadPhase.YEdges);
    }

    private static void AssertSorted(System.Collections.Generic.IReadOnlyList<Gw2BroadphaseEdge> edges)
    {
        for (var index = 1; index < edges.Count; index++)
            Assert.IsTrue(edges[index - 1].Coordinate <= edges[index].Coordinate);
    }

    private static void WriteEdge(byte[] data, int index, uint packedId, float coordinate)
    {
        BitConverter.TryWriteBytes(data.AsSpan(4 + index * 8), packedId);
        BitConverter.TryWriteBytes(data.AsSpan(8 + index * 8), coordinate);
    }
}
