using Gw2.Formats;
using System.Numerics;

namespace Gw2.Dat.Content;

public sealed record Gw2MapShoreChain(
    float Offset,
    float Opacity,
    float AnimationSpeed,
    uint Flags,
    IReadOnlyList<Vector2> Points,
    string MaterialFilename,
    IReadOnlyList<string> TextureFilenames,
    float RestTime);

public sealed class Gw2MapShore
{
    public Gw2MapShore(IReadOnlyList<Gw2MapShoreChain> chains)
    {
        Chains = chains;
    }

    public IReadOnlyList<Gw2MapShoreChain> Chains { get; }

    public static Gw2MapShore? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.Chunks
            .Select(chunk => chunk.Root)
            .FirstOrDefault(candidate => candidate is not null && candidate.TypeName == "MapShore");
        if (root is null) return null;

        var chains = new List<Gw2MapShoreChain>();
        if (root.Fields.GetValueOrDefault("chains") is object?[] chainObjects)
            foreach (var chainObject in chainObjects)
                if (chainObject is PackObject chain)
                    chains.Add(new(
                        PackValue.AsF32(chain.Fields.GetValueOrDefault("offset")),
                        PackValue.AsF32(chain.Fields.GetValueOrDefault("opacity")),
                        PackValue.AsF32(chain.Fields.GetValueOrDefault("animationSpeed")),
                        PackValue.AsU32(chain.Fields.GetValueOrDefault("flags")),
                        PackValue.AsVec2List(chain.Fields.GetValueOrDefault("points")),
                        chain.Fields.GetValueOrDefault("materialFilename") as string ?? string.Empty,
                        PackValue.AsStrings(chain.Fields.GetValueOrDefault("textureFilenames")),
                        PackValue.AsF32(chain.Fields.GetValueOrDefault("restTime"))));

        return new(chains);
    }
}
