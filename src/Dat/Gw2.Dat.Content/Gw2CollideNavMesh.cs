using Gw2.Formats;
using System.Numerics;

namespace Gw2.Dat.Content;

public sealed record Gw2CollideNavMeshChunkRef(Vector3 BoundsMin, Vector3 BoundsMax, string ChunkFilename);

public sealed class Gw2CollideNavMesh
{
    public Gw2CollideNavMesh(Vector3 boundsMin, Vector3 boundsMax, IReadOnlyList<uint> chunkDims, IReadOnlyList<Gw2CollideNavMeshChunkRef> chunks)
    {
        BoundsMin = boundsMin;
        BoundsMax = boundsMax;
        ChunkDims = chunkDims;
        Chunks = chunks;
    }

    public Vector3 BoundsMin { get; }
    public Vector3 BoundsMax { get; }
    public IReadOnlyList<uint> ChunkDims { get; }
    public IReadOnlyList<Gw2CollideNavMeshChunkRef> Chunks { get; }

    public static Gw2CollideNavMesh? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.Chunks
            .Select(chunk => chunk.Root)
            .FirstOrDefault(candidate => candidate is not null && candidate.TypeName == "CollideNavMesh");
        if (root is null) return null;

        var chunks = new List<Gw2CollideNavMeshChunkRef>();
        if (root.Fields.GetValueOrDefault("chunkRefArray") is object?[] chunkObjects)
            foreach (var chunkObject in chunkObjects)
                if (chunkObject is PackObject chunk)
                    chunks.Add(new(
                        Vec3(chunk, "boundsMin"),
                        Vec3(chunk, "boundsMax"),
                        chunk.Fields.GetValueOrDefault("chunkFilename") as string ?? string.Empty));

        var dims = new List<uint>();
        if (root.Fields.GetValueOrDefault("chunkDims") is object?[] dimObjects)
            foreach (var dim in dimObjects) dims.Add(PackValue.AsU32(dim));

        return new(Vec3(root, "boundsMin"), Vec3(root, "boundsMax"), dims, chunks);
    }

    private static Vector3 Vec3(PackObject obj, string name) =>
        obj.Fields.GetValueOrDefault(name) is float[] values && values.Length >= 3
            ? new Vector3(values[0], values[1], values[2])
            : Vector3.Zero;
}
