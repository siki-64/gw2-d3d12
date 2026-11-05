using Gw2.Formats;
using Gw2.Dat.Content;
using Gw2.Dat.Archive;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Gw2.Dat.Shaders;

namespace Gw2.DatSurvey.Tests;

[TestClass]
public sealed class ShaderDxcCompileTests
{
    private const uint WaterMaterialFileId = 187551;
    private const uint TerrainMaterialFileId = 0x2D29C;
    private const uint CloudSkyMaterialFileId = 3135800;
    private const uint CanonicalPropMaterialFileId = 0x1130E;
    private const uint LeafMaterialFileId = 0xE106;

    [TestMethod]
    [DataRow(WaterMaterialFileId)]
    [DataRow(TerrainMaterialFileId)]
    [DataRow(CloudSkyMaterialFileId)]
    [DataRow(CanonicalPropMaterialFileId)]
    [DataRow(LeafMaterialFileId)]
    public void DecompiledHlslCompilesWithDxc(uint materialFileId)
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("The DXC package ships Windows binaries.");
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        using var archive = FileArchive.Open(dat, verifyChecksums: false);
        var material = Gw2PackDecoders.Decode<Gw2AmatMaterial>(new Gw2DatContentSource(archive).DecodeAsset(materialFileId))!;

        var directory = Directory.CreateTempSubdirectory("gw2-dxc-");
        try
        {
            var failures = new List<string>();
            for (var index = 0; index < material.Shaders.Count; index++)
            {
                var binary = material.Shaders[index].Binaries.FirstOrDefault(candidate => candidate.Backend == Gw2AmatShaderBackend.Dx11 && candidate.Data.Length > 0);
                if (binary is null) continue;
                var shader = Gw2BgfxShaderBinary.TryParse(binary.Data)!;
                var source = Path.Combine(directory.FullName, $"shader{index}.hlsl");
                File.WriteAllText(source, ShaderDecompiler.Decompile(shader, ShaderCompilerTarget.Dxc));
                if (DecompiledShaderBuild.CompileToDxil(source, material.Shaders[index].IsPixelShader, source + ".dxil") is { } errors) failures.Add($"shader {index}: {errors.Split('\n').FirstOrDefault(line => line.Contains("error", StringComparison.Ordinal))}");
            }

            Assert.AreEqual(0, failures.Count, string.Join("\n", failures.Take(10)));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
