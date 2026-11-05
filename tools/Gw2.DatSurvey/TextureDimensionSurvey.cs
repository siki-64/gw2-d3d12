using Gw2.Formats;
using System.Globalization;
using Gw2.Dat.Content;
using Gw2.Dat.Archive;

namespace Gw2.DatSurvey;

public sealed record TextureDimensionCount(Gw2ShaderResourceDimension Dimension, string Stage, int Declarations, int Materials, IReadOnlyList<uint> ExampleMaterialIds);

public static class TextureDimensionSurvey
{
    private const int ExampleCount = 8;

    public static IReadOnlyList<TextureDimensionCount> Count(IEnumerable<(uint FileId, Gw2AmatMaterial Material)> materials)
    {
        var declarations = new Dictionary<(Gw2ShaderResourceDimension, string), int>();
        var materialIds = new Dictionary<(Gw2ShaderResourceDimension, string), SortedSet<uint>>();
        foreach (var (fileId, material) in materials)
        {
            foreach (var shader in material.Shaders)
            {
                if (ProgramOf(shader) is not { } program) continue;
                var stage = shader.IsPixelShader ? "ps" : "vs";
                foreach (var declaration in program.ResourceDeclarations())
                {
                    var key = (declaration.Dimension, stage);
                    declarations[key] = declarations.GetValueOrDefault(key) + 1;
                    if (!materialIds.TryGetValue(key, out var ids)) materialIds[key] = ids = [];
                    ids.Add(fileId);
                }
            }
        }

        return declarations
            .OrderBy(entry => entry.Key.Item1).ThenBy(entry => entry.Key.Item2, StringComparer.Ordinal)
            .Select(entry => new TextureDimensionCount(entry.Key.Item1, entry.Key.Item2, entry.Value, materialIds[entry.Key].Count, materialIds[entry.Key].Take(ExampleCount).ToList()))
            .ToList();
    }

    public static IEnumerable<(uint FileId, Gw2AmatMaterial Material)> ArchiveMaterials(FileArchive archive, string datPath)
    {
        var source = new Gw2DatContentSource(archive);
        foreach (var fileId in Gw2DatTypeIndex.Load(archive, datPath).FileIdsOfType("AMAT"))
        {
            if (Gw2PackDecoders.Decode<Gw2AmatMaterial>(source.DecodeAsset(fileId)) is { } material) yield return (fileId, material);
        }
    }

    public static string Format(IReadOnlyList<TextureDimensionCount> counts) =>
        string.Concat(counts.Select(count =>
            $"{count.Dimension,-28} {count.Stage}  declarations {count.Declarations,7}  materials {count.Materials,6}  e.g. {string.Join(' ', count.ExampleMaterialIds.Select(id => "0x" + id.ToString("X", CultureInfo.InvariantCulture)))}\n"));

    private static Gw2ShaderProgram? ProgramOf(Gw2AmatShader shader)
    {
        var binary = shader.Binaries.FirstOrDefault(candidate => candidate.Backend == Gw2AmatShaderBackend.Dx11 && candidate.Data.Length > 0);
        if (binary is null || Gw2BgfxShaderBinary.TryParse(binary.Data)?.Container is not { } container) return null;
        return container.TryGetChunk("SHEX", out var programChunk) ? Gw2ShaderProgram.TryParse(programChunk.Data) : null;
    }
}
