using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using Gw2.Client.Scene;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Scene.Tests;

[TestClass]
public sealed class SkinPatternTests
{
    [TestMethod]
    public void HumanFemalePatternsWrapFromIndexOneAndFaceTextureDecodesWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var composite = reader.ReadComposite(Gw2RaceAnimations.CompositeFileId)!;
        var race = Gw2CharacterBodyParts.FindRace(composite, "HumanFemale")!;

        Assert.AreEqual(11, race.SkinPatterns.Count);
        Assert.IsNull(Gw2CharacterSkinPattern.Select(race, 0));
        Assert.AreSame(race.SkinPatterns[0], Gw2CharacterSkinPattern.Select(race, 1));
        Assert.AreSame(race.SkinPatterns[0], Gw2CharacterSkinPattern.Select(race, 12));

        var pattern = Gw2CharacterSkinPattern.Select(race, 1)!;
        Assert.IsTrue(Gw2CharacterSkinPattern.TextureFor(pattern, 5).TryGetFileId(out var faceFileId));
        var mask = Gw2RgbaImage.FromMaskTexture(reader.ReadTexture(faceFileId)!.Texture!);
        Assert.IsTrue(Enumerable.Range(0, mask.Width * mask.Height).Any(texel => mask.Pixels[texel * 4] > 0));
    }
}
