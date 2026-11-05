using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2BoneScaleFileTests
{
    private const uint BoneScaleFileId = 3628525;

    [TestMethod]
    public void ReadsBoneScaleRegionsWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var document = reader.ReadDocument(BoneScaleFileId);
        Assert.IsTrue(document.Chunks.All(chunk => chunk.UnsupportedReason is null));
        var scale = Gw2BoneScaleFile.TryParse(document)!;

        Assert.AreEqual(7, scale.BodyRegion.Count);
        Assert.AreEqual(0, scale.MorphWeight.Count);
        Assert.AreEqual(0x204a8UL, scale.BodyRegion[0].Name);
        Assert.AreEqual(1f, scale.BodyRegion[0].Value);
        Assert.AreEqual(1, scale.BodyRegion[0].Bones.Count);
        var bones = scale.BodyRegion.SelectMany(region => region.Bones).ToArray();
        Assert.IsTrue(bones.All(bone => bone.Min <= bone.Max));
        Assert.IsTrue(bones.All(bone => bone.Rotate.Count == 3 && bone.Scale.Count == 3 && bone.Translate.Count == 3));
        Assert.IsTrue(bones.Any(bone => bone.Scale.Concat(bone.Translate).Concat(bone.Rotate).Any(component => component != 0)));
    }
}
