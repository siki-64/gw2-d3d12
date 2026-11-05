using Gw2.Formats;
using Gw2.Dat.Content;
using Gw2.Dat.Archive;

namespace Gw2.DatSurvey;

internal static class Program
{
    private const string Usage = """
        Gw2.DatSurvey - survey and inspect a GW2 archive for reverse engineering.

        Usage:
          Gw2.DatSurvey --dat <gw2.dat> [options]

        Options:
          --dat <path>           GW2 archive to read. Required.
          --out <dir>            Output directory for shader listings.
                                 Defaults to <repo>/artifacts/dat-survey.
          --no-verify            Skip archive CRC verification.

          --survey <dir>         Decode every MFT entry and write entries.json, types.md, fields.md and summary.md.
          --key-coverage <path>  Write encrypted strings against the captured text keys as markdown.
          --pack-cache           Write the decoded content packs next to the archive; later runs load them instead of decoding.
          --shader <id>          Disassemble a material's (AMAT) Dx11 shaders by content file ID.
                                 Repeatable. Writes .asm listings under <out>/shaders.
          --hlsl                 With --shader, also write decompiled .hlsl per shader and a .fx per material.
          --compile-decompiled <dir>
                                 For each <hash>.missing the client wrote (--decompiled-shaders), decompile that shader
                                 and compile it with DXC (Shader Model 6.0) into <hash>.dxil.
          --list-type <fourcc>   Print the content file IDs of one type (for example AMAT, MODL, ATEX) from the
                                 type index next to the archive; the first run builds it.
          --shader-file <path>   Disassemble a material blob saved to a file (for example one embedded in the exe).
                                 Repeatable. Needs no --dat.
          --exe-materials <exe>  Read the AMAT packs embedded in the exe, decompile their Dx11 shaders and compile them
                                 with DXC (Shader Model 6.0) into <out>/exe-materials. Needs no --dat.

          --texture-dimensions   Count the texture dimensions every material shader declares, per stage, in the archive
                                 (--dat) and in the exe (--exe-materials).

          --help                Show this help.
        """;

