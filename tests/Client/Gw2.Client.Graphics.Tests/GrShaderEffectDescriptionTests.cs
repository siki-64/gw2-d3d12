using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using Crystal.Graphics;
using Gw2.Dat.Content;
using Gw2.Client.Graphics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Graphics.Tests;

[TestClass]
public sealed class GrShaderEffectDescriptionTests
{
    private const uint AboveWaterMaterialFileId = 187551;
    private const int HighVertexShaderIndex = 0;
    private const int HighPixelShaderIndex = 3;

    [TestMethod]
    public void WaterMaterialDescribesOneConstantBufferPerStageAndItsResources()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var material = reader.ReadMaterial(AboveWaterMaterialFileId)!;
        var vertex = Gw2BgfxShaderBinary.TryParse(Dx11Binary(material, HighVertexShaderIndex).Data)!;
        var fragment = Gw2BgfxShaderBinary.TryParse(Dx11Binary(material, HighPixelShaderIndex).Data)!;

        var description = GrShaderEffectDescription.Describe(vertex, fragment);
        Assert.AreEqual(2, description.ConstantBuffers.Count);
        Assert.AreEqual(ShaderStages.Vertex, description.ConstantBuffers[0].Stages);
        Assert.AreEqual(ShaderStages.Pixel, description.ConstantBuffers[1].Stages);
        foreach (var buffer in description.ConstantBuffers)
            Assert.IsTrue(buffer.Variables.All(variable => variable.Offset % 16 == 0 && variable.Offset + variable.Size <= buffer.Size), buffer.Name);
        Assert.AreEqual(vertex.Code.Span.Length, description.VertexShader.Length);
        Assert.AreEqual(fragment.Code.Span.Length, description.PixelShader.Length);
        var vertexConstants = description.ConstantBuffers.Single(buffer => buffer.Stages == ShaderStages.Vertex);
        var world = vertexConstants.Variables.Single(variable => variable.Name == "World");
        Assert.AreEqual((0, 416, 64, 64, 4), (vertexConstants.Register, vertexConstants.Size, world.Offset, world.Size, world.Elements));
        var environmentMap = description.Resources.Single(resource => resource.Name == "ssEnvMap");
        Assert.AreEqual((ShaderResourceKind.Texture, ShaderResourceDimension.TextureCube, 13, ShaderStages.Pixel), (environmentMap.Kind, environmentMap.Dimension, environmentMap.Register, environmentMap.Stages));
        Assert.AreEqual(13, description.Resources.Single(resource => resource.Name == "ssEnvMapSampler").Register);
        CollectionAssert.AreEqual(new[] { "POSITION0", "NORMAL0", "COLOR0" }, description.VertexInputs.Select(input => $"{input.Semantic}{input.SemanticIndex}").ToArray());
    }

    private static Gw2AmatShaderBinary Dx11Binary(Gw2AmatMaterial material, int shaderIndex) =>
        material.Shaders[shaderIndex].Binaries.First(binary => binary.Backend == Gw2AmatShaderBackend.Dx11);
}
