using Gw2.Formats;
using Gw2.Dat.Content;
using Gw2.Dat.Archive;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.DatSurvey.Tests;

[TestClass]
public sealed class Gw2BgfxShaderBinaryTests
{
    private const uint CanonicalPropMaterialFileId = 0x1130E;
    private const int ConstantBufferDeclarationOpcode = 89;
    private const uint EyeOverlayMaterialFileId = 0x3B66;
    private const uint HumanFemaleBodyMaterialFileId = 0x8585;
    private const int HumanFemaleBodyPixelShaderIndex = 71;

    [TestMethod]
    [TestCategory("Corpus")]
    public void EveryAmatBinaryParsesToTheEndAndItsCodeChecksumValidates()
    {
        using var archive = OpenFrozenArchive();
        var source = new Gw2DatContentSource(archive);
        var binaries = 0;
        var largestConstantBufferCount = 0;
        foreach (var fileId in AmatFileIds(archive))
        {
            var material = Gw2PackDecoders.Decode<Gw2AmatMaterial>(source.DecodeAsset(fileId));
            if (material is null) continue;
            foreach (var binary in material.Shaders.SelectMany(shader => shader.Binaries))
            {
                if (binary.Backend != Gw2AmatShaderBackend.Dx11 || binary.Data.Length == 0) continue;
                var shader = Gw2BgfxShaderBinary.TryParse(binary.Data);
                Assert.IsNotNull(shader, $"file 0x{fileId:X} does not parse as a bgfx shader binary");
                var code = shader.ChecksummedCode();
                Assert.IsNotNull(code, $"file 0x{fileId:X} has no DXBC container");
                CollectionAssert.AreEqual(Gw2DxbcChecksum.Compute(code), code.AsSpan(4, 16).ToArray());
                var program = Gw2ShaderProgram.TryParse(shader.Container!.TryGetChunk("SHEX", out var chunk) ? chunk.Data : default);
                Assert.IsNotNull(program, $"file 0x{fileId:X} has no SHEX program");
                largestConstantBufferCount = Math.Max(largestConstantBufferCount,
                    program.Instructions.Count(instruction => instruction.OpCode == ConstantBufferDeclarationOpcode));
                binaries++;
            }
        }

        Console.WriteLine($"{binaries} binaries, at most {largestConstantBufferCount} constant buffers per stage");
        Assert.IsTrue(binaries > 0);
        Assert.AreEqual(1, largestConstantBufferCount);
    }

    [TestMethod]
    public void CanonicalPropPixelShaderIsAFragmentShaderWithItsSamplersInTheUniformTable()
    {
        using var archive = OpenFrozenArchive();
        var material = Gw2PackDecoders.Decode<Gw2AmatMaterial>(new Gw2DatContentSource(archive).DecodeAsset(CanonicalPropMaterialFileId))!;
        var pixel = material.Shaders.First(shader => shader.IsPixelShader).Binaries.First(binary => binary.Backend == Gw2AmatShaderBackend.Dx11);

        var shader = Gw2BgfxShaderBinary.TryParse(pixel.Data)!;

        Assert.AreEqual(Gw2BgfxShaderStage.Fragment, shader.Stage);
        Assert.IsTrue(shader.Uniforms.Any(uniform => uniform.Kind == Gw2ShaderReflectionKind.Sampler));
        Assert.IsTrue(shader.ConstantBufferSize > 0);
    }

