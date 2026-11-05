namespace Gw2.Formats;

public sealed class FileArchiveIds
{
    private readonly IReadOnlyDictionary<uint, uint> _recordByFileId;
    private readonly Dictionary<uint, (uint BaseId, uint NewestFileId)> _idsByRecord = [];

    public FileArchiveIds(IReadOnlyDictionary<uint, uint> recordByFileId)
    {
        _recordByFileId = recordByFileId;
        foreach (var (fileId, record) in recordByFileId)
            _idsByRecord[record] = _idsByRecord.TryGetValue(record, out var ids)
                ? (Math.Min(ids.BaseId, fileId), Math.Max(ids.NewestFileId, fileId))
                : (fileId, fileId);
    }

    public bool Contains(uint fileId) => _recordByFileId.ContainsKey(fileId);

    public bool TryGetBaseId(uint fileId, out uint baseId)
    {
        baseId = 0;
        if (!_recordByFileId.TryGetValue(fileId, out uint record)) return false;
        baseId = _idsByRecord[record].BaseId;
        return true;
    }

    public bool TryGetFileId(uint baseId, out uint fileId)
    {
        fileId = 0;
        if (!_recordByFileId.TryGetValue(baseId, out uint record)) return false;
        fileId = _idsByRecord[record].NewestFileId;
        return true;
    }

    public uint BaseIdOf(uint fileId) =>
        TryGetBaseId(fileId, out uint baseId) ? baseId : throw new KeyNotFoundException($"0x{fileId:X8} is not in the archive index");
}
