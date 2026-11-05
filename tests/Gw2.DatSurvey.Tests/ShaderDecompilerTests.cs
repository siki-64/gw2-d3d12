using Gw2.Formats;
using Gw2.Dat.Content;
using Gw2.Dat.Archive;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Gw2.Dat.Shaders;

namespace Gw2.DatSurvey.Tests;

[TestClass]
public sealed class ShaderDecompilerTests
{
    private const uint WaterMaterialFileId = 187551;
    private const uint CanonicalPropMaterialFileId = 0x1130E;
    private const uint HumanFemaleBodyMaterialFileId = 0x8585;
    private const uint UniformNamedMinMaterialFileId = 0xDB7A;
    private const uint UniformNamedMaxMaterialFileId = 0x35003;
    private const uint TerrainMaterialFileId = 0x2D29C;
    private const uint CloudSkyMaterialFileId = 3135800;
    private const uint LeafMaterialFileId = 0xE106;

    [TestMethod]
    [TestCategory("Corpus")]
    public void EveryArchiveShaderDecompilesAndCompiles()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("D3DCompile needs Windows.");
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        using var archive = FileArchive.Open(dat, verifyChecksums: false);
        var source = new Gw2DatContentSource(archive);
        var seen = new HashSet<string>();
        var failures = new Dictionary<string, (int Count, string Example)>();
        var shaders = 0;
        foreach (var fileId in Gw2DatTypeIndex.Load(archive, dat).FileIdsOfType("AMAT"))
        {
            if (Gw2PackDecoders.Decode<Gw2AmatMaterial>(source.DecodeAsset(fileId)) is not { } material) continue;
            for (var index = 0; index < material.Shaders.Count; index++)
            {
                var binary = material.Shaders[index].Binaries.FirstOrDefault(candidate => candidate.Backend == Gw2AmatShaderBackend.Dx11 && candidate.Data.Length > 0);
                if (binary is null || !seen.Add(Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(binary.Data.Span)))) continue;
                shaders++;
                string reason;
                try
                {
                    var hlsl = ShaderDecompiler.Decompile(Gw2BgfxShaderBinary.TryParse(binary.Data)!);
                    if (ShaderCompiler.CompileErrors(hlsl, material.Shaders[index].IsPixelShader ? "ps_5_0" : "vs_5_0") is not { } errors) continue;
                    var line = errors.Split('\n')[0];
                    reason = "compile: " + line[(line.IndexOf("): ", StringComparison.Ordinal) + 3)..];
                }
                catch (Exception exception) when (exception is NotSupportedException or InvalidDataException or IndexOutOfRangeException or ArgumentOutOfRangeException)
                {
                    reason = $"{exception.GetType().Name}: {exception.Message}";
                }

                failures[reason] = failures.TryGetValue(reason, out var entry) ? (entry.Count + 1, entry.Example) : (1, $"0x{fileId:X} shader {index}");
            }
        }

        var report = string.Join("\n", failures.OrderByDescending(pair => pair.Value.Count).Select(pair => $"{pair.Value.Count} x {pair.Key} (e.g. {pair.Value.Example})"));
        Console.WriteLine($"{shaders} unique shaders\n{report}");
        Assert.AreEqual(0, failures.Count, report);
    }

    [TestMethod]
    [DataRow(WaterMaterialFileId)]
    [DataRow(CanonicalPropMaterialFileId)]
    [DataRow(HumanFemaleBodyMaterialFileId)]
    [DataRow(UniformNamedMinMaterialFileId)]
    [DataRow(UniformNamedMaxMaterialFileId)]
    public void DecompiledHlslCompilesForEveryShaderOfTheMaterial(uint materialFileId)
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("D3DCompile needs Windows.");
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        using var archive = FileArchive.Open(dat, verifyChecksums: false);
        var material = Gw2PackDecoders.Decode<Gw2AmatMaterial>(new Gw2DatContentSource(archive).DecodeAsset(materialFileId))!;

        var failures = new List<string>();
        for (var index = 0; index < material.Shaders.Count; index++)
        {
            var binary = material.Shaders[index].Binaries.FirstOrDefault(candidate => candidate.Backend == Gw2AmatShaderBackend.Dx11 && candidate.Data.Length > 0);
            if (binary is null) continue;
            var shader = Gw2BgfxShaderBinary.TryParse(binary.Data)!;
            var target = material.Shaders[index].IsPixelShader ? "ps_5_0" : "vs_5_0";
            string hlsl;
            try
            {
                hlsl = ShaderDecompiler.Decompile(shader);
            }
            catch (NotSupportedException exception)
            {
                failures.Add($"shader {index}: {exception.Message}");
                continue;
            }

            if (ShaderCompiler.CompileErrors(hlsl, target) is { } errors) failures.Add($"shader {index}: {errors.Split('\n')[0]}");
        }

        Assert.AreEqual(0, failures.Count, string.Join("\n", failures.Take(10)));
    }

    [TestMethod]
    [DataRow(WaterMaterialFileId)]
    [DataRow(TerrainMaterialFileId)]
    [DataRow(CloudSkyMaterialFileId)]
    [DataRow(CanonicalPropMaterialFileId)]
    [DataRow(LeafMaterialFileId)]
    public void RecompiledHlslIsComparedInstructionByInstructionWithTheArchiveBytecode(uint materialFileId)
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("D3DCompile needs Windows.");
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        using var archive = FileArchive.Open(dat, verifyChecksums: false);
        var material = Gw2PackDecoders.Decode<Gw2AmatMaterial>(new Gw2DatContentSource(archive).DecodeAsset(materialFileId))!;

        for (var index = 0; index < material.Shaders.Count; index++)
        {
            var binary = material.Shaders[index].Binaries.FirstOrDefault(candidate => candidate.Backend == Gw2AmatShaderBackend.Dx11 && candidate.Data.Length > 0);
            if (binary is null) continue;
            var shader = Gw2BgfxShaderBinary.TryParse(binary.Data)!;
            var original = ShaderBytecodeComparison.ShexOf(shader.Code);
            Assert.IsTrue(ShaderBytecodeComparison.Compare(original, original).Strict.Identical);

            var compiled = ShaderCompiler.Compile(ShaderDecompiler.Decompile(shader), material.Shaders[index].IsPixelShader ? "ps_5_0" : "vs_5_0", ShaderCompiler.FlagsMatching(original));
            Assert.IsNotNull(compiled.Code, compiled.Errors);
            var match = ShaderBytecodeComparison.Compare(original, ShaderBytecodeComparison.ShexOf(compiled.Code));
            Console.WriteLine($"shader {index}: {Describe(match.Strict)} strict, {Describe(match.Canonical)} canonical");
            Assert.AreEqual(match.Strict.OriginalInstructions, match.Strict.RecompiledInstructions, 2, $"shader {index} instruction count");
        }
    }

    private static string Describe(ShaderInstructionMatch match) =>
        $"{match.CommonInstructions}/{match.OriginalInstructions} ({match.Similarity:P1}{(match.Identical ? ", identical" : string.Empty)})";
}
