using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2ModelSkeletonGrannySlotTests
{
    [TestMethod]
    public void BoneExtendedDataAndMeshBindingSlotsReadWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var skeleton = Gw2ModelSkeleton.TryParse(reader.ReadDocument(42877))!;
        var infoBar = skeleton.Bones.Single(bone => bone.Name == "actionpoint:InfoBar");
        var bodyAim = skeleton.Bones.Single(bone => bone.Name == "actionpoint:BodyAim");
        var root = skeleton.Bones.Single(bone => bone.Name == "bone:root");

        Assert.AreEqual((byte)2, infoBar.ExtendedDataType);
        Assert.AreEqual((byte)76, infoBar.ExtendedDataObject);
        Assert.AreEqual((byte)84, bodyAim.ExtendedDataObject);
        Assert.AreEqual((byte)19, root.ExtendedDataType);
        Assert.AreEqual((byte)0, root.ExtendedDataObject);

        var bound = Gw2ModelSkeleton.TryParse(reader.ReadDocument(14211))!;
        Assert.AreEqual(bound.MeshBindingCount, bound.MeshBindingMeshes.Count);
        CollectionAssert.AreEqual(new byte[] { 236, 0, 0, 0 }, bound.MeshBindingMeshes.ToArray());
    }
}
