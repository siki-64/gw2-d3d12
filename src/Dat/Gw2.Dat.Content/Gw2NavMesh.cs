using Gw2.Formats;
using System.Numerics;

namespace Gw2.Dat.Content;

public sealed record Gw2NavMeshChunkData(
    uint ChunkIndex,
    Vector3 BoundsMin,
    Vector3 BoundsMax,
    byte[] NavMeshData,
    byte[] CoarseGraphData,
    byte[] QueryMediatorMoppData);

public sealed class Gw2NavMeshChunkFile
{
    public Gw2NavMeshChunkFile(IReadOnlyList<Gw2NavMeshChunkData> chunks)
    {
        Chunks = chunks;
    }

    public IReadOnlyList<Gw2NavMeshChunkData> Chunks { get; }

    public static Gw2NavMeshChunkFile? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.FindRoot("PackMapNavMeshChunk");
        if (root is null) return null;

        return new([new(
            PackValue.AsU32(root.Fields.GetValueOrDefault("chunkIndex")),
            Vec3(root, "boundsMin"),
            Vec3(root, "boundsMax"),
            PackValue.AsBytes(root.Fields.GetValueOrDefault("navMeshData")),
            PackValue.AsBytes(root.Fields.GetValueOrDefault("coarseGraphData")),
            PackValue.AsBytes(root.Fields.GetValueOrDefault("queryMediatorMoppData")))]);
    }

    private static Vector3 Vec3(PackObject obj, string name) =>
        obj.Fields.GetValueOrDefault(name) is float[] values && values.Length >= 3
            ? new Vector3(values[0], values[1], values[2])
            : Vector3.Zero;
}

public sealed record Gw2NavMeshExternalChunk(Vector3 BoundsMin, Vector3 BoundsMax, string Filename);

public sealed record Gw2NavMeshMover(uint ChunkIndex, ulong MapPropId, byte[] NavMeshData, byte[] CoarseGraphData, byte[] MediatorData);

public sealed class Gw2NavMeshExternal
{
    public Gw2NavMeshExternal(
        Vector3 boundsMin,
        Vector3 boundsMax,
        IReadOnlyList<uint> chunkDims,
        IReadOnlyList<Gw2NavMeshExternalChunk> chunks,
        IReadOnlyList<Gw2NavMeshMover> movers)
    {
        BoundsMin = boundsMin;
        BoundsMax = boundsMax;
        ChunkDims = chunkDims;
        Chunks = chunks;
        Movers = movers;
    }

    public Vector3 BoundsMin { get; }
    public Vector3 BoundsMax { get; }
    public IReadOnlyList<uint> ChunkDims { get; }
    public IReadOnlyList<Gw2NavMeshExternalChunk> Chunks { get; }
    public IReadOnlyList<Gw2NavMeshMover> Movers { get; }

    public static Gw2NavMeshExternal? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.FindRoot("PackMapNavMeshExternal");
        if (root is null) return null;

        var chunks = new List<Gw2NavMeshExternalChunk>();
        if (root.Fields.GetValueOrDefault("chunkArray") is object?[] chunkObjects)
            foreach (var chunkObject in chunkObjects)
                if (chunkObject is PackObject chunk)
                    chunks.Add(new(
                        Vec3(chunk, "boundsMin"),
                        Vec3(chunk, "boundsMax"),
                        chunk.Fields.GetValueOrDefault("filename") as string ?? string.Empty));

        var movers = new List<Gw2NavMeshMover>();
        if (root.Fields.GetValueOrDefault("dynamicArray") is object?[] moverObjects)
            foreach (var moverObject in moverObjects)
                if (moverObject is PackObject mover)
                    movers.Add(new(
                        PackValue.AsU32(mover.Fields.GetValueOrDefault("chunkIndex")),
                        PackValue.AsU64(mover.Fields.GetValueOrDefault("mapPropId")),
                        PackValue.AsBytes(mover.Fields.GetValueOrDefault("navMeshData")),
                        PackValue.AsBytes(mover.Fields.GetValueOrDefault("coarseGraphData")),
                        PackValue.AsBytes(mover.Fields.GetValueOrDefault("mediatorData"))));

        var dims = new List<uint>();
        if (root.Fields.GetValueOrDefault("chunkDims") is object?[] dimObjects)
            foreach (var dim in dimObjects) dims.Add(PackValue.AsU32(dim));

        return new(Vec3(root, "boundsMin"), Vec3(root, "boundsMax"), dims, chunks, movers);
    }

    private static Vector3 Vec3(PackObject obj, string name) =>
        obj.Fields.GetValueOrDefault(name) is float[] values && values.Length >= 3
            ? new Vector3(values[0], values[1], values[2])
            : Vector3.Zero;
}
