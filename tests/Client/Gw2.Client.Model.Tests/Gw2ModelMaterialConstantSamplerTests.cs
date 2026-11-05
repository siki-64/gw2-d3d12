using System;
using System.IO;
using System.Linq;
using Gw2.Dat.Content;
using Gw2.Formats;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2ModelMaterialConstantSamplerTests
{
    private const uint TelegraphModelFileId = 0xD226;
    private const uint DiffuseFadeToken = 0x5472FF5C;
    private const uint AllyMaterialId = 7;

    [TestMethod]
    public void FadesTheTelegraphInOverTheStartClip()
    {
        var start = TelegraphAnimation(Token64.Encode("areasa") | 0x4000000000000000UL);
        var granny = Gw2GrannyAnimation.TryParse(start.GrannyAnimation, start.PointerSize);
        var fade = start.MaterialConstantAnimations.Single(constant => constant.MaterialId == AllyMaterialId && constant.ConstantToken == DiffuseFadeToken);

        Assert.AreEqual(0f, Gw2ModelMaterialConstantSampler.ValueAt(granny, fade, 0f).X, 1e-2f);
        Assert.AreEqual(1f, Gw2ModelMaterialConstantSampler.ValueAt(granny, fade, granny!.Duration).X, 1e-2f);
    }

    [TestMethod]
    public void KeepsTheInitialValueWithoutATrack()
    {
        var zeroPose = TelegraphAnimation(Token64.Encode("zeropose"));
        var fade = zeroPose.MaterialConstantAnimations.Single(constant => constant.MaterialId == AllyMaterialId && constant.ConstantToken == DiffuseFadeToken);

        Assert.AreEqual(fade.InitialValue, Gw2ModelMaterialConstantSampler.ValueAt(null, fade, 0f));
    }

    private static Gw2ModelAnimationData TelegraphAnimation(ulong token)
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        return reader.ReadModelAnimations(TelegraphModelFileId)!.Animations.Single(animation => animation.Token == token);
    }
}
