using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2AmatEffectCullTests
{
    private const uint LeafMaterialFileId = 0xE106;
    private const uint TrunkMaterialFileId = 0xE173;

    [TestMethod]
    public void LeafAndTrunkEffectsCullCounterClockwiseSoTwoSidednessComesFromTheModel()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        foreach (var materialFileId in new[] { LeafMaterialFileId, TrunkMaterialFileId })
        {
            var effects = reader.ReadMaterial(materialFileId)!.Techniques
                .SelectMany(technique => technique.Passes)
                .SelectMany(pass => pass.Effects)
                .ToList();
            Assert.IsTrue(effects.All(effect => Gw2AmatEffectCull.Of(effect.ShaderPassFlags) == Gw2AmatCull.CounterClockwise), $"{materialFileId:X}");
        }
    }

    [TestMethod]
    public void ShaderPassFlagsPickTheCullAndMirroringSwapsIt()
    {
        Assert.AreEqual(Gw2AmatCull.CounterClockwise, Gw2AmatEffectCull.Of(0x4001));
        Assert.AreEqual(Gw2AmatCull.Clockwise, Gw2AmatEffectCull.Of(0x2));
        Assert.AreEqual(Gw2AmatCull.None, Gw2AmatEffectCull.Of(0x4000));
        Assert.AreEqual(Gw2AmatCull.Clockwise, Gw2AmatEffectCull.Mirrored(Gw2AmatCull.CounterClockwise));
        Assert.AreEqual(Gw2AmatCull.None, Gw2AmatEffectCull.Mirrored(Gw2AmatCull.None));
    }
}
