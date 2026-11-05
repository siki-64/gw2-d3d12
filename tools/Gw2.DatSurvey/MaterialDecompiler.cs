using Gw2.Dat.Archive;
using Gw2.Dat.Content;
using Gw2.Dat.Shaders;

namespace Gw2.DatSurvey;

public static class MaterialDecompiler
{
    public static IReadOnlyList<ShaderListing> DecompileMaterial(IGw2ContentSource source, uint materialFileId)
    {
        ArgumentNullException.ThrowIfNull(source);
        var material = Gw2PackDecoders.Decode<Gw2AmatMaterial>(source.DecodeAsset(materialFileId))
            ?? throw new InvalidDataException($"File {materialFileId} is not a material");
        var listings = new List<ShaderListing>();
        for (var index = 0; index < material.Shaders.Count; index++)
        {
            var binary = material.Shaders[index].Binaries.FirstOrDefault(candidate => candidate.Backend == Gw2AmatShaderBackend.Dx11 && candidate.Data.Length > 0);
            if (binary is null || Gw2BgfxShaderBinary.TryParse(binary.Data) is not { } shader) continue;
            listings.Add(new(index, material.Shaders[index].IsPixelShader ? "ps" : "vs", ShaderDecompiler.Decompile(shader)));
        }

        return listings;
    }
}