    private static int Main(string[] args)
    {
        string? datPath = null;
        string? surveyPath = null;
        string? keyCoveragePath = null;
        string? compileDecompiledPath = null;
        var writePackCache = false;
        var outputDirectory = DefaultOutputDirectory();
        var shaderIds = new List<uint>();
        var shaderFiles = new List<string>();
        string? listType = null;
        string? exeMaterialsPath = null;
        var verify = true;
        var writeHlsl = false;
        var surveyTextureDimensions = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--dat":
                    if (!Take(args, ref i, out datPath)) return UsageError("--dat requires a path");
                    break;
                case "--out":
                    if (!Take(args, ref i, out var output)) return UsageError("--out requires a directory");
                    outputDirectory = output;
                    break;
                case "--survey":
                    if (!Take(args, ref i, out surveyPath)) return UsageError("--survey requires a directory");
                    break;
                case "--key-coverage":
                    if (!Take(args, ref i, out keyCoveragePath)) return UsageError("--key-coverage requires a path");
                    break;
                case "--compile-decompiled":
                    if (!Take(args, ref i, out compileDecompiledPath)) return UsageError("--compile-decompiled requires a directory");
                    break;
                case "--hlsl":
                    writeHlsl = true;
                    break;
                case "--pack-cache":
                    writePackCache = true;
                    break;
                case "--shader":
                    if (!Take(args, ref i, out var shader) || !TryParseContentId(shader, out var parsedShader))
                        return UsageError("--shader requires a decimal or 0x-prefixed content ID");
                    shaderIds.Add(parsedShader);
                    break;
                case "--shader-file":
                    if (!Take(args, ref i, out var shaderFile)) return UsageError("--shader-file requires a path");
                    shaderFiles.Add(shaderFile);
                    break;
                case "--list-type":
                    if (!Take(args, ref i, out listType) || listType.Length != 4) return UsageError("--list-type requires a four-character type");
                    break;
                case "--exe-materials":
                    if (!Take(args, ref i, out exeMaterialsPath)) return UsageError("--exe-materials requires a path");
                    break;
                case "--texture-dimensions":
                    surveyTextureDimensions = true;
                    break;
                case "--no-verify":
                    verify = false;
                    break;
                case "--help" or "-h":
                    Console.WriteLine(Usage);
                    return 0;
                default:
                    return UsageError($"unknown argument '{args[i]}'");
            }
        }

        if (surveyTextureDimensions) return SurveyTextureDimensions(datPath, exeMaterialsPath);

        if (exeMaterialsPath is not null)
        {
            var materials = ExeMaterialPacks.ReadMaterials(File.ReadAllBytes(exeMaterialsPath));
            var directory = Path.Combine(outputDirectory, "exe-materials");
            var (compiled, failed) = DecompiledShaderBuild.ConvertExeMaterials(materials, directory);
            Console.WriteLine($"{materials.Count} material(s): compiled {compiled}, failed {failed} -> {Path.GetFullPath(directory)}");
            return failed == 0 ? 0 : 1;
        }

        if (shaderFiles.Count > 0) return DisassembleShaderFiles(shaderFiles, outputDirectory);
        if (datPath is null) return UsageError("--dat is required");
        if (!File.Exists(datPath)) return UsageError($"archive '{datPath}' does not exist");

        if (listType is not null)
        {
            using var archive = FileArchive.Open(datPath, verifyChecksums: false);
            foreach (var fileId in Gw2DatTypeIndex.Load(archive, datPath).FileIdsOfType(listType))
                Console.WriteLine($"0x{fileId:X}");
            return 0;
        }

        if (surveyPath is not null)
        {
            var run = CoverageSurvey.Run(datPath, verify, Gw2PackSchemaCatalog.Default, Gw2PackDecoders.All, Environment.ProcessorCount, Console.Error);
            CoverageReport.Write(surveyPath, run, Gw2PackSchemaCatalog.Default);
            Console.WriteLine($"surveyed {run.Entries.Count} record(s) -> {Path.GetFullPath(surveyPath)}");
            return 0;
        }

        if (writePackCache)
        {
            using var reader = Gw2ContentReader.Open(datPath, verify);
            var path = reader.WritePackCache();
            Console.WriteLine($"wrote pack cache ({new FileInfo(path).Length / (1024 * 1024)} MB) -> {Path.GetFullPath(path)}");
            return 0;
        }

        if (compileDecompiledPath is not null)
        {
            using var archive = FileArchive.Open(datPath, verify);
            var (compiled, failed, notFound) = DecompiledShaderBuild.CompileMissing(archive, datPath, compileDecompiledPath);
            Console.WriteLine($"compiled {compiled}, failed {failed}, not found {notFound} -> {Path.GetFullPath(compileDecompiledPath)}");
            return failed == 0 && notFound == 0 ? 0 : 1;
        }

        if (keyCoveragePath is not null)
        {
            using var reader = Gw2ContentReader.Open(datPath, verify);
            var coverage = Gw2TextKeyCoverage.Measure(
                reader.TextManifest,
                fileId => reader.Contains(fileId) ? reader.ReadBytes(fileId) : null,
                (Environment.GetEnvironmentVariable("GW2_KEYS") is { Length: > 0 } keysPath ? Gw2KeyStore.Load(keysPath) : Gw2KeyStore.Empty()));
            KeyCoverageReport.Write(keyCoveragePath, coverage);
            Console.WriteLine($"key coverage -> {Path.GetFullPath(keyCoveragePath)}");
            return 0;
        }

        if (shaderIds.Count == 0) return UsageError("nothing to do");

        try
        {
            using var archive = FileArchive.Open(datPath, verify);
            var contentSource = new Gw2DatContentSource(archive);
            var shadersDirectory = Path.Combine(outputDirectory, "shaders");
            Directory.CreateDirectory(shadersDirectory);
            var written = 0;
            foreach (var shaderId in shaderIds.Distinct())
            {
                if (!archive.FileIndex.ContainsKey(shaderId))
                {
                    Console.Error.WriteLine($"skip: 0x{shaderId:X8} is not in the archive");
                    continue;
                }

                IReadOnlyList<ShaderListing> listings;
                try
                {
                    listings = ShaderDisassembler.Disassemble(contentSource, shaderId);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"fail: 0x{shaderId:X8}: {ex.Message}");
                    continue;
                }

                foreach (var listing in listings)
                {
                    var file = Path.Combine(shadersDirectory, $"amat_{shaderId:X8}_{listing.Stage}{listing.ShaderIndex}.asm");
                    File.WriteAllText(file, listing.Text);
                    written++;
                }

                if (writeHlsl && Gw2PackDecoders.Decode<Gw2AmatMaterial>(contentSource.DecodeAsset(shaderId)) is { } material)
                    foreach (var file in ShaderEffectSource.Write(material, $"amat_{shaderId:X8}"))
                        File.WriteAllText(Path.Combine(shadersDirectory, file.FileName), file.Text);
            }

            Console.WriteLine($"disassembled {written} shader(s) -> {Path.GetFullPath(shadersDirectory)}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static int DisassembleShaderFiles(IEnumerable<string> paths, string outputDirectory)
    {
        var shadersDirectory = Path.Combine(outputDirectory, "shaders");
        Directory.CreateDirectory(shadersDirectory);
        var written = 0;
        foreach (var path in paths)
        {
            var document = Gw2AssetDocument.Parse(File.ReadAllBytes(path), Gw2PackSchemaCatalog.Default);
            foreach (var listing in ShaderDisassembler.Disassemble(document, path))
            {
                File.WriteAllText(Path.Combine(shadersDirectory, $"{Path.GetFileNameWithoutExtension(path)}_{listing.Stage}{listing.ShaderIndex}.asm"), listing.Text);
                written++;
            }
        }

        Console.WriteLine($"disassembled {written} shader(s) -> {Path.GetFullPath(shadersDirectory)}");
        return 0;
    }

    private static bool Take(string[] args, ref int index, out string value)
    {
        if (index + 1 >= args.Length)
        {
            value = string.Empty;
            return false;
        }

        value = args[++index];
        return true;
    }

    private static bool TryParseContentId(string text, out uint value)
    {
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return uint.TryParse(text.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out value);
        return uint.TryParse(text, out value);
    }

    private static string DefaultOutputDirectory()
    {
        for (var directory = new DirectoryInfo(Environment.CurrentDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "gw2.slnx")))
                return Path.Combine(directory.FullName, "artifacts", "dat-survey");

        return Path.Combine(Environment.CurrentDirectory, "artifacts", "dat-survey");
    }

    private static int SurveyTextureDimensions(string? datPath, string? exePath)
    {
        if (datPath is null && exePath is null) return UsageError("--texture-dimensions needs --dat or --exe-materials");
        if (datPath is not null)
        {
            using var archive = FileArchive.Open(datPath, verifyChecksums: false);
            Console.WriteLine($"archive {datPath}");
            Console.Write(TextureDimensionSurvey.Format(TextureDimensionSurvey.Count(TextureDimensionSurvey.ArchiveMaterials(archive, datPath))));
        }

        if (exePath is not null)
        {
            var materials = ExeMaterialPacks.ReadMaterials(File.ReadAllBytes(exePath))
                .Select(pack => (FileId: (uint)pack.FileOffset, Material: Gw2PackDecoders.Decode<Gw2AmatMaterial>(pack.Document)))
                .Where(entry => entry.Material is not null)
                .Select(entry => (entry.FileId, entry.Material!));
            Console.WriteLine($"exe {exePath} (ids are file offsets)");
            Console.Write(TextureDimensionSurvey.Format(TextureDimensionSurvey.Count(materials)));
        }

        return 0;
    }

    private static int UsageError(string message)
    {
        Console.Error.WriteLine($"error: {message}");
        Console.Error.WriteLine();
        Console.Error.WriteLine(Usage);
        return 2;
    }
}
