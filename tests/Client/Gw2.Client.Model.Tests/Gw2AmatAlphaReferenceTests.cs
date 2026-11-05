using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2AmatAlphaReferenceTests
{
    private const uint LeafMaterialFileId = 0xE106;
    

    [TestMethod]
    public void OnlyTheFadingLeafEffectCarriesAnAlphaReference()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var effects = reader.ReadMaterial(LeafMaterialFileId)!.Techniques
            .SelectMany(technique => technique.Passes)
            .SelectMany(pass => pass.Effects)
            .DistinctBy(effect => effect.Token)
            .ToList();

        foreach (var effect in effects)
        {
            var expected = effect.Token == Gw2AmatEffectKeys.Fading ? 1f / 255f : 0f;
            Assert.AreEqual(expected, new Gw2AmatRenderState(effect.RenderState).AlphaReference, 1e-6f);
        }
        Assert.IsTrue(effects.Any(effect => effect.Token == Gw2AmatEffectKeys.Fading));
    }
}
