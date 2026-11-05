using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2ModelAnimationExtrasTests
{
    private const uint StreakModelFileId = 1676414;
    private const uint LightningModelFileId = 454456;
    private const uint TokenMapModelFileId = 3800172;
    private const uint VariantModelFileId = 13375;
    private const uint TriggerModelFileId = 13829;
    private const uint MorphModelFileId = 24397;
    private const ulong ZeroPoseAnimationToken = 192720390330;
    private const ulong StreakAnimationToken = 21581545048718928;
    private const ulong TriggerAnimationToken = 426922279162101;
    private const uint NoTrack = uint.MaxValue;

    private static Gw2ContentReader OpenReader()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        return Gw2ContentReader.Open(dat, verifyChecksums: false);
    }

    [TestMethod]
    public void ReadsStreakAnimations()
    {
        using var reader = OpenReader();
        var animation = reader.ReadModelAnimations(StreakModelFileId)!.Animations.Single(candidate => candidate.Token == StreakAnimationToken);

        Assert.AreEqual(6, animation.StreakAnimations.Count);
        Assert.AreEqual(3458765073528572198ul, animation.StreakAnimations[0].Bone);
        Assert.AreEqual(2, animation.StreakAnimations[0].Anchors.Count);
        Assert.AreEqual(1152921509037346241ul, animation.StreakAnimations[0].Anchors[0].Bone);
    }

    [TestMethod]
    public void ReadsLightningAnimations()
    {
        using var reader = OpenReader();
        var animation = reader.ReadModelAnimations(LightningModelFileId)!.Animations.Single(candidate => candidate.Token == ZeroPoseAnimationToken);

        Assert.AreEqual(21, animation.LightningAnimations.Count);
        Assert.AreEqual(5783135585107063266ul, animation.LightningAnimations[0].Bone);
    }

    [TestMethod]
    public void ReadsTokenMapAnimations()
    {
        using var reader = OpenReader();
        var animation = reader.ReadModelAnimations(TokenMapModelFileId)!.Animations.Single(candidate => candidate.Token == ZeroPoseAnimationToken);

        Assert.AreEqual(2, animation.TokenMapAnimations.Count);
        Assert.AreEqual(new Gw2ModelTokenMapAnimation(693604481, NoTrack, NoTrack, Vector4.Zero), animation.TokenMapAnimations[0]);
    }

    [TestMethod]
    public void ReadsTriggerTimesMorphTrackGroupsAndVariantIndices()
    {
        using var reader = OpenReader();

        var triggered = reader.ReadModelAnimations(TriggerModelFileId)!.Animations.Single(candidate => candidate.Token == TriggerAnimationToken);
        CollectionAssert.AreEqual(new[] { 0.2f }, triggered.TriggerTimes.ToArray());

        var morphed = reader.ReadModelAnimations(MorphModelFileId)!.Animations.Single(candidate => candidate.Token == ZeroPoseAnimationToken);
        CollectionAssert.AreEqual(new ushort[] { 1 }, morphed.MorphTrackGroups.ToArray());

        var variants = reader.ReadModelAnimations(VariantModelFileId)!.Animations.Single(candidate => candidate.Token == ZeroPoseAnimationToken);
        CollectionAssert.AreEqual(new uint[] { 0, 0, 0 }, variants.VariantIndices.ToArray());
    }
}
