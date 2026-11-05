using Gw2.Formats;
using System.Numerics;

namespace Gw2.Dat.Content;

public sealed record Gw2MapOcclusionVolume(uint Token, uint Flags, IReadOnlyList<Vector3> Vertices, string Name);

public sealed class Gw2MapOcclusions
{
    public Gw2MapOcclusions(IReadOnlyList<Gw2MapOcclusionVolume> volumes)
    {
        Volumes = volumes;
    }

    public IReadOnlyList<Gw2MapOcclusionVolume> Volumes { get; }

    public static Gw2MapOcclusions? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.Chunks
            .Select(chunk => chunk.Root)
            .FirstOrDefault(candidate => candidate is not null && candidate.TypeName == "MapOcclusions");
        if (root is null) return null;

        var volumes = new List<Gw2MapOcclusionVolume>();
        if (root.Fields.GetValueOrDefault("Occlusions") is object?[] occlusionObjects)
            foreach (var occlusionObject in occlusionObjects)
                if (occlusionObject is PackObject occlusion)
                    volumes.Add(new(
                        PackValue.AsU32(occlusion.Fields.GetValueOrDefault("token")),
                        PackValue.AsU32(occlusion.Fields.GetValueOrDefault("flags")),
                        PackValue.AsVec3List(occlusion.Fields.GetValueOrDefault("vertices")),
                        occlusion.Fields.GetValueOrDefault("name") as string ?? string.Empty));

        return new(volumes);
    }
}
