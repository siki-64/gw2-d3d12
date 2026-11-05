using Gw2.Formats;
namespace Gw2.Dat.Archive;

public sealed record Gw2KeyEntry(uint AssetType, uint AssetId, ulong Key);

public sealed class Gw2KeyTable
{
    public Gw2KeyTable(IReadOnlyList<Gw2KeyEntry> entries)
    {
        Entries = entries;
    }

    public IReadOnlyList<Gw2KeyEntry> Entries { get; }

    public static Gw2KeyTable? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.Chunks
            .Select(chunk => chunk.Root)
            .FirstOrDefault(candidate => candidate is not null && candidate.TypeName == "KeyTableData");
        if (root is null) return null;

        var entries = new List<Gw2KeyEntry>();
        if (root.Fields.GetValueOrDefault("keyEntryArr") is object?[] entryObjects)
            foreach (var entryObject in entryObjects)
                if (entryObject is PackObject entry)
                    entries.Add(new(
                        PackValue.AsU32(entry.Fields.GetValueOrDefault("assetType")),
                        PackValue.AsU32(entry.Fields.GetValueOrDefault("assetId")),
                        PackValue.AsU64(entry.Fields.GetValueOrDefault("key"))));

        return new(entries);
    }
}
