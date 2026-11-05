using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2EnvironmentWaterZoneBlendTests
{
    private const uint BeachMapFileId = 3719751;
    private static readonly Vector3 InsideFirstZone = new(-24576.172f, 31649.586f, -703.13586f);
    private static readonly Vector3 OpenSeaAtTheBoat = new(26643.3f, 6508.1f, -105.3f);

    [TestMethod]
    public void BeachWaterTakesTheZoneWaterInsideAZoneAndTheMapWaterOutside()
    {
        var datPath = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(datPath) || !File.Exists(datPath)) Assert.Inconclusive("Gw2.dat is not installed.");
        using var reader = Gw2ContentReader.Open(datPath, verifyChecksums: false);
        var environment = reader.ReadMap(BeachMapFileId)!.Environment!;

        var global = Gw2EnvironmentWaterBlend.DayNight(environment, 1f)!;
        var zoneWeights = Gw2EnvironmentZoneWeights.At(environment, InsideFirstZone);
        var inZone = Gw2EnvironmentWaterBlend.FromZones(environment, zoneWeights, 1f)!;
        var atBoat = Gw2EnvironmentWaterBlend.FromZones(environment, Gw2EnvironmentZoneWeights.At(environment, OpenSeaAtTheBoat), 1f)!;

        Assert.AreEqual(1f, zoneWeights.Single().Weight);
        Assert.AreEqual((200f, 2f, 809.10834f), (global.BumpAmount, global.SurfaceFresnel, global.DepthAttenuation));
        Assert.AreEqual((300f, 1f, 50f), (inZone.BumpAmount, inZone.SurfaceFresnel, inZone.DepthAttenuation));
        Assert.AreSame(global, atBoat);
    }
}
