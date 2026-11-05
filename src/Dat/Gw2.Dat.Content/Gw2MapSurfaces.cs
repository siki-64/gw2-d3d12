using Gw2.Formats;
namespace Gw2.Dat.Content;

public sealed record Gw2MapSurfaceAttribute(ulong Id, ulong Sound, uint Flags);

public sealed record Gw2MapSurfaceAttributeTool(string Name, string Category);

public sealed record Gw2MapSurfaceOverride(ulong SurfaceId, byte[] TriangleMask);

public sealed record Gw2MapSurfacePropOverride(ulong PropId, IReadOnlyList<Gw2MapSurfaceOverride> Overrides);

public sealed record Gw2MapSurfaceTerrainOverride(uint ChunkX, uint ChunkY, IReadOnlyList<Gw2MapSurfaceOverride> Overrides);

public sealed class Gw2MapSurfaces
{
    public Gw2MapSurfaces(IReadOnlyList<Gw2MapSurfaceAttribute> attributes, IReadOnlyList<Gw2MapSurfaceAttributeTool> tools)
    {
        Attributes = attributes;
        Tools = tools;
    }

    public IReadOnlyList<Gw2MapSurfaceAttribute> Attributes { get; }
    public IReadOnlyList<Gw2MapSurfaceAttributeTool> Tools { get; }
    public IReadOnlyList<Gw2MapSurfacePropOverride> PropOverrides { get; init; } = [];
    public IReadOnlyList<Gw2MapSurfaceTerrainOverride> TerrainOverrides { get; init; } = [];

    public static Gw2MapSurfaces? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.Chunks
            .Select(chunk => chunk.Root)
            .FirstOrDefault(candidate => candidate is not null && candidate.TypeName == "MapSurfaces");
        if (root is null) return null;

        var attributes = new List<Gw2MapSurfaceAttribute>();
        if (root.Fields.GetValueOrDefault("attributeData") is object?[] attributeObjects)
            foreach (var attributeObject in attributeObjects)
                if (attributeObject is PackObject attribute)
                    attributes.Add(new(
                        PackValue.AsU64(attribute.Fields.GetValueOrDefault("Id")),
                        PackValue.AsU64(attribute.Fields.GetValueOrDefault("Sound")),
                        PackValue.AsU32(attribute.Fields.GetValueOrDefault("flags"))));

        var tools = new List<Gw2MapSurfaceAttributeTool>();
        if (root.Fields.GetValueOrDefault("toolData") is object?[] toolObjects)
            foreach (var toolObject in toolObjects)
                if (toolObject is PackObject tool)
                    tools.Add(new(
                        tool.Fields.GetValueOrDefault("name") as string ?? string.Empty,
                        tool.Fields.GetValueOrDefault("category") as string ?? string.Empty));

        return new(attributes, tools)
        {
            PropOverrides = PackValue.AsObjects(root.Fields.GetValueOrDefault("propArray"))
                .Select(prop => new Gw2MapSurfacePropOverride(
                    PackValue.AsU64(prop.Fields.GetValueOrDefault("propId")),
                    ReadOverrides(prop)))
                .ToArray(),
            TerrainOverrides = PackValue.AsObjects(root.Fields.GetValueOrDefault("terrainArray"))
                .Select(terrain =>
                {
                    var chunkCoord = PackValue.AsU32List(terrain.Fields.GetValueOrDefault("chunkCoord"));
                    return new Gw2MapSurfaceTerrainOverride(
                        chunkCoord.Count > 0 ? chunkCoord[0] : 0,
                        chunkCoord.Count > 1 ? chunkCoord[1] : 0,
                        ReadOverrides(terrain));
                })
                .ToArray(),
        };
    }

    private static IReadOnlyList<Gw2MapSurfaceOverride> ReadOverrides(PackObject owner) =>
        PackValue.AsObjects(owner.Fields.GetValueOrDefault("overrideArray"))
            .Select(entry => new Gw2MapSurfaceOverride(
                PackValue.AsU64(entry.Fields.GetValueOrDefault("surfaceId")),
                PackValue.AsBytes(entry.Fields.GetValueOrDefault("bitArray"))))
            .ToArray();
}
