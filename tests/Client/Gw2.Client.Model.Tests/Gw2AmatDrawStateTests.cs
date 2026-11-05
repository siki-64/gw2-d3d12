using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2AmatDrawStateTests
{
    private const uint TerrainMaterialFileId = 184988;
    private const uint TwoPassMaterialFileId = 16068;

    [TestMethod]
    public void TerrainEffectsGetTheExeDepthAndWriteMasks()
    {
        var material = ReadMaterial(TerrainMaterialFileId);
        var technique = material.TechniqueFor(Gw2ShaderQuality.High)!;
        var effects = technique.Passes[0].Effects;

        var standard = StateOf(effects, Gw2AmatEffectKeys.Default);
        Assert.AreEqual(new Gw2AmatDrawState(Gw2DepthTest.LessEqual, true, Gw2ColorWrite.All, 0f, false), standard);

        var shadowDepth = StateOf(effects, Gw2AmatEffectKeys.ShadowDepth);
        Assert.AreEqual(Gw2ColorWrite.None, shadowDepth.ColorWrite);
        Assert.IsTrue(shadowDepth.DepthWrite);

        var fading = StateOf(effects, Gw2AmatEffectKeys.Fading);
        Assert.AreEqual(Gw2ColorWrite.Rgb, fading.ColorWrite);

        var normalSpecDepth = StateOf(effects, Gw2AmatEffectKeys.NormalSpecDepth);
        Assert.AreEqual(Gw2AmatDrawState.DepthOnlyEffectBias, normalSpecDepth.DepthBias);
    }

    [TestMethod]
    public void LaterPassesTestEqualUnlessTheEffectAsksForLessEqual()
    {
        Assert.AreEqual(Gw2DepthTest.LessEqual, Gw2AmatDrawState.Of(0, 0, Gw2AmatEffectKeys.Default).DepthTest);
        Assert.AreEqual(Gw2DepthTest.Equal, Gw2AmatDrawState.Of(0, 1, Gw2AmatEffectKeys.Default).DepthTest);
        Assert.AreEqual(Gw2DepthTest.LessEqual, Gw2AmatDrawState.Of(Gw2AmatShaderPassFlags.LessEqualOnLaterPasses, 1, Gw2AmatEffectKeys.Default).DepthTest);
    }

    [TestMethod]
    public void DrawFlagsOverrideTheDepthTestInTheExeOrder()
    {
        Assert.AreEqual(Gw2DepthTest.Disabled, Gw2AmatDrawState.Of(Gw2AmatShaderPassFlags.NoDepthTest, 0, Gw2AmatEffectKeys.Default).DepthTest);
        Assert.AreEqual(Gw2DepthTest.Equal, Gw2AmatDrawState.Of(Gw2AmatShaderPassFlags.NoDepthTest, 0, Gw2AmatEffectKeys.Default, Gw2DrawFlags.EqualDepthTest).DepthTest);
        Assert.AreEqual(Gw2DepthTest.Greater, Gw2AmatDrawState.Of(0, 0, Gw2AmatEffectKeys.Default, Gw2DrawFlags.EqualDepthTest | Gw2DrawFlags.GreaterDepthTest).DepthTest);
        Assert.IsFalse(Gw2AmatDrawState.Of(0, 0, Gw2AmatEffectKeys.Default, Gw2DrawFlags.LessEqualWithoutDepthWrite).DepthWrite);
    }

    [TestMethod]
    public void ColorMaskCombinesRgbAndAlphaFlags()
    {
        Assert.AreEqual(Gw2ColorWrite.Alpha, Gw2AmatDrawState.Of(Gw2AmatShaderPassFlags.NoRgbWrite, 0, Gw2AmatEffectKeys.Default).ColorWrite);
        Assert.AreEqual(Gw2ColorWrite.None, Gw2AmatDrawState.Of(Gw2AmatShaderPassFlags.NoRgbWrite | Gw2AmatShaderPassFlags.NoAlphaWrite, 0, Gw2AmatEffectKeys.Default).ColorWrite);
        Assert.AreEqual(Gw2ColorWrite.None, Gw2AmatDrawState.Of(Gw2AmatShaderPassFlags.NoColorWrite, 0, Gw2AmatEffectKeys.Default).ColorWrite);
        Assert.AreEqual(Gw2ColorWrite.Alpha, Gw2AmatDrawState.Of(0, 0, Gw2AmatEffectKeys.Default, Gw2DrawFlags.NoRgbWrite).ColorWrite);
    }

    [TestMethod]
    public void DepthBiasPrefersDepthOnlyEffectsThenNegativeThenView()
    {
        Assert.AreEqual(Gw2AmatDrawState.DepthOnlyEffectBias, Gw2AmatDrawState.Of(Gw2AmatShaderPassFlags.NegativeDepthBias, 0, Gw2AmatEffectKeys.DepthAlias).DepthBias);
        Assert.AreEqual(Gw2AmatDrawState.NegativeBias, Gw2AmatDrawState.Of(Gw2AmatShaderPassFlags.NegativeDepthBias, 0, Gw2AmatEffectKeys.Default, viewDepthBias: 2f).DepthBias);
        var view = Gw2AmatDrawState.Of(0, 0, Gw2AmatEffectKeys.Default, viewDepthBias: 2f);
        Assert.AreEqual(2f, view.DepthBias);
        Assert.IsTrue(view.SlopeScaledDepthBias);
    }

    [TestMethod]
    public void LaterPassesDoNotFallBackToTheDefaultEffect()
    {
        var material = ReadMaterial(TwoPassMaterialFileId);
        var techniqueIndex = material.TechniqueIndexFor(Gw2ShaderQuality.High);
        Assert.IsTrue(material.TryResolveEffect(techniqueIndex, 0, Gw2AmatEffectKeys.ShadowDepth, out _));
        Assert.IsFalse(material.TryResolveEffect(techniqueIndex, 1, Gw2AmatEffectKeys.ShadowDepth, out _));
        Assert.IsFalse(material.TryResolveEffect(techniqueIndex, 1, 0x1234, out _));
        Assert.IsTrue(material.TryResolveEffect(techniqueIndex, 0, 0x1234, out var fallback));
        Assert.AreEqual(Gw2AmatEffectKeys.Default, fallback.Effect.Token);
    }

    [TestMethod]
    public void TwoPassMaterialSplitsAlphaAndColourWrites()
    {
        var material = ReadMaterial(TwoPassMaterialFileId);
        var techniqueIndex = material.TechniqueIndexFor(Gw2ShaderQuality.High);
        Assert.IsTrue(material.TryResolveEffect(techniqueIndex, 0, Gw2AmatEffectKeys.Default, out var first));
        Assert.IsTrue(material.TryResolveEffect(techniqueIndex, 1, Gw2AmatEffectKeys.Default, out var second));
        Assert.AreEqual(new Gw2AmatDrawState(Gw2DepthTest.LessEqual, true, Gw2ColorWrite.Alpha, 0f, false), Gw2AmatDrawState.Of(first.Effect.ShaderPassFlags, 0, Gw2AmatEffectKeys.Default));
        Assert.AreEqual(new Gw2AmatDrawState(Gw2DepthTest.LessEqual, false, Gw2ColorWrite.Rgb, Gw2AmatDrawState.NegativeBias, false), Gw2AmatDrawState.Of(second.Effect.ShaderPassFlags, 1, Gw2AmatEffectKeys.Default));
    }
    private static Gw2AmatDrawState StateOf(System.Collections.Generic.IReadOnlyList<Gw2AmatEffect> effects, ulong key)
    {
        var effect = effects.Single(candidate => candidate.Token == key);
        return Gw2AmatDrawState.Of(effect.ShaderPassFlags, 0, key);
    }

    [TestMethod]
    public void ViewWithoutDepthTestDrawsAlwaysWithoutDepthWrite()
    {
        var state = Gw2AmatDrawState.Of(0, 0, Gw2AmatEffectKeys.Default, viewDepthTest: false);
        Assert.AreEqual(Gw2DepthTest.Always, state.DepthTest);
        Assert.IsFalse(state.DepthWrite);
        Assert.AreEqual(Gw2DepthTest.Disabled, Gw2AmatDrawState.Of(Gw2AmatShaderPassFlags.NoDepthTest, 0, Gw2AmatEffectKeys.Default, viewDepthTest: false).DepthTest);
        Assert.AreEqual(Gw2DepthTest.Equal, Gw2AmatDrawState.Of(0, 1, Gw2AmatEffectKeys.Default, Gw2DrawFlags.EqualDepthTest, viewDepthTest: false).DepthTest);
    }

    [TestMethod]
    public void StageNoDepthWriteKeepsWriteOnlyForUnmarkedItemsOfMarkedStages()
    {
        Assert.IsFalse(Gw2AmatDrawState.Of(0, 0, Gw2AmatEffectKeys.Default, stageFlags: Gw2DrawStageFlags.NoDepthWrite).DepthWrite);
        var markedStage = Gw2DrawStageFlags.NoDepthWrite | Gw2DrawStageFlags.KeepsDepthWriteForMarkedItems;
        Assert.IsTrue(Gw2AmatDrawState.Of(0, 0, Gw2AmatEffectKeys.Default, stageFlags: markedStage).DepthWrite);
        Assert.IsFalse(Gw2AmatDrawState.Of(0, 0, Gw2AmatEffectKeys.Default, 0x800000, stageFlags: markedStage).DepthWrite);
    }

    [TestMethod]
    public void TwoPassMaterialLandsInTheSplitPassBucket()
    {
        var material = ReadMaterial(TwoPassMaterialFileId);
        Assert.AreEqual(Gw2SortBuckets.SplitPasses, material.SortBucket);
        Assert.AreEqual(2, material.TechniqueFor(Gw2ShaderQuality.High)!.Passes.Count);
    }

    private static Gw2AmatMaterial ReadMaterial(uint fileId)
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        return reader.ReadMaterial(fileId)!;
    }
}
