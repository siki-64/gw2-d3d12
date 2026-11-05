using Gw2.Formats;
using System.Numerics;

namespace Gw2.Dat.Content;

public sealed record Gw2MapEditCamLocation(Vector3 Position, float Rotation, float Attack, string Name);

public sealed record Gw2MapEditSnapPoint(ulong Guid, uint ModuleId, uint Flags, Vector3 Position, Vector3 Rotation, float Scale);

public sealed record Gw2MapEditMeasureSpan(
    string Name,
    ulong SubstrateId0,
    ulong SubstrateId1,
    uint SubstrateModuleId0,
    uint SubstrateModuleId1,
    Vector3 Position0,
    Vector3 Position1,
    Vector3 Limits);

public sealed record Gw2MapEditDirtyChunk(uint DirtyFlags, IReadOnlyList<uint> ChunkCoord);

public sealed class Gw2MapEdit
{
    public Gw2MapEdit(
        string mapHome,
        IReadOnlyList<Gw2MapEditCamLocation> camLocations,
        IReadOnlyList<Gw2MapEditSnapPoint> snapPoints,
        IReadOnlyList<Gw2MapEditMeasureSpan> measureSpans,
        IReadOnlyList<Gw2MapEditDirtyChunk> dirtyChunks)
    {
        MapHome = mapHome;
        CamLocations = camLocations;
        SnapPoints = snapPoints;
        MeasureSpans = measureSpans;
        DirtyChunks = dirtyChunks;
    }

    public string MapHome { get; }
    public IReadOnlyList<Gw2MapEditCamLocation> CamLocations { get; }
    public IReadOnlyList<Gw2MapEditSnapPoint> SnapPoints { get; }
    public IReadOnlyList<Gw2MapEditMeasureSpan> MeasureSpans { get; }
    public IReadOnlyList<Gw2MapEditDirtyChunk> DirtyChunks { get; }

    public static Gw2MapEdit? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.Chunks
            .Select(chunk => chunk.Root)
            .FirstOrDefault(candidate => candidate is not null && candidate.TypeName == "MapEditData");
        if (root is null) return null;

        var cams = new List<Gw2MapEditCamLocation>();
        if (root.Fields.GetValueOrDefault("camLocations") is object?[] camObjects)
            foreach (var camObject in camObjects)
                if (camObject is PackObject cam)
                    cams.Add(new(
                        Vec3(cam, "position"),
                        PackValue.AsF32(cam.Fields.GetValueOrDefault("rotation")),
                        PackValue.AsF32(cam.Fields.GetValueOrDefault("attack")),
                        cam.Fields.GetValueOrDefault("name") as string ?? string.Empty));

        var snaps = new List<Gw2MapEditSnapPoint>();
        if (root.Fields.GetValueOrDefault("snapPoints") is object?[] snapObjects)
            foreach (var snapObject in snapObjects)
                if (snapObject is PackObject snap)
                    snaps.Add(new(
                        PackValue.AsU64(snap.Fields.GetValueOrDefault("guid")),
                        PackValue.AsU32(snap.Fields.GetValueOrDefault("moduleId")),
                        PackValue.AsU32(snap.Fields.GetValueOrDefault("flags")),
                        Vec3(snap, "position"),
                        Vec3(snap, "rotation"),
                        PackValue.AsF32(snap.Fields.GetValueOrDefault("scale"))));

        var spans = new List<Gw2MapEditMeasureSpan>();
        if (root.Fields.GetValueOrDefault("measureSpans") is object?[] spanObjects)
            foreach (var spanObject in spanObjects)
                if (spanObject is PackObject span)
                    spans.Add(new(
                        span.Fields.GetValueOrDefault("name") as string ?? string.Empty,
                        PackValue.AsU64(span.Fields.GetValueOrDefault("substrateId0")),
                        PackValue.AsU64(span.Fields.GetValueOrDefault("substrateId1")),
                        PackValue.AsU32(span.Fields.GetValueOrDefault("substrateModuleId0")),
                        PackValue.AsU32(span.Fields.GetValueOrDefault("substrateModuleId1")),
                        Vec3(span, "position0"),
                        Vec3(span, "position1"),
                        Vec3(span, "limits")));

        var dirty = new List<Gw2MapEditDirtyChunk>();
        if (root.Fields.GetValueOrDefault("dirtyChunks") is object?[] dirtyObjects)
            foreach (var dirtyObject in dirtyObjects)
                if (dirtyObject is PackObject chunk)
                    dirty.Add(new(
                        PackValue.AsU32(chunk.Fields.GetValueOrDefault("dirtyFlags")),
                        PackValue.AsU32List(chunk.Fields.GetValueOrDefault("chunkCoord"))));

        return new(root.Fields.GetValueOrDefault("mapHome") as string ?? string.Empty, cams, snaps, spans, dirty);
    }

    private static Vector3 Vec3(PackObject obj, string name) =>
        obj.Fields.GetValueOrDefault(name) is float[] values && values.Length >= 3
            ? new Vector3(values[0], values[1], values[2])
            : Vector3.Zero;
}
