using Gw2.Formats;
using System.Numerics;

namespace Gw2.Dat.Content;

public sealed record Gw2NavMeshChunk(byte[] NavMeshData, byte[] MediatorMoppData, byte[] CoarseGraphData);

public sealed class Gw2PhysicsNavMesh
{
    public Gw2PhysicsNavMesh(Vector3 boundsMin, Vector3 boundsMax, float erosionRadius, IReadOnlyList<Gw2NavMeshChunk> chunks)
    {
        BoundsMin = boundsMin;
        BoundsMax = boundsMax;
        ErosionRadius = erosionRadius;
        Chunks = chunks;
    }

    public Vector3 BoundsMin { get; }
    public Vector3 BoundsMax { get; }
    public float ErosionRadius { get; }
    public IReadOnlyList<Gw2NavMeshChunk> Chunks { get; }

    public static Gw2PhysicsNavMesh? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.FindRoot("PackMapPhysicsNavMesh");
        if (root is null) return null;

        var chunks = new List<Gw2NavMeshChunk>();
        if (root.Fields.GetValueOrDefault("chunkArray") is object?[] chunkObjects)
            foreach (var chunkObject in chunkObjects)
                if (chunkObject is PackObject chunk)
                    chunks.Add(new(
                        PackValue.AsBytes(chunk.Fields.GetValueOrDefault("navMeshData")),
                        PackValue.AsBytes(chunk.Fields.GetValueOrDefault("mediatorMoppData")),
                        PackValue.AsBytes(chunk.Fields.GetValueOrDefault("coarseGraphData"))));

        return new(Vec3(root, "boundsMin"), Vec3(root, "boundsMax"), PackValue.AsF32(root.Fields.GetValueOrDefault("erosionRadius")), chunks);
    }

    private static Vector3 Vec3(PackObject obj, string name) =>
        obj.Fields.GetValueOrDefault(name) is float[] values && values.Length >= 3
            ? new Vector3(values[0], values[1], values[2])
            : Vector3.Zero;
}
