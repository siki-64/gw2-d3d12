using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2ModelSkeletonDataTests
{
    private const uint HundredBladesHitModelFileId = 0x355B4;
    private const uint BoneConstraintModelFileId = 2467602;
    private const uint OverrideConstraintModelFileId = 1725890;
    private const uint TrackMaskModelFileId = 13869;
    private const uint EmitterBoneModelFileId = 643092;

    [TestMethod]
    public void ReadsBoneConstraintSettingsWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var constraints = reader.ReadModelSkeleton(BoneConstraintModelFileId)!.BoneConstraints;

        Assert.AreEqual(185, constraints.Count);
        Assert.AreEqual(1152941915552006677ul, constraints[0].Token);
        Assert.AreEqual(1u, constraints[0].Flags);
        Assert.AreEqual(0.7f, constraints[0].AnimBlend, 1e-6f);
        Assert.AreEqual(0.005f, constraints[0].Drag, 1e-6f);
        Assert.AreEqual(0.005f, constraints[0].Gravity, 1e-6f);
    }

    [TestMethod]
    public void ReadsSkeletonNamesPlacementAndConstraintsWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var skeleton = reader.ReadModelSkeleton(HundredBladesHitModelFileId)!;

        Assert.AreEqual("4036391266", skeleton.SkeletonName);
        Assert.AreEqual(1u, skeleton.LodType);
        Assert.IsNotNull(skeleton.InitialPlacement);
        Assert.AreEqual(3, skeleton.MeshBindingCount);
        Assert.AreEqual(0f, skeleton.InitialPlacement!.Position.Length(), 1e-6f);
        Assert.AreEqual(1f, skeleton.InitialPlacement.Orientation.W, 1e-6f);
    }

    [TestMethod]
    public void ReadsOverrideConstraintsTrackMasksAndEmitterBonesWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);

        var overrides = reader.ReadModelSkeleton(OverrideConstraintModelFileId)!.OverrideBoneConstraints;
        Assert.AreEqual(61, overrides.Count);
        Assert.AreEqual(3458765216236444230ul, overrides[0].Token);
        Assert.AreEqual(67u, overrides[0].Flags);
        Assert.AreEqual(0.2f, overrides[0].AnimBlend, 1e-6f);

        var trackMasks = reader.ReadModelSkeleton(TrackMaskModelFileId)!.TrackMasks;
        Assert.AreEqual(9, trackMasks.Count);
        Assert.AreEqual(166950ul, trackMasks[0].Token);
        Assert.AreEqual(820, trackMasks[0].Data.Length);
        Assert.AreEqual(0f, trackMasks[0].DefaultWeight);
        Assert.AreEqual(153, trackMasks[0].BoneWeights.Count);
        var skeleton = reader.ReadModelSkeleton(TrackMaskModelFileId)!;
        var headIndex = skeleton.Bones.ToList().FindIndex(bone => bone.Name == "bone:Head");
        Assert.AreEqual(0.5f, trackMasks[0].WeightOf(headIndex));
        Assert.AreEqual(1f, trackMasks[0].WeightOf(skeleton.Bones.ToList().FindIndex(bone => bone.Name == "bone:Jaw")));
        Assert.AreEqual(0f, trackMasks[0].WeightOf(0));

        var emitterBones = reader.ReadModelSkeleton(EmitterBoneModelFileId)!.EmitterBones;
        Assert.AreEqual(46, emitterBones.Count);
        CollectionAssert.AreEqual(new uint[] { 851980, 917517, 983054 }, emitterBones.Take(3).ToArray());
    }
}
