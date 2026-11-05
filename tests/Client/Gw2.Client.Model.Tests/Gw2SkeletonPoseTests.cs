using Gw2.Formats;
using System;
using System.IO;
using System.Numerics;
using Gw2.Client.Model;
using Gw2.Dat.Content;
using Gw2.Dat.Archive;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2SkeletonPoseTests
{
    private const uint HundredBladesHitModelFileId = 0x355B4;

    [TestMethod]
    public void RestPoseMatchesTheBindPoseWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var skeleton = reader.ReadModelSkeleton(HundredBladesHitModelFileId)!;
        var worlds = Gw2SkeletonPose.WorldMatricesAt(skeleton, null, 0f);
        for (var index = 0; index < skeleton.Bones.Count; index++)
        {
            Assert.IsTrue(Matrix4x4.Invert(skeleton.Bones[index].InverseWorld, out var bind));
            Assert.AreEqual(bind.Translation.X, worlds[index].Translation.X, 0.05f, skeleton.Bones[index].Name);
            Assert.AreEqual(bind.Translation.Y, worlds[index].Translation.Y, 0.05f, skeleton.Bones[index].Name);
            Assert.AreEqual(bind.Translation.Z, worlds[index].Translation.Z, 0.05f, skeleton.Bones[index].Name);
        }
    }
}
