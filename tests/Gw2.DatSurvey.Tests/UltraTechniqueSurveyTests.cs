using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Gw2.Dat.Archive;
using Gw2.Dat.Content;
using Gw2.Dat.Shaders;
using Gw2.Formats;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.DatSurvey.Tests;

[TestClass]
public sealed class UltraTechniqueSurveyTests
{
    private const string ReportEnvironmentVariable = "GW2_ULTRA_REPORT";

    [TestMethod]
    public void UltraShadersDecompileAndCompileNextToTheirHighCounterparts()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("The DXC package ships Windows binaries.");
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        using var archive = FileArchive.Open(dat, verifyChecksums: false);
        var source = new Gw2DatContentSource(archive);
        var index = Gw2DatTypeIndex.Load(archive, dat);

        var report = new StringBuilder();
        var failures = new List<string>();
        var directory = Directory.CreateTempSubdirectory("gw2-ultra-");
        var materialsWithUltra = 0;
        var pairedEffects = 0;
        var differingShaders = 0;
        var identicalShaders = 0;
        try
        {
            foreach (var fileId in index.FileIdsOfType("AMAT").Distinct().Order())
            {
                Gw2AmatMaterial? material;
                try { material = Gw2PackDecoders.Decode<Gw2AmatMaterial>(source.DecodeAsset(fileId)); }
                catch (Exception) { continue; }
                if (material?.Techniques.FirstOrDefault(technique => Gw2TechniqueQuality.SelectionQualityOf(technique.Quality) == Gw2ShaderQuality.Ultra) is not { } ultra) continue;
                materialsWithUltra++;
                var high = material.TechniqueFor(Gw2ShaderQuality.High);
                report.AppendLine($"## {fileId} techniques [{string.Join(", ", material.Techniques.Select(technique => technique.QualityName))}]");
                foreach (var (ultraShader, highShader, label) in PairedShaders(ultra, high))
                {
                    pairedEffects++;
                    if (highShader == ultraShader)
                    {
                        identicalShaders++;
                        continue;
                    }
                    var ultraSource = Decompile(material, ultraShader);
                    var highSource = highShader is { } highIndex ? Decompile(material, highIndex) : null;
                    if (ultraSource is null) continue;
                    if (highSource == ultraSource)
                    {
                        identicalShaders++;
                        continue;
                    }
                    differingShaders++;
                    var path = Path.Combine(directory.FullName, $"{fileId}_{ultraShader}.hlsl");
                    File.WriteAllText(path, ultraSource);
                    var errors = DecompiledShaderBuild.CompileToDxil(path, material.Shaders[(int)ultraShader].IsPixelShader, path + ".dxil");
                    if (errors is not null) failures.Add($"{fileId} shader {ultraShader}: {errors.Split('\n').FirstOrDefault(line => line.Contains("error", StringComparison.Ordinal))}");
                    report.AppendLine($"- {label}: ultra shader {ultraShader} vs high shader {highShader?.ToString() ?? "none"}, {(errors is null ? "compiles" : "FAILS")}");
                    report.AppendLine($"  metrics {(material.Shaders[(int)ultraShader].IsPixelShader ? "pixel" : "vertex")} {Metrics(highSource ?? "")} -> {Metrics(ultraSource)}");
                    report.AppendLine("```diff");
                    report.AppendLine(LineDiff(highSource ?? "", ultraSource));
                    report.AppendLine("```");
                }
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }

        report.Insert(0, $"materials with ultra: {materialsWithUltra}, paired shaders: {pairedEffects}, identical to high: {identicalShaders}, differing: {differingShaders}, compile failures: {failures.Count}\n\n");
        if (Environment.GetEnvironmentVariable(ReportEnvironmentVariable) is { Length: > 0 } reportPath) File.WriteAllText(reportPath, report.ToString());
        Console.WriteLine(report.ToString().Split('\n')[0]);
        Assert.AreNotEqual(0, materialsWithUltra);
        Assert.AreEqual(0, failures.Count, string.Join("\n", failures.Take(10)));
    }

    private static IEnumerable<(uint Ultra, uint? High, string Label)> PairedShaders(Gw2AmatTechnique ultra, Gw2AmatTechnique? high)
    {
        for (var pass = 0; pass < ultra.Passes.Count; pass++)
            foreach (var effect in ultra.Passes[pass].Effects)
            {
                var highEffect = high is not null && pass < high.Passes.Count ? high.Passes[pass].Effects.FirstOrDefault(candidate => candidate.Token == effect.Token) : null;
                yield return (effect.PixelShaderIndex, highEffect?.PixelShaderIndex, $"pass {pass} effect 0x{effect.Token:X} pixel");
                foreach (var variant in effect.VertexShaderVariants)
                    yield return (variant.VertexShaderIndex, highEffect?.VertexShaderVariantFor(variant.Variant)?.VertexShaderIndex, $"pass {pass} effect 0x{effect.Token:X} vertex variant {variant.Variant}");
            }
    }

    private static string? Decompile(Gw2AmatMaterial material, uint shaderIndex)
    {
        if (shaderIndex >= material.Shaders.Count) return null;
        var binary = material.Shaders[(int)shaderIndex].Binaries.FirstOrDefault(candidate => candidate.Backend == Gw2AmatShaderBackend.Dx11 && candidate.Data.Length > 0);
        if (binary is null || Gw2BgfxShaderBinary.TryParse(binary.Data) is not { } shader) return null;
        return ShaderDecompiler.Decompile(shader, ShaderCompilerTarget.Dxc);
    }

    private static string Metrics(string hlsl)
    {
        var lines = hlsl.Split('\n');
        var samples = lines.Sum(line => CountOf(line, ".Sample"));
        var textures = lines.Where(line => line.Contains("Texture2D", StringComparison.Ordinal) || line.Contains("TextureCube", StringComparison.Ordinal) || line.Contains("Texture3D", StringComparison.Ordinal)).Select(line => line.Trim()).Distinct().Count();
        var interpolators = lines.Where(line => line.Contains(" : TEXCOORD", StringComparison.Ordinal)).Select(line => line.Trim()).Distinct().Count();
        var constants = lines.Count(line => line.Contains(": packoffset(", StringComparison.Ordinal));
        return $"lines={lines.Length} samples={samples} textures={textures} interpolators={interpolators} constants={constants}";
    }

    private static int CountOf(string text, string part)
    {
        var count = 0;
        for (var at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal)) count++;
        return count;
    }

    private static string LineDiff(string before, string after)
    {
        var beforeLines = before.Split('\n').Select(line => line.TrimEnd()).ToList();
        var afterLines = after.Split('\n').Select(line => line.TrimEnd()).ToList();
        var removed = beforeLines.Except(afterLines).Select(line => "- " + line);
        var added = afterLines.Except(beforeLines).Select(line => "+ " + line);
        return string.Join("\n", removed.Concat(added));
    }
}
