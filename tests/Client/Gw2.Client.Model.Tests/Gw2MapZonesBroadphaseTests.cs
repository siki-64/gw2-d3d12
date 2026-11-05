using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2MapZonesBroadphaseTests
{
    private const uint MapFileId = 187611;

    [TestMethod]
    public void ReadsZoneBroadphaseWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var zones = Gw2MapZones.TryParse(reader.ReadDocument(MapFileId))!;

        Assert.AreEqual(155, zones.Zones.Count);
        Assert.AreEqual(4996, zones.Broadphase!.Data.Length);
        Assert.AreEqual(zones.Zones.Max(zone => (uint)zone.BroadId) + 1, zones.Broadphase.MaxBroadId);
    }
}
