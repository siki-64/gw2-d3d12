using Gw2.Formats;
using System;
using System.Collections.Generic;
using System.IO;
using Gw2.Dat.Content;
using Gw2.Dat.Archive;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Graphics.Tests;

[TestClass]
public sealed class GrTexResidencyTests
{
    private const uint ShipwreckStrandPropTexture = 831351;
    private const uint ShipwreckStrandPropHighResolutionTexture = 831352;

    [TestMethod]
    public void PromoteDistanceScalesWithTheSettingAndTheBackBufferArea()
    {
        Assert.AreEqual(1260f, GrTexResidency.PromoteDistance(GrTextureQuality.High, 1920, 1080), 0.5f);
        Assert.AreEqual(2520f, GrTexResidency.PromoteDistance(GrTextureQuality.Medium, 1920, 1080), 1f);
        Assert.AreEqual(5040f, GrTexResidency.PromoteDistance(GrTextureQuality.Low, 1920, 1080), 2f);
        Assert.AreEqual(540f, GrTexResidency.PromoteDistance(GrTextureQuality.High, 640, 480), 1e-3f);
        Assert.AreEqual(4320f, GrTexResidency.PromoteDistance(GrTextureQuality.High, 7680, 4320), 1e-3f);
    }

    [TestMethod]
    public void BaseFilesDropOneTopMipPerLevelAndOneMoreWithoutAHighResolutionFile()
    {
        Assert.AreEqual(0, GrTexResidency.BaseTopMipsDropped(GrTextureQuality.High, hasHighResolution: true));
        Assert.AreEqual(1, GrTexResidency.BaseTopMipsDropped(GrTextureQuality.High, hasHighResolution: false));
        Assert.AreEqual(2, GrTexResidency.BaseTopMipsDropped(GrTextureQuality.Low, hasHighResolution: true));
        Assert.AreEqual(1, GrTexResidency.HighResolutionTopMipsDropped(GrTextureQuality.Medium));
    }

    [TestMethod]
    public void DroppedMipsKeepBothDimensionsAtLeastFour()
    {
        Assert.AreEqual(2, GrTextureUpload.DroppableTopMips(512, 256, 10, 2));
        Assert.AreEqual(1, GrTextureUpload.DroppableTopMips(64, 8, 7, 3));
        Assert.AreEqual(0, GrTextureUpload.DroppableTopMips(4, 4, 3, 1));
        Assert.AreEqual(0, GrTextureUpload.DroppableTopMips(512, 512, 1, 1));
    }

    [TestMethod]
    public void TexturePromotesInsideTheDistanceAndDemotesPastTheHysteresis()
    {
        var residency = new GrTexResidency(GrTextureQuality.High);
        residency.Track(7);
        var promote = new List<uint>();
        var demote = new List<uint>();

        Round(residency, 7, 1000f);
        residency.Evaluate(1000f, promote, demote);
        CollectionAssert.AreEqual(new uint[] { 7 }, promote);

        promote.Clear();
        residency.Evaluate(1000f, promote, demote);
        Assert.AreEqual(0, promote.Count);
        residency.CompletePromotion(7, promoted: true);
        Assert.IsTrue(residency.IsHighResolution(7));

        Round(residency, 7, 1200f);
        residency.Evaluate(1000f, promote, demote);
        Assert.AreEqual(0, demote.Count);

        Round(residency, 7, 1200.5f);
        residency.Evaluate(1000f, promote, demote);
        CollectionAssert.AreEqual(new uint[] { 7 }, demote);
        Assert.IsFalse(residency.IsHighResolution(7));
        Assert.AreEqual(0, residency.HighResolutionCount);
    }

    [TestMethod]
    public void DistanceIsTheNearestReportOfTheRoundAndPersistsWithoutReports()
    {
        var residency = new GrTexResidency(GrTextureQuality.High);
        residency.Track(3);
        Assert.AreEqual(float.PositiveInfinity, residency.DistanceOf(3));

        residency.BeginRound();
        residency.Report(3, 900f);
        residency.Report(3, 400f);
        residency.Report(3, 700f);
        Assert.AreEqual(400f, residency.DistanceOf(3));

        residency.BeginRound();
        Assert.AreEqual(400f, residency.DistanceOf(3));
        residency.Report(3, 2000f);
        Assert.AreEqual(2000f, residency.DistanceOf(3));
    }

    [TestMethod]
    public void FailedPromotionIsNotRetried()
    {
        var residency = new GrTexResidency(GrTextureQuality.High);
        residency.Track(5);
        var promote = new List<uint>();
        var demote = new List<uint>();
        Round(residency, 5, 0f);
        residency.Evaluate(100f, promote, demote);
        residency.CompletePromotion(5, promoted: false);
        promote.Clear();
        residency.Evaluate(100f, promote, demote);
        Assert.AreEqual(0, promote.Count);
    }

    [TestMethod]
    public void PropTextureChainEndsAtFourByFourAndIsPaddedToOneByOne()
    {
        var (baseAsset, _) = ReadPropTexturePair();
        Assert.AreEqual(8, baseAsset.Texture!.Mips.Count);

        var resident = GrTextureUpload.Prepare(baseAsset)!;

        Assert.AreEqual((512, 512, 10), (resident.Width, resident.Height, resident.Mips.Count));
        Assert.AreEqual(resident.Mips[7].Count, resident.Mips[9].Count);
        CollectionAssert.AreEqual(resident.Mips[7].ToArray(), resident.Mips[8].ToArray());
    }

    [TestMethod]
    public void PromotedTextureIsTwiceTheResidentSizeAtEverySetting()
    {
        var (baseAsset, highAsset) = ReadPropTexturePair();
        Assert.AreEqual((1024, 1024, 1), (highAsset.Texture!.Width, highAsset.Texture.Height, highAsset.Texture.Mips.Count));

        foreach (var quality in new[] { GrTextureQuality.High, GrTextureQuality.Medium, GrTextureQuality.Low })
        {
            var resident = GrTextureUpload.Prepare(baseAsset, GrTexResidency.BaseTopMipsDropped(quality, hasHighResolution: true))!;
            var promoted = GrTextureUpload.PrepareUpResolved(baseAsset, highAsset, GrTexResidency.HighResolutionTopMipsDropped(quality))!;
            Assert.AreEqual((512 >> (int)quality, 512 >> (int)quality), (resident.Width, resident.Height));
            Assert.AreEqual((resident.Width * 2, resident.Height * 2, resident.Mips.Count + 1), (promoted.Width, promoted.Height, promoted.Mips.Count));
            CollectionAssert.AreEqual(resident.Mips[0].ToArray(), promoted.Mips[1].ToArray());
        }
    }

    private static (Gw2TextureAsset Base, Gw2TextureAsset HighResolution) ReadPropTexturePair()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        return (reader.ReadTexture(ShipwreckStrandPropTexture)!, reader.ReadTexture(ShipwreckStrandPropHighResolutionTexture)!);
    }

    private static void Round(GrTexResidency residency, uint fileId, float distance)
    {
        residency.BeginRound();
        residency.Report(fileId, distance);
    }
}
