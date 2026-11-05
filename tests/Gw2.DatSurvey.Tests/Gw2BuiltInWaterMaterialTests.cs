using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using Gw2.Dat.Content;
using Gw2.Dat.Archive;
using Gw2.DatSurvey;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.DatSurvey.Tests;

[TestClass]
public sealed class Gw2BuiltInWaterMaterialTests
{
    private const long ImageBase = 0x7FF7B1B10000;
    private const long DataSectionRvaMinusFileOffset = 0x1A00;

    [TestMethod]
    public void AboveWaterMatchesTheExePack() => AssertMatches(Gw2BuiltInWaterMaterial.Above);

    [TestMethod]
    public void BelowWaterMatchesTheExePack() => AssertMatches(Gw2BuiltInWaterMaterial.Below);

    private static void AssertMatches(Gw2BuiltInWaterMaterial expected)
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        var exePath = Path.ChangeExtension(dat, ".exe");
        if (!File.Exists(exePath)) Assert.Inconclusive($"{exePath} is missing.");
        var offset = (int)(expected.PackAddress - ImageBase - DataSectionRvaMinusFileOffset);
        var pack = ExeMaterialPacks.ReadMaterials(File.ReadAllBytes(exePath)).Single(candidate => candidate.FileOffset == offset);
        var material = Gw2PackDecoders.Decode<Gw2AmatMaterial>(pack.Document)!;
        var techniqueIndex = material.TechniqueIndexFor(Gw2ShaderQuality.High);
        Assert.IsTrue(material.TryResolveEffect(techniqueIndex, 0, Gw2AmatEffectKeys.Default, out var resolved));
        Assert.AreEqual(expected.RenderState, resolved.Effect.RenderState);
        Assert.AreEqual(expected.ShaderPassFlags, resolved.Effect.ShaderPassFlags);
        var pixel = material.Shaders[(int)resolved.Effect.PixelShaderIndex].Binaries.First(binary => binary.Backend == Gw2AmatShaderBackend.Dx11);
        var vertex = material.Shaders[(int)resolved.Effect.VertexShaderVariantFor(0)!.VertexShaderIndex].Binaries.First(binary => binary.Backend == Gw2AmatShaderBackend.Dx11);
        var actualSamplers = pixel.Samplers.Select(sampler => (sampler.TextureSlot, sampler.IsEngineTexture, sampler.ResourceIndex, material.SamplerStateOf(sampler))).ToArray();
        CollectionAssert.AreEqual(expected.Samplers.Select(sampler => (sampler.TextureSlot, sampler.IsEngineTexture, sampler.ResourceIndex, sampler.State)).ToArray(), actualSamplers);
        foreach (var (name, token) in vertex.ConstantTokensByName().Concat(pixel.ConstantTokensByName()).Where(constant => !constant.Key.StartsWith("cb0_", StringComparison.Ordinal)))
            Assert.AreEqual(Gw2BuiltInWaterMaterial.ConstantTokens[name], token, name);
    }
}
