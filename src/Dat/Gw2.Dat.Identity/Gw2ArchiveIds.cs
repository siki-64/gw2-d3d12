namespace Gw2.Dat.Identity;

public sealed class Gw2ArchiveIds
{
    private readonly Dictionary<uint, uint> _recordByArchiveId;
    private readonly Dictionary<uint, (uint Lowest, uint Highest)> _idsByRecord;

    public Gw2ArchiveIds(IReadOnlyDictionary<uint, uint> recordByArchiveId)
    {
        if (recordByArchiveId is null) throw new ArgumentNullException(nameof(recordByArchiveId));

        _recordByArchiveId = new Dictionary<uint, uint>(recordByArchiveId.Count);
        _idsByRecord = new Dictionary<uint, (uint, uint)>();
        foreach (var pair in recordByArchiveId)
        {
            _recordByArchiveId[pair.Key] = pair.Value;
            _idsByRecord[pair.Value] = _idsByRecord.TryGetValue(pair.Value, out var ids)
                ? (Math.Min(ids.Lowest, pair.Key), Math.Max(ids.Highest, pair.Key))
                : (pair.Key, pair.Key);
        }
    }

    public static string Format(uint archiveId) => $"0x{archiveId:X8}";

    public bool Contains(uint archiveId) => _recordByArchiveId.ContainsKey(archiveId);

    public bool TryGetBaseId(uint archiveId, out Gw2BaseId baseId)
    {
        baseId = default;
        if (!_recordByArchiveId.TryGetValue(archiveId, out var record)) return false;
        baseId = new Gw2BaseId(_idsByRecord[record].Lowest);
        return true;
    }

    public bool TryGetFileId(Gw2BaseId baseId, out Gw2FileId fileId)
    {
        fileId = default;
        if (!_recordByArchiveId.TryGetValue(baseId.Value, out var record)) return false;
        fileId = new Gw2FileId(_idsByRecord[record].Highest);
        return true;
    }

    public Gw2BaseId BaseIdOf(uint archiveId) =>
        TryGetBaseId(archiveId, out var baseId)
            ? baseId
            : throw new KeyNotFoundException($"0x{archiveId:X8} is not in the archive index");
}
