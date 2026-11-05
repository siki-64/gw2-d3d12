using Gw2.Formats;
using System.Diagnostics;
using System.Globalization;
using Gw2.Dat.Content;
using Gw2.Dat.Archive;
using Gw2.Dat.Shaders;

namespace Gw2.DatSurvey;

public static class DecompiledShaderBuild
{
    public static string? CompileToDxil(string sourcePath, bool isPixelShader, string outputPath)
    {
        var profile = isPixelShader ? "ps_6_0" : "vs_6_0";
        var startInfo = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "dxc", "dxc.exe"))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in new[] { "/nologo", "/T", profile, "/E", "main", "/O3", "/Zpc", "/Fo", outputPath, sourcePath }) startInfo.ArgumentList.Add(argument);
        using var dxc = Process.Start(startInfo)!;
        var output = dxc.StandardOutput.ReadToEndAsync();
        var errors = dxc.StandardError.ReadToEnd();
        output.Wait();
        dxc.WaitForExit();
        return dxc.ExitCode == 0 ? null : errors;
    }

    public static (int Compiled, int Failed, int NotFound) CompileMissing(FileArchive archive, string datPath, string directory)
    {
        var wanted = Directory.EnumerateFiles(directory, "*.missing")
            .Select(path => uint.Parse(Path.GetFileNameWithoutExtension(path), NumberStyles.HexNumber, CultureInfo.InvariantCulture))
            .ToHashSet();
        var source = new Gw2DatContentSource(archive);
        var compiled = 0;
        var failed = 0;
        foreach (var fileId in Gw2DatTypeIndex.Load(archive, datPath).FileIdsOfType("AMAT"))
        {
            if (wanted.Count == 0) break;
            if (Gw2PackDecoders.Decode<Gw2AmatMaterial>(source.DecodeAsset(fileId)) is not { } material) continue;
            for (var index = 0; index < material.Shaders.Count; index++)
            {
                var binary = material.Shaders[index].Binaries.FirstOrDefault(candidate => candidate.Backend == Gw2AmatShaderBackend.Dx11 && candidate.Data.Length > 0);
                if (binary is null || Gw2BgfxShaderBinary.TryParse(binary.Data) is not { } shader || !wanted.Remove(shader.CodeHash())) continue;
                var stem = Path.Combine(directory, shader.CodeHash().ToString("X8", CultureInfo.InvariantCulture));
                File.WriteAllText(stem + ".hlsl", ShaderDecompiler.Decompile(shader, ShaderCompilerTarget.Dxc));
                if (CompileToDxil(stem + ".hlsl", material.Shaders[index].IsPixelShader, stem + ".dxil") is { } errors)
                {
                    File.WriteAllText(stem + ".errors", errors);
                    failed++;
                    continue;
                }

                File.Delete(stem + ".missing");
                compiled++;
            }
        }

        return (compiled, failed, wanted.Count);
    }

    public static (int Compiled, int Failed) ConvertExeMaterials(IEnumerable<ExeEmbeddedPack> materials, string directory)
    {
        Directory.CreateDirectory(directory);
        var compiled = 0;
        var failed = 0;
        foreach (var pack in materials)
        {
            if (Gw2PackDecoders.Decode<Gw2AmatMaterial>(pack.Document) is not { } material) continue;
            for (var index = 0; index < material.Shaders.Count; index++)
            {
                var binary = material.Shaders[index].Binaries.FirstOrDefault(candidate => candidate.Backend == Gw2AmatShaderBackend.Dx11 && candidate.Data.Length > 0);
                if (binary is null || Gw2BgfxShaderBinary.TryParse(binary.Data) is not { } shader) continue;
                var stem = Path.Combine(directory, $"exe_{pack.FileOffset:X8}_{(material.Shaders[index].IsPixelShader ? "ps" : "vs")}{index}");
                string? errors;
                try
                {
                    File.WriteAllText(stem + ".hlsl", ShaderDecompiler.Decompile(shader, ShaderCompilerTarget.Dxc));
                    errors = CompileToDxil(stem + ".hlsl", material.Shaders[index].IsPixelShader, stem + ".dxil");
                }
                catch (NotSupportedException exception)
                {
                    errors = $"decompile: {exception.Message}";
                }

                if (errors is not null)
                {
                    File.WriteAllText(stem + ".errors", errors);
                    failed++;
                    continue;
                }

                File.Delete(stem + ".errors");
                compiled++;
            }
        }

        return (compiled, failed);
    }
}
