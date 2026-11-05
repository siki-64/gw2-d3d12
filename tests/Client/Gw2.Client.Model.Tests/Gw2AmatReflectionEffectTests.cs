using Gw2.Formats;
using System;
using System.IO;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2AmatReflectionEffectTests
{
    private const uint TerrainMaterialFileId = 184988;
    private const int DefaultPixelShader = 73;
    private const int ReflectionPixelShader = 81;

    [TestMethod]
    public void TerrainMaterialHasItsOwnReflectionPixelShader()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var material = reader.ReadMaterial(TerrainMaterialFileId)!;
        Assert.IsTrue(material.TryResolveEffect(0, 0, Gw2AmatEffectKeys.Default, out var main));
        Assert.IsTrue(material.TryResolveEffect(0, 0, Gw2AmatEffectKeys.Reflection, out var reflection));
        Assert.AreEqual(DefaultPixelShader, main.PixelShaderIndex);
        Assert.AreEqual(ReflectionPixelShader, reflection.PixelShaderIndex);
    }
}
