using Gw2.Formats;
using System.Numerics;

namespace Gw2.Dat.Content;

public sealed record Gw2MapChunk(string Type, ushort Version);

public sealed record Gw2MapInfo(string Guid, IReadOnlyList<float> Bounds, uint Flags, IReadOnlyList<Gw2MapChunk> Chunks)
{
    public static Gw2MapInfo? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var parameter = document.Chunks
            .Select(chunk => chunk.Root)
            .FirstOrDefault(root => root is not null && root.TypeName == "MapParam");
        if (parameter is null) return null;

        var guid = parameter.Fields.GetValueOrDefault("guid") is ReadOnlyMemory<byte> bytes
            ? Gw2Binary.ToHexString(bytes.Span)
            : string.Empty;
        var bounds = parameter.Fields.GetValueOrDefault("rect") as float[] ?? [];
        var flags = parameter.Fields.GetValueOrDefault("flags") is uint value ? value : 0;
        var chunks = document.Chunks
            .Select(chunk => new Gw2MapChunk(chunk.Chunk.Type.TrimEnd(), chunk.Chunk.Version))
            .ToArray();

        return new(guid, bounds, flags, chunks);
    }
}

public sealed record Gw2MapStartModel(uint FileId, uint Flags, ulong Permutation);

public sealed record Gw2MapStart(uint Token, Vector3 Position, float Radius, IReadOnlyList<uint> ModelFileIds)
{
    public IReadOnlyList<Gw2MapStartModel> Models { get; init; } = [];
}

public sealed record Gw2MapRedirector(uint Token, Vector3 Position, float Radius);

public sealed record Gw2PortalMap(string Guid, IReadOnlyList<Gw2MapStart> Starts)
{
    public IReadOnlyList<Gw2MapRedirector> Redirectors { get; init; } = [];
}

public sealed class Gw2PortalManifest
{
    public Gw2PortalManifest(IReadOnlyList<Gw2PortalMap> maps) => Maps = maps;

    public IReadOnlyList<Gw2PortalMap> Maps { get; }

    public static Gw2PortalManifest? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.FindRoot("ContentPortalManifest");
        if (root is null) return null;

        var maps = new List<Gw2PortalMap>();
        if (root.Fields.GetValueOrDefault("mapArray") is object?[] mapArray)
        {
            foreach (var mapObject in mapArray)
                if (mapObject is PackObject map)
                    maps.Add(ReadMap(map));
        }

        return new(maps);
    }

    public IEnumerable<uint> EnumerateModelDependencies()
    {
        var seen = new HashSet<uint>();
        foreach (var map in Maps)
            foreach (var start in map.Starts)
                foreach (var fileId in start.ModelFileIds)
                    if (seen.Add(fileId))
                        yield return fileId;
    }

    private static Gw2PortalMap ReadMap(PackObject map)
    {
        var guid = map.Fields.GetValueOrDefault("mapGUID") is ReadOnlyMemory<byte> bytes
            ? Gw2Binary.ToHexString(bytes.Span)
            : string.Empty;

        var starts = new List<Gw2MapStart>();
        if (map.Fields.GetValueOrDefault("mapStartArray") is object?[] startArray)
        {
            foreach (var startObject in startArray)
                if (startObject is PackObject start)
                    starts.Add(ReadStart(start));
        }

        return new(guid, starts)
        {
            Redirectors = PackValue.AsObjects(map.Fields.GetValueOrDefault("mapRedirectorArray"))
                .Select(redirector => new Gw2MapRedirector(
                    PackValue.AsU32(redirector.Fields.GetValueOrDefault("token")),
                    redirector.Fields.GetValueOrDefault("position") is float[] { Length: >= 3 } position
                        ? new Vector3(position[0], position[1], position[2])
                        : Vector3.Zero,
                    PackValue.AsF32(redirector.Fields.GetValueOrDefault("radius"))))
                .ToArray(),
        };
    }

    private static Gw2MapStart ReadStart(PackObject start)
    {
        var token = start.Fields.GetValueOrDefault("token") is uint value ? value : 0;
        var position = start.Fields.GetValueOrDefault("position") is float[] floats && floats.Length >= 3
            ? new Vector3(floats[0], floats[1], floats[2])
            : Vector3.Zero;
        var radius = start.Fields.GetValueOrDefault("radius") is float value0 ? value0 : 0;

        var modelFileIds = new List<uint>();
        if (start.Fields.GetValueOrDefault("modelArray") is object?[] models)
        {
            foreach (var modelObject in models)
                if (modelObject is PackObject model &&
                    model.Fields.GetValueOrDefault("filename") is PackFileReference reference &&
                    reference.TryGetFileId(out var fileId))
                    modelFileIds.Add(fileId);
        }

        var startModels = PackValue.AsObjects(start.Fields.GetValueOrDefault("modelArray"))
            .Select(model => new Gw2MapStartModel(
                model.Fields.GetValueOrDefault("filename") is PackFileReference reference && reference.TryGetFileId(out var fileId) ? fileId : 0,
                PackValue.AsU32(model.Fields.GetValueOrDefault("flags")),
                PackValue.AsU64(model.Fields.GetValueOrDefault("permutation"))))
            .ToArray();

        return new(token, position, radius, modelFileIds) { Models = startModels };
    }
}
