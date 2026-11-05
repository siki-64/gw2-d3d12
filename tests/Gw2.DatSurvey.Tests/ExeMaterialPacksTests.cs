using Gw2.Formats;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.DatSurvey.Tests;

[TestClass]
public sealed class ExeMaterialPacksTests
{
    private const int EmbeddedMaterialCount = 191;
    private const int BgfxMaterialCount = 186;
    private const int Dx9MaterialCount = 5;
    private const int CascadeShadowMaterialOffset = 0x1D35FE0;

    [TestMethod]
    public void ReadsEveryEmbeddedMaterialPack()
    {
        var materials = ExeMaterialPacks.ReadMaterials(File.ReadAllBytes(ExePath()));

        Assert.AreEqual(EmbeddedMaterialCount, materials.Count);
        Assert.AreEqual(BgfxMaterialCount, materials.Count(pack => Gw2PackDecoders.Decode<Gw2AmatMaterial>(pack.Document) is not null));
        Assert.AreEqual(Dx9MaterialCount, materials.Count(pack => Gw2AmatDx9Material.TryParse(pack.Document) is not null));
    }

    [TestMethod]
    public void CascadeShadowMatricesAreConstantUniforms()
    {
        var pack = ExeMaterialPacks.ReadMaterials(File.ReadAllBytes(ExePath())).Single(candidate => candidate.FileOffset == CascadeShadowMaterialOffset);
        var material = Gw2PackDecoders.Decode<Gw2AmatMaterial>(pack.Document)!;
        var pixelShader = material.Shaders.First(shader => shader.IsPixelShader).Binaries.First(binary => binary.Backend == Gw2AmatShaderBackend.Dx11);
        var uniform = Gw2BgfxShaderBinary.TryParse(pixelShader.Data)!.Uniforms.Single(candidate => candidate.Name == "ShadowMapCascadeWorldToShadow");

        Assert.AreEqual(0x14, uniform.Type);
        Assert.AreEqual(Gw2ShaderReflectionKind.Constant, uniform.Kind);
    }

    [TestMethod]
    [TestCategory("Corpus")]
    public void EveryEmbeddedBgfxShaderDecompilesAndCompilesWithDxc()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("The DXC tool package ships Windows binaries here.");
        var materials = ExeMaterialPacks.ReadMaterials(File.ReadAllBytes(ExePath()));
        var directory = Directory.CreateTempSubdirectory("gw2-research-dxc-");
        try
        {
            var (compiled, failed) = DecompiledShaderBuild.ConvertExeMaterials(materials, directory.FullName);
            Assert.AreEqual(0, failed, string.Join("\n", directory.EnumerateFiles("*.errors").Take(10).Select(file => $"{file.Name}: {File.ReadAllText(file.FullName).Split('\n')[0]}")));
            Assert.IsTrue(compiled > 0);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static string ExePath()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        var exe = Path.ChangeExtension(dat, ".exe");
        if (!File.Exists(exe)) Assert.Inconclusive($"No exe at {exe}.");
        return exe;
    }
}
