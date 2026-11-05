using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2ColorLookupTests
{
    private const uint ShipwreckStrandEnvironment = 3719751;
    private const uint ShipwreckStrandDayClut = 3718504;
    private const uint ShipwreckStrandNightClut = 1150829;

    [TestMethod]
    public void NightClutOnMap1595IsIdentityAsLive()
    {
        var reader = OpenReader();

        var night = Gw2ColorLookup.FromDds(reader.ReadBytes(ShipwreckStrandNightClut));

        Assert.IsNotNull(night);
        CollectionAssert.AreEqual(Gw2ColorLookup.Identity.Rgba, night.Rgba);
    }

    [TestMethod]
    public void DayClutOnMap1595CompressesRedHighlights()
    {
        var reader = OpenReader();

        var day = Gw2ColorLookup.FromDds(reader.ReadBytes(ShipwreckStrandDayClut));

        Assert.IsNotNull(day);
        Assert.AreEqual(new Gw2ColorRgb(0, 0, 0), day.Lookup(0, 0, 0));
        Assert.AreEqual(new Gw2ColorRgb(230, 0, 0), day.Lookup(15, 0, 0));
        Assert.AreEqual(new Gw2ColorRgb(230, 177, 83), day.Lookup(15, 10, 5));
    }

    [TestMethod]
    public void GlobalEffectWeightsSplitDayAndNightCluts()
    {
        var reader = OpenReader();
        var environment = reader.ReadMap(ShipwreckStrandEnvironment)!.Environment!;

        var weights = Gw2EnvironmentColorLookup.WeightsFromZones(environment, [], 0.25f);

        Assert.AreEqual(0.25f, weights.Single(weighted => weighted.ClutFileId == ShipwreckStrandDayClut).Weight, 1e-6f);
        Assert.AreEqual(0.75f, weights.Single(weighted => weighted.ClutFileId == ShipwreckStrandNightClut).Weight, 1e-6f);
    }

    [TestMethod]
    public void BlendWeighsEachLookupByItsShare()
    {
        var black = Gw2ColorLookup.Blend([(Gw2ColorLookup.Identity, 0f)]);
        var half = Gw2ColorLookup.Blend([(Gw2ColorLookup.Identity, 1f), (Gw2ColorLookup.Identity, 1f)]);

        Assert.AreSame(Gw2ColorLookup.Identity, black);
        Assert.AreEqual(new Gw2ColorRgb(255, 170, 85), half.Lookup(15, 10, 5));
    }

    private static Gw2ContentReader OpenReader()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        return Gw2ContentReader.Open(dat, verifyChecksums: false);
    }
}
