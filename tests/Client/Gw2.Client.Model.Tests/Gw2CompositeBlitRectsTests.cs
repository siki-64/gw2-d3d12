using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2CompositeBlitRectsTests
{
    private const uint CompositeFileId = 154681;

    [TestMethod]
    public void ReadsBlitRectsAndBoneScaleSetsWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var composite = Gw2Composite.TryParse(reader.ReadDocument(CompositeFileId))!;

        Assert.AreEqual(5, composite.BlitRects.Count);
        var charrHeavy = composite.BlitRects[0];
        Assert.AreEqual("ArmorCharrHeavy", charrHeavy.Name);
        Assert.AreEqual(1024u, charrHeavy.Width);
        Assert.AreEqual(1024u, charrHeavy.Height);
        Assert.AreEqual(17, charrHeavy.RectIndices.Length);
        Assert.AreEqual(19, charrHeavy.Rects.Count);
        CollectionAssert.AreEqual(new uint[] { 0, 512, 384, 1024 }, charrHeavy.Rects[0].ToArray());

        Assert.AreEqual(291, composite.BoneScaleSets.Count);
        Assert.AreEqual(8, composite.BoneScaleSets[0].BodyRegion.Count);
        Assert.AreEqual(0.62f, composite.BoneScaleSets[0].BodyRegion[0].Value, 1e-6f);
        Assert.AreEqual(120, composite.BoneScaleSets.Count(set => set.MorphWeight.Count > 0));
    }
}
