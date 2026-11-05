using Gw2.Formats;
using System;
using System.IO;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2SortBucketTests
{
    private const uint TerrainMaterialFileId = 184988;
    private const uint TwoPassMaterialFileId = 16068;

    [TestMethod]
    public void SortOrderMapsThroughTheExeTable()
    {
        Assert.AreEqual(1, Gw2SortBuckets.Of(0));
        Assert.AreEqual(13, Gw2SortBuckets.Of(8));
        Assert.AreEqual(15, Gw2SortBuckets.Of(10));
        Assert.AreEqual(Gw2SortBuckets.Default, Gw2SortBuckets.Of(16));
    }

    [TestMethod]
    public void BucketsSortLikeTheExe()
    {
        Assert.AreEqual(Gw2SortType.FrontToBack, Gw2SortBuckets.TypeOf(1));
        Assert.AreEqual(Gw2SortType.ByStateThenFrontToBack, Gw2SortBuckets.TypeOf(5));
        Assert.AreEqual(Gw2SortType.BackToFront, Gw2SortBuckets.TypeOf(6));
        Assert.AreEqual(Gw2SortType.Unsorted, Gw2SortBuckets.TypeOf(11));
        Assert.AreEqual(Gw2SortType.BackToFront, Gw2SortBuckets.TypeOf(13));
    }

    [TestMethod]
    public void SortOrderTenIsTheDistortionBucket()
    {
        Assert.AreEqual(Gw2SortBuckets.Distortion, Gw2SortBuckets.Of(10));
        Assert.AreEqual(Gw2SortType.BackToFront, Gw2SortBuckets.TypeOf(Gw2SortBuckets.Distortion));
    }

    [TestMethod]
    public void MaterialsCarryTheirGrSortOrder()
    {
        Assert.AreEqual(1, ReadMaterial(TerrainMaterialFileId).SortBucket);
        Assert.AreEqual(11, ReadMaterial(TwoPassMaterialFileId).SortBucket);
    }

    private static Gw2AmatMaterial ReadMaterial(uint fileId)
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        return reader.ReadMaterial(fileId)!;
    }
}
