using System.Numerics;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2TwoBoneLegSolverTests
{
    [TestMethod]
    public void PlacesTheFootOnAReachableTargetKeepingBothBoneLengths()
    {
        var skeleton = new Gw2ModelSkeleton(default,
        [
            Bone("hip", 0xFFFFFFFF, Vector3.Zero),
            Bone("knee", 0, new Vector3(0.1f, 0f, 1f)),
            Bone("ankle", 1, new Vector3(-0.1f, 0f, 1f)),
        ], [], [], [], []);
        var pose = new Gw2LocalPose(
            [Vector3.Zero, new Vector3(0.1f, 0f, 1f), new Vector3(-0.1f, 0f, 1f)],
            [Quaternion.Identity, Quaternion.Identity, Quaternion.Identity],
            [Vector3.One, Vector3.One, Vector3.One]);
        var target = new Vector3(0.3f, 0.2f, 1.6f);

        Assert.IsTrue(Gw2TwoBoneLegSolver.Solve(skeleton, pose, new Gw2LegChainBones(2, 1, 0), target, 0f, 0f));

        var worlds = Gw2SkeletonPose.WorldMatricesOf(skeleton, pose);
        Assert.AreEqual(0f, Vector3.Distance(target, worlds[2].Translation), 1e-4f);
        Assert.AreEqual(new Vector3(0.1f, 0f, 1f).Length(), worlds[1].Translation.Length(), 1e-4f);
    }

    private static Gw2ModelBone Bone(string name, uint parent, Vector3 position) =>
        new(name, parent, 0, position, Quaternion.Identity, Matrix4x4.Identity, Matrix4x4.Identity, 0f);
}
