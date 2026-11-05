using System.Collections.Generic;
using System.Threading;
using Gw2.Client.Content;
using Gw2.Dat.Content;

namespace Gw2.Client;

public sealed class Gw2ArchiveMaterials
{
    private readonly Gw2ContentService _content;
    private readonly Dictionary<uint, Gw2ContentHandle<Gw2AmatMaterial>> _requested = new();
    private readonly Dictionary<uint, Gw2AmatMaterial?> _readOnMainThread = new();
    private readonly Lock _lock = new();

    public Gw2ArchiveMaterials(Gw2ContentService content) => _content = content;

    public int MainThreadReads { get; private set; }

    public void Request(uint fileId)
    {
        lock (_lock) RequestLocked(fileId);
    }

    private void RequestLocked(uint fileId)
    {
        if (fileId == 0 || _requested.ContainsKey(fileId)) return;
        _requested[fileId] = _content.Request(fileId, static (reader, id) => reader.ReadMaterial(id));
    }

    public bool IsDecoded(uint fileId)
    {
        if (fileId == 0) return true;
        lock (_lock) return _requested.TryGetValue(fileId, out var handle) && handle.IsCompleted;
    }

    public Gw2AmatMaterial? Of(uint fileId)
    {
        lock (_lock) return OfLocked(fileId);
    }

    private Gw2AmatMaterial? OfLocked(uint fileId)
    {
        RequestLocked(fileId);
        var handle = _requested[fileId];
        if (handle.IsCompleted) return handle.Result;
        if (_readOnMainThread.TryGetValue(fileId, out var material)) return material;
        MainThreadReads++;
        material = _content.Reader.ReadMaterial(fileId);
        _readOnMainThread[fileId] = material;
        return material;
    }
}
