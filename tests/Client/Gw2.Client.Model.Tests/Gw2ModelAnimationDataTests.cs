using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2ModelAnimationDataTests
{
    private const uint HundredBladesHitModelFileId = 0x355B4;
    private const uint EffectWindModelFileId = 88293;
    private const uint ClothModelFileId = 2306945;
    private const ulong HitAnimationToken = 676136;
    private const ulong ZeroPoseAnimationToken = 192720390330;
    private const ulong HitLightBoneToken = 1882761268739642945;
    private const uint NoTrack = uint.MaxValue;

    [TestMethod]
    public void ReadsLightAnimationTracksWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var hit = reader.ReadModelAnimations(HundredBladesHitModelFileId)!.Animations.Single(animation => animation.Token == HitAnimationToken);

        var light = hit.LightAnimations.Single();
        Assert.AreEqual(HitLightBoneToken, light.Bone);
        CollectionAssert.AreEqual(new byte[] { 0, 1, 2, 4 }, light.Tracks.Select(track => track.Type).ToArray());
        Assert.AreEqual(1u, light.Tracks[0].TrackGroupIndex);
        Assert.AreEqual(0u, light.Tracks[0].VectorTrackIndex);
        Assert.AreEqual(0.753f, light.Tracks[0].InitialValue.X, 1e-5f);
        Assert.AreEqual(NoTrack, light.Tracks[2].TrackGroupIndex);
        Assert.AreEqual(3u, light.Tracks[3].VectorTrackIndex);
    }

    [TestMethod]
    public void ReadsUvCloudMaterialConstantAndBoneConstraintAnimationsWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var bank = reader.ReadModelAnimations(EffectWindModelFileId)!;
        var zeroPose = bank.Animations.Single(animation => animation.Token == ZeroPoseAnimationToken);

        Assert.AreEqual(1, zeroPose.UvAnimations.Count);
        Assert.AreEqual(440, zeroPose.CloudAnimations.Count);
        Assert.AreEqual(7, zeroPose.MaterialConstantAnimations.Count);
        Assert.AreEqual(3, zeroPose.LightAnimations.Count);
        Assert.AreEqual(56, zeroPose.BoneConstraintAnimations.Count);
        Assert.AreEqual(0.5f, zeroPose.LightAnimations[1].Tracks.Single().InitialValue.X, 1e-6f);
        Assert.AreEqual(4, bank.Animations.Count(animation => animation.WindAnimations.Count > 0));
    }

    [TestMethod]
    public void ReadsAnimationPropertiesWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var bank = reader.ReadModelAnimations(ClothModelFileId)!;

        Assert.AreEqual(3, bank.Animations.Count(animation => animation.Properties.Count == 1));
    }
}