    [TestMethod]
    public void CanonicalPropVertexShaderReadsPositionNormalAndFourTextureCoordinates()
    {
        var shader = CanonicalPropBgfxShader(0);
        Assert.IsTrue(shader.Container!.TryGetChunk("ISGN", out var chunk));

        var inputs = Gw2DxbcSignature.TryParse(chunk.Data)!;

        CollectionAssert.AreEqual(
            new[] { "POSITION0", "NORMAL0", "TEXCOORD0", "TEXCOORD1", "TEXCOORD2", "TEXCOORD3" },
            inputs.Select(input => $"{input.SemanticName}{input.SemanticIndex}").ToArray());
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4, 5 }, inputs.Select(input => input.Register).ToArray());
    }

    [TestMethod]
    public void CanonicalPropVertexInputSignatureWritesBackToTheSameBytes()
    {
        var shader = CanonicalPropBgfxShader(0);
        Assert.IsTrue(shader.Container!.TryGetChunk("ISGN", out var chunk));

        var written = Gw2DxbcSignature.Write(Gw2DxbcSignature.TryParse(chunk.Data)!);

        CollectionAssert.AreEqual(chunk.Data.ToArray(), written);
    }

    [TestMethod]
    public void CanonicalPropPixelShaderDeclaresTwoDimensionalTexturesWithMatchingSamplers()
    {
        var shader = CanonicalPropBgfxShader(64);
        Assert.IsTrue(shader.Container!.TryGetChunk("SHEX", out var chunk));
        var program = Gw2ShaderProgram.TryParse(chunk.Data)!;

        var resources = program.ResourceDeclarations();

        CollectionAssert.AreEqual(new[] { 0, 1, 3, 4, 14 }, resources.Select(resource => resource.Register).ToArray());
        Assert.IsTrue(resources.All(resource => resource.Dimension == Gw2ShaderResourceDimension.Texture2D));
        CollectionAssert.AreEqual(new[] { 0, 1, 3, 4, 14 }, program.SamplerDeclarations().ToArray());
    }

    [TestMethod]
    public void HumanFemaleBodyPixelShaderTakesBacklightShadowLightBufferAndShadowMapFromEngineTextures()
    {
        using var archive = OpenFrozenArchive();
        var material = Gw2PackDecoders.Decode<Gw2AmatMaterial>(new Gw2DatContentSource(archive).DecodeAsset(HumanFemaleBodyMaterialFileId))!;
        var pixel = material.Shaders[HumanFemaleBodyPixelShaderIndex].Binaries.First(binary => binary.Backend == Gw2AmatShaderBackend.Dx11);

        var bindings = pixel.Samplers.OrderBy(binding => binding.TextureSlot)
            .Select(binding => (Slot: (int)binding.TextureSlot, binding.IsEngineTexture, Resource: (int)binding.ResourceIndex))
            .ToArray();

        CollectionAssert.AreEqual(
            new[] { (0, false, 0), (1, false, 1), (2, true, 0), (14, true, 2), (15, true, 5) },
            bindings);
    }

    [TestMethod]
    public void HumanFemaleBodySamplesAlbedoAnisotropicAndTheLightBufferPointClamped()
    {
        using var archive = OpenFrozenArchive();
        var material = Gw2PackDecoders.Decode<Gw2AmatMaterial>(new Gw2DatContentSource(archive).DecodeAsset(HumanFemaleBodyMaterialFileId))!;
        var pixel = material.Shaders[HumanFemaleBodyPixelShaderIndex].Binaries.First(binary => binary.Backend == Gw2AmatShaderBackend.Dx11);
        var stateBySlot = pixel.Samplers.ToDictionary(binding => (int)binding.TextureSlot, material.SamplerStateOf);

        Assert.AreEqual(Gw2SamplerFilter.Anisotropic, stateBySlot[0].Minify);
        Assert.AreEqual(Gw2SamplerAddress.Wrap, stateBySlot[0].AddressU);
        Assert.AreEqual(Gw2SamplerFilter.Linear, stateBySlot[1].Minify);
        Assert.AreEqual(Gw2SamplerFilter.Point, stateBySlot[14].Minify);
        Assert.AreEqual(Gw2SamplerFilter.Point, stateBySlot[14].Mip);
        Assert.AreEqual(Gw2SamplerAddress.Clamp, stateBySlot[14].AddressV);
    }

    [TestMethod]
    public void EyeOverlayMultipliesOverTheBodyAndTheBodyDrawsOpaque()
    {
        using var archive = OpenFrozenArchive();
        var source = new Gw2DatContentSource(archive);
        var overlay = RenderStateOf(Gw2PackDecoders.Decode<Gw2AmatMaterial>(source.DecodeAsset(EyeOverlayMaterialFileId))!);
        var body = RenderStateOf(Gw2PackDecoders.Decode<Gw2AmatMaterial>(source.DecodeAsset(HumanFemaleBodyMaterialFileId))!);

        Assert.IsTrue(overlay.IsBlended);
        Assert.AreEqual(Gw2BlendFactor.DestinationColor, overlay.ColorSource);
        Assert.AreEqual(Gw2BlendFactor.Zero, overlay.ColorDestination);
        Assert.AreEqual(Gw2BlendEquation.Add, overlay.ColorEquation);
        Assert.IsFalse(body.IsBlended);
    }

    private static Gw2AmatRenderState RenderStateOf(Gw2AmatMaterial material)
    {
        var technique = material.Techniques.ToList().IndexOf(material.HighestQualityTechnique!);
        Assert.IsTrue(material.TryResolveEffect(technique, 0, Gw2AmatEffectKeys.Default, out var selection));
        return new Gw2AmatRenderState(selection.Effect.RenderState);
    }

    private static Gw2BgfxShaderBinary CanonicalPropBgfxShader(int shaderIndex)
    {
        using var archive = OpenFrozenArchive();
        var material = Gw2PackDecoders.Decode<Gw2AmatMaterial>(new Gw2DatContentSource(archive).DecodeAsset(CanonicalPropMaterialFileId))!;
        var binary = material.Shaders[shaderIndex].Binaries.First(binary => binary.Backend == Gw2AmatShaderBackend.Dx11);
        return Gw2BgfxShaderBinary.TryParse(binary.Data)!;
    }

    private static FileArchive OpenFrozenArchive()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        return FileArchive.Open(dat, verifyChecksums: false);
    }

    private static IEnumerable<uint> AmatFileIds(FileArchive archive) =>
        Gw2DatTypeIndex.Load(archive, Gw2Installation.DatPath()!).FileIdsOfType("AMAT");
}
