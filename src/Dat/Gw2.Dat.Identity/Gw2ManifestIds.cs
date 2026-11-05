namespace Gw2.Dat.Identity;

public readonly record struct Gw2ManifestEntry(Gw2BaseId BaseId, Gw2FileId FileId, uint Size);

public sealed class Gw2ManifestIds
{
    private readonly Dictionary<Gw2BaseId, Gw2ManifestEntry> _entryByBaseId = new();

    public Gw2ManifestIds(uint buildId, IEnumerable<Gw2ManifestEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        BuildId = buildId;
        foreach (var entry in entries)
            _entryByBaseId[entry.BaseId] = entry;
    }

    public uint BuildId { get; }

    public int Count => _entryByBaseId.Count;

    public IEnumerable<Gw2ManifestEntry> Entries => _entryByBaseId.Values;

    public bool TryGetEntry(Gw2BaseId baseId, out Gw2ManifestEntry entry) =>
        _entryByBaseId.TryGetValue(baseId, out entry);

    public bool TryGetFileId(Gw2BaseId baseId, out Gw2FileId fileId)
    {
        var found = _entryByBaseId.TryGetValue(baseId, out var entry);
        fileId = entry.FileId;
        return found;
    }
}
