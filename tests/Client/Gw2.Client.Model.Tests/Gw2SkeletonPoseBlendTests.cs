using System.Numerics;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2SkeletonPoseBlendTests
{
    [TestMethod]
    public void BlendsTrackPositionsByWeightAndKeepsUncoveredBonesInBindPose()
    {
        var skeleton = new Gw2ModelSkeleton(default, [Bone("Root", new Vector3(1f, 2f, 3f)), Bone("Other", new Vector3(5f, 0f, 0f))], [], [], [], []);
        var first = Clip(new Vector3(0f, 0f, 0f));
        var second = Clip(new Vector3(10f, 0f, 0f));

        var worlds = Gw2SkeletonPose.WorldMatricesBlended(skeleton, [new(first, 0f, 0.75f), new(second, 0f, 0.25f)]);

        Assert.AreEqual(2.5f, worlds[0].Translation.X, 1e-4f);
        Assert.AreEqual(5f, worlds[1].Translation.X, 1e-4f);
    }

    [TestMethod]
    public void AddsAdditiveClipOffsetsOnTopOfTheBlendedPose()
    {
        var skeleton = new Gw2ModelSkeleton(default, [Bone("Root", Vector3.Zero)], [], [], [], []);
        var additive = new Gw2AnimationClip { IsAdditive = true, Tracks = Clip(new Vector3(0f, 4f, 0f)).Tracks };

        var worlds = Gw2SkeletonPose.WorldMatricesBlended(skeleton, [new(Clip(new Vector3(2f, 0f, 0f)), 0f, 1f), new(additive, 0f, 0.5f)]);

        Assert.AreEqual(2f, worlds[0].Translation.X, 1e-4f);
        Assert.AreEqual(2f, worlds[0].Translation.Y, 1e-4f);
    }

    [TestMethod]
    public void ScalesEachBonesWeightByTheTrackMask()
    {
        var skeleton = new Gw2ModelSkeleton(default, [Bone("Root", Vector3.Zero)], [], [], [], []);

        var worlds = Gw2SkeletonPose.WorldMatricesBlended(skeleton, [new(Clip(Vector3.Zero), 0f, 1f), new(Clip(new Vector3(10f, 0f, 0f)), 0f, 1f, [0.25f])]);

        Assert.AreEqual(2f, worlds[0].Translation.X, 1e-4f);
    }

    private static Gw2AnimationClip Clip(Vector3 position) => new()
    {
        Tracks = [new Gw2AnimationTrack(0, "Root", [new Gw2AnimationKey(0f, position, Quaternion.Identity, Vector3.One)])],
    };

    private static Gw2ModelBone Bone(string name, Vector3 bindPosition) =>
        new(name, uint.MaxValue, 0, bindPosition, Quaternion.Identity, Matrix4x4.Identity, Matrix4x4.CreateTranslation(-bindPosition), 0f);
}
