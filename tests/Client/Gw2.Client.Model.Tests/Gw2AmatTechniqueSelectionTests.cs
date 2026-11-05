using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2AmatTechniqueSelectionTests
{
    private const uint TerrainMaterialFileId = 184988;

    [TestMethod]
    public void EachQualityPicksTheFirstTechniqueAtOrBelowIt()
    {
        var material = ReadMaterial(TerrainMaterialFileId);
        var qualities = material.Techniques.Select(technique => Gw2TechniqueQuality.SelectionQualityOf(technique.Quality)).ToList();
        foreach (var quality in new[] { Gw2ShaderQuality.Low, Gw2ShaderQuality.Medium, Gw2ShaderQuality.High })
        {
            var index = material.TechniqueIndexFor(quality);
            var expected = qualities.FindIndex(candidate => candidate <= quality);
            Assert.AreEqual(expected < 0 ? qualities.Count - 1 : expected, index, quality.ToString());
        }
    }

    [TestMethod]
    public void HighNeverPicksAnUltraTechnique()
    {
        var material = ReadMaterial(TerrainMaterialFileId);
        Assert.AreNotEqual(Gw2ShaderQuality.Ultra, Gw2TechniqueQuality.SelectionQualityOf(material.TechniqueFor(Gw2ShaderQuality.High)!.Quality));
    }

    [TestMethod]
    public void AliasKeysFallBackInTheExeOrder()
    {
        CollectionAssert.AreEqual(new[] { Gw2AmatEffectKeys.Fading }, Gw2AmatEffectKeys.FallbacksOf(Gw2AmatEffectKeys.FadingAlias).ToArray());
        CollectionAssert.AreEqual(new[] { Gw2AmatEffectKeys.ShadowDepth }, Gw2AmatEffectKeys.FallbacksOf(Gw2AmatEffectKeys.ShadowDepthAlias).ToArray());
        CollectionAssert.AreEqual(
            new[] { Gw2AmatEffectKeys.NormalSpecDepthAlias, Gw2AmatEffectKeys.DepthAlias, Gw2AmatEffectKeys.NormalSpecDepth },
            Gw2AmatEffectKeys.FallbacksOf(Gw2AmatEffectKeys.NormalSpecDepthAliasChain).ToArray());
        Assert.AreEqual(0, Gw2AmatEffectKeys.FallbacksOf(Gw2AmatEffectKeys.Default).Count);
    }

    [TestMethod]
    public void MissingVertexVariantsFallBackToFlag80ThenStatic()
    {
        var effect = new Gw2AmatEffect(Gw2AmatEffectKeys.Default, 0, 0, 0, [new(0, 10), new(2, 12)]);
        Assert.AreEqual(12u, effect.VertexShaderVariantFor(4)!.VertexShaderIndex);
        Assert.AreEqual(10u, effect.VertexShaderVariantFor(3)!.VertexShaderIndex);
        Assert.AreEqual(10u, effect.VertexShaderVariantFor(1)!.VertexShaderIndex);
        Assert.IsNull(new Gw2AmatEffect(Gw2AmatEffectKeys.Default, 0, 0, 0, [new(3, 13)]).VertexShaderVariantFor(0));
    }

    private static Gw2AmatMaterial ReadMaterial(uint fileId)
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        return reader.ReadMaterial(fileId)!;
    }
}
