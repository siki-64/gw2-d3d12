using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2MapPropsTests
{
    private const uint PropsV21FileId = 187611;

    [TestMethod]
    public void PlacesAnimatedPropsAfterStaticAndInstancedPropsWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var props = Gw2MapProps.TryParse(reader.ReadDocument(PropsV21FileId))!;

        var animated = props.Placements.Where(placement => placement.AnimSequence is not null).ToArray();
        Assert.AreEqual(141, animated.Length);
        Assert.IsTrue(props.Placements.TakeLast(141).All(placement => placement.AnimSequence is not null));
        Assert.IsTrue(animated.All(placement => reader.Contains(placement.ModelFileId)));
    }

    [TestMethod]
    public void ReadsBoundsBroadPhaseAndBlitSlotsWhenConfigured()
    {
        const ulong FirstInstanceGuid = 1498411076987919936;
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var props = Gw2MapProps.TryParse(reader.ReadDocument(PropsV21FileId))!;

        Assert.AreEqual(6285u, props.NextBroadId);
        Assert.AreEqual(146244, props.BroadPhaseData.Length);

        var first = props.Placements[0];
        Assert.AreEqual(9016.7f, first.Bounds.X, 1e-2f);
        Assert.AreEqual(-1876.7075f, first.Bounds.W, 1e-2f);
        Assert.AreEqual((ushort)1, first.BroadId);
        Assert.AreEqual(ushort.MaxValue, first.BucketId);
        Assert.AreEqual(byte.MaxValue, first.UnnamedByte);
        Assert.AreEqual(7, first.BlitTextureFileIds.Count);

        var instance = props.Placements.First(placement => placement.Guid == FirstInstanceGuid);
        Assert.AreEqual((ushort)2747, instance.BroadId);
        Assert.AreEqual((ushort)1564, instance.BucketId);
        Assert.IsTrue(props.Placements.All(placement => placement.BroadId < props.NextBroadId));
    }

    [TestMethod]
    public void PairsBlitSlotsWithModelTextureBlitIdsWhenConfigured()
    {
        const uint CampMapFileId = 3776288;
        const uint MossRockModelFileId = 3042294;
        const ulong RockTopBlitId = 0x2D02D83E50;
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var rocks = reader.ReadMap(CampMapFileId)!.Props!.Placements.Where(placement => placement.ModelFileId == MossRockModelFileId).ToArray();
        var modelBlitIds = reader.ReadModel(MossRockModelFileId)!.Materials.SelectMany(material => material.Textures).Select(texture => texture.BlitId).ToHashSet();

        Assert.IsTrue(modelBlitIds.Contains(RockTopBlitId));
        var rockTops = rocks.SelectMany(rock => rock.BlitTextures).Where(blit => blit.BlitId == RockTopBlitId).Select(blit => blit.TextureFileId).ToHashSet();
        CollectionAssert.IsSubsetOf(new[] { 3378818u, 3414834u, 224492u }, rockTops.ToArray());
    }
}
