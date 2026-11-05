using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2MapDecalsTests
{
    private const uint DecalsV10FileId = 187611;

    [TestMethod]
    public void ReadsTextureLayoutConstantsAndPropLinksWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var decals = Gw2MapDecals.TryParse(reader.ReadDocument(DecalsV10FileId))!.Decals;

        Assert.AreEqual(35, decals.Count);
        Assert.AreEqual(new Vector2(16, 16), decals[0].GridSize);
        Assert.AreEqual(Vector2.One, decals[0].TextureScaleUv0);
        Assert.AreEqual(185109u, decals[0].MaterialFileId);
        Assert.AreEqual(2, decals[0].TextureFileIds.Count);
        Assert.IsTrue(decals.All(decal => decal.MaterialFileId != 0 && decal.TextureFileIds.All(fileId => fileId != 0) && decal.Vertices.Count == 0));
        Assert.AreEqual(new Vector2(87.932f, 87.932f), decals[0].ProjectionSize);
        Assert.AreEqual(Vector2.One, decals[0].TextureScaleUv1);
        CollectionAssert.AreEqual(new[] { 1325015965594879014ul, 1325396675790963725ul }, decals[0].PropIds.ToArray());
        Assert.AreEqual(26, decals.Count(decal => decal.PropIds.Count > 0));

        Assert.AreEqual(7, decals.Count(decal => decal.Constants.Count > 0));
        Assert.AreEqual(new Gw2MapDecalConstant(1597866340, Vector4.One), decals[2].Constants[0]);
    }
}
