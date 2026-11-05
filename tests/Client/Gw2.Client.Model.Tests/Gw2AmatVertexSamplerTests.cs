using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2AmatVertexSamplerTests
{
    private const uint VertexEngineTextureMaterialFileId = 77598;
    private const uint VertexItemTextureMaterialFileId = 184994;

    [TestMethod]
    public void VertexEngineTexturesAreFlaggedTen()
    {
        var samplers = VertexSamplersOf(VertexEngineTextureMaterialFileId);
        Assert.IsTrue(samplers.Any(sampler => sampler.IsVertexEngineTexture && sampler.TextureSlot == 3 && sampler.ResourceIndex == 0));
    }

    [TestMethod]
    public void VertexItemTexturesUseTheirSlotAsIndex()
    {
        var samplers = VertexSamplersOf(VertexItemTextureMaterialFileId);
        Assert.AreNotEqual(0, samplers.Length);
        foreach (var sampler in samplers.Where(sampler => !sampler.IsVertexEngineTexture))
        {
            Assert.IsTrue(sampler.IsVertexTexture);
            Assert.AreEqual(sampler.TextureSlot, sampler.ResourceIndex);
        }
    }

    private static Gw2AmatSamplerBinding[] VertexSamplersOf(uint fileId)
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var material = reader.ReadMaterial(fileId)!;
        return material.Shaders
            .Where(shader => !shader.IsPixelShader)
            .SelectMany(shader => shader.Binaries.Where(binary => binary.Backend == Gw2AmatShaderBackend.Dx11))
            .SelectMany(binary => binary.Samplers)
            .ToArray();
    }
}
