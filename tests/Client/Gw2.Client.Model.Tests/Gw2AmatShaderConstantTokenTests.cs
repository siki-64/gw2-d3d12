using System;
using System.IO;
using System.Linq;
using Gw2.Dat.Content;
using Gw2.Formats;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2AmatShaderConstantTokenTests
{
    private const uint AboveWaterMaterialFileId = 187551;
    private const int HighVertexShaderIndex = 0;
    private const int HighPixelShaderIndex = 3;

    [TestMethod]
    public void WaterShaderReflectionNamesPairWithTheirConstantTokens()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var material = reader.ReadMaterial(AboveWaterMaterialFileId)!;
        var vertex = Dx11Binary(material, HighVertexShaderIndex).ConstantTokensByName();
        var pixel = Dx11Binary(material, HighPixelShaderIndex).ConstantTokensByName();

        Assert.AreEqual(Token.Encode("grtime"), vertex["Time"]);
        Assert.AreEqual(Token.Encode("wtrshr"), vertex["ShoreDepth"]);
        Assert.AreEqual(Token.Encode("shrcl"), vertex["ShoreChurnLowA"]);
        Assert.AreEqual(Token.Encode("shrch"), vertex["ShoreChurnHighA"]);
        Assert.AreEqual(Token.Encode("blurwat"), pixel["BlurryWater"]);
        Assert.AreEqual(Token.Encode("spclr"), pixel["SpecularCp"]);
        Assert.AreEqual(Token.Encode("shrclr"), pixel["ShoreColor"]);
        Assert.AreEqual(Token.Encode("shrclb"), pixel["ShoreChurnLowB"]);
        Assert.AreEqual(Token.Encode("shrchb"), pixel["ShoreChurnHighB"]);
    }

    private static Gw2AmatShaderBinary Dx11Binary(Gw2AmatMaterial material, int shaderIndex) =>
        material.Shaders[shaderIndex].Binaries.First(binary => binary.Backend == Gw2AmatShaderBackend.Dx11);
}
