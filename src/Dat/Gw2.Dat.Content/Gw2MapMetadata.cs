using Gw2.Formats;
namespace Gw2.Dat.Content;

public sealed record Gw2MapMetadataEntry(ushort MapId, byte MapType);

public sealed class Gw2MapMetadata
{
    public const uint FileId = 198302;
    public const byte UnknownMapType = 20;

    public Gw2MapMetadata(IReadOnlyList<Gw2MapMetadataEntry> maps)
    {
        Maps = maps;
    }

    public IReadOnlyList<Gw2MapMetadataEntry> Maps { get; }

    public byte MapTypeOf(ushort mapId)
    {
        foreach (var map in Maps)
            if (map.MapId == mapId) return map.MapType;
        return UnknownMapType;
    }

    public static Gw2MapMetadata? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.FindRoot("PackMapMetadata");
        if (root is null) return null;

        var maps = new List<Gw2MapMetadataEntry>();
        if (root.Fields.GetValueOrDefault("maps") is object?[] mapObjects)
            foreach (var mapObject in mapObjects)
                if (mapObject is PackObject map)
                    maps.Add(new(
                        (ushort)PackValue.AsU32(map.Fields.GetValueOrDefault("mapId")),
                        (byte)PackValue.AsU32(map.Fields.GetValueOrDefault("mapType"))));

        return new(maps);
    }
}
