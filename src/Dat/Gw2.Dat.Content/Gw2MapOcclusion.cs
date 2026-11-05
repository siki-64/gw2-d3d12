using Gw2.Formats;
namespace Gw2.Dat.Content;

public sealed class Gw2MapOcclusion
{
    public Gw2MapOcclusion(uint enableTomeQueries, byte[] tome, byte[] propIdMap, byte[] reserved)
    {
        EnableTomeQueries = enableTomeQueries;
        Tome = tome;
        PropIdMap = propIdMap;
        Reserved = reserved;
    }

    public uint EnableTomeQueries { get; }
    public byte[] Tome { get; }
    public byte[] PropIdMap { get; }
    public byte[] Reserved { get; }

    public static Gw2MapOcclusion? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.Chunks
            .Select(chunk => chunk.Root)
            .FirstOrDefault(candidate => candidate is not null && candidate.TypeName == "MapOcclusionTome");
        if (root is null) return null;

        return new(
            PackValue.AsU32(root.Fields.GetValueOrDefault("enableTomeQueries")),
            PackValue.AsBytes(root.Fields.GetValueOrDefault("tome")),
            PackValue.AsBytes(root.Fields.GetValueOrDefault("propIDMap")),
            PackValue.AsBytes(root.Fields.GetValueOrDefault("reserved")));
    }
}
