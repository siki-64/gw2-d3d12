using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using Gw2.Client.Scene;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Scene.Tests;

[TestClass]
public sealed class CompositeAtlasTests
{
    [TestMethod]
    public void HairCoversHairFaceAndEarsUnlessEarsAreHidden()
    {
        CollectionAssert.AreEqual(new[] { 10, 9, 11 }, Gw2CompositeAtlas.Regions(6, 0, 0, false).ToArray());
        CollectionAssert.AreEqual(new[] { 10, 9 }, Gw2CompositeAtlas.Regions(6, 0x10000, 0, false).ToArray());
    }

    [TestMethod]
    public void HideFlagBitElevenAddsRegionTwelve()
    {
        CollectionAssert.AreEqual(new[] { 12, 12 }, Gw2CompositeAtlas.Regions(0, 0, 1u << 11, false).ToArray());
        CollectionAssert.AreEqual(new[] { 12, 11 }, Gw2CompositeAtlas.Regions(0, 0, 1u << 16, false).ToArray());
    }

    [TestMethod]
    public void BlitCopiesTexelsFromTheBoundsOrigin()
    {
        var source = new Gw2RgbaImage(4, 4);
        source.SetPixel(1, 2, 0xFF112233);
        var atlas = new Gw2RgbaImage(16, 16);
        Gw2CompositeAtlas.Blit(atlas, [new Gw2AtlasRect(8, 8, 12, 12), new Gw2AtlasRect(4, 4, 8, 8)], source);

        Assert.AreEqual(0xFF112233u, atlas.GetPixel(5, 6));
        Assert.AreEqual(0u, atlas.GetPixel(9, 10));
    }

    [TestMethod]
    public void FullCutMaskRestoresTheUnshiftedSourceAndEmptyMaskKeepsTheShift()
    {
        var source = new Gw2RgbaImage(2, 1);
        source.SetPixel(0, 0, 0xFF808080);
        source.SetPixel(1, 0, 0xFF808080);
        var cutMask = new Gw2RgbaImage(2, 1);
        cutMask.SetPixel(0, 0, 0xFFFFFFFF);
        cutMask.SetPixel(1, 0, 0xFF000000);
        var darken = new Gw2ColorShift(0f, 1f, 0f, 1f, 0.5f);
        var atlas = new Gw2RgbaImage(2, 1);
        Gw2CompositeAtlas.Compose(atlas, [new Gw2AtlasRect(0, 0, 2, 1)], source, [new(null, darken), new(cutMask, null)]);

        Assert.AreEqual(0xFF808080u, atlas.GetPixel(0, 0));
        Assert.AreNotEqual(0xFF808080u, atlas.GetPixel(1, 0));
    }

    [TestMethod]
    public void ComposeOverKeepsTheAtlasOutsideTheCutMask()
    {
        var source = new Gw2RgbaImage(2, 1);
        source.SetPixel(0, 0, 0xFF808080);
        source.SetPixel(1, 0, 0xFF808080);
        var cutMask = new Gw2RgbaImage(2, 1);
        cutMask.SetPixel(0, 0, 0xFFFFFFFF);
        cutMask.SetPixel(1, 0, 0xFF000000);
        var atlas = new Gw2RgbaImage(2, 1);
        atlas.SetPixel(0, 0, 0xFF102030);
        atlas.SetPixel(1, 0, 0xFF102030);
        Gw2CompositeAtlas.ComposeOver(atlas, [new Gw2AtlasRect(0, 0, 2, 1)], source, [new(cutMask, null)]);

        Assert.AreEqual(0xFF808080u, atlas.GetPixel(0, 0));
        Assert.AreEqual(0xFF102030u, atlas.GetPixel(1, 0));
    }

    [TestMethod]
    public void HumanFemaleFaceCutMaskDecodesWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var mask = Gw2RgbaImage.FromMaskTexture(reader.ReadTexture(128337)!.Texture!);

        Assert.AreEqual(256, mask.Width);
        Assert.AreEqual(128, mask.Height);
        Assert.IsTrue(Enumerable.Range(0, mask.Width * mask.Height).Any(texel => mask.Pixels[texel * 4] > 200));
        Assert.IsTrue(Enumerable.Range(0, mask.Width * mask.Height).Any(texel => mask.Pixels[texel * 4] < 50));
    }

    [TestMethod]
    public void HumanFemaleBareChestLandsInArmorHeavyRegionTwelveWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var composite = reader.ReadComposite(Gw2RaceAnimations.CompositeFileId)!;
        var race = Gw2CharacterBodyParts.FindRace(composite, "HumanFemale")!;
        var chest = Gw2CharacterBodyParts.FirstPartOfEachType(race, [Gw2CompositePartType.SkinChest]).Single();
        var set = composite.BlitRects[(int)chest.BlitRectIndex];

        Assert.AreEqual("ArmorHeavy", set.Name);
        CollectionAssert.AreEqual(new[] { new Gw2AtlasRect(384, 768, 896, 1024) }, Gw2CompositeAtlas.PartRects(set, chest, 0, false).ToArray());
        CollectionAssert.AreEqual(new[] { new Gw2AtlasRect(192, 384, 448, 512) }, Gw2CompositeAtlas.PartRects(set, chest, 0, true).ToArray());
    }
}
