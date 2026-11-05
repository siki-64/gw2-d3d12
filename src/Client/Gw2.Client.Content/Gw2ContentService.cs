using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Gw2.Dat.Content;

namespace Gw2.Client.Content;

public sealed class Gw2ContentService : IDisposable
{
    private readonly Gw2ContentReader _reader;
    private readonly BlockingCollection<Action> _pendingDecodes = new();
    private readonly Dictionary<(uint FileId, Type Kind), CachedContent> _cache = new();
    private readonly Thread[] _workers;

    public Gw2ContentService(Gw2ContentReader reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _ = _reader.Ids;
        _ = _reader.Keys;
        _workers = new Thread[Math.Clamp(Environment.ProcessorCount / 2, 1, 4)];
        for (var index = 0; index < _workers.Length; index++)
        {
            _workers[index] = new Thread(RunWorker) { IsBackground = true, Name = $"Gw2 content worker {index}" };
            _workers[index].Start();
        }
    }

    public Gw2ContentReader Reader => _reader;

    public int CachedCount
    {
        get
        {
            lock (_cache) return _cache.Count;
        }
    }

    public Gw2ContentHandle<T> Request<T>(uint fileId, Func<Gw2ContentReader, uint, T?> decode) where T : class
    {
        ArgumentNullException.ThrowIfNull(decode);
        var key = (fileId, typeof(T));
        lock (_cache)
        {
            if (_cache.TryGetValue(key, out var cached))
            {
                cached.ReferenceCount++;
                return (Gw2ContentHandle<T>)cached.Handle;
            }

            var completion = new TaskCompletionSource<T?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var handle = new Gw2ContentHandle<T>(fileId, completion.Task);
            _cache[key] = new CachedContent(handle);
            _pendingDecodes.Add(() =>
            {
                try
                {
                    completion.SetResult(decode(_reader, fileId));
                }
                catch (Exception exception)
                {
                    completion.SetException(exception);
                }
            });
            return handle;
        }
    }

    public void Release<T>(Gw2ContentHandle<T> handle) where T : class
    {
        ArgumentNullException.ThrowIfNull(handle);
        var key = (handle.FileId, typeof(T));
        lock (_cache)
        {
            if (!_cache.TryGetValue(key, out var cached) || !ReferenceEquals(cached.Handle, handle)) return;
            if (--cached.ReferenceCount <= 0) _cache.Remove(key);
        }
    }

    public void Dispose()
    {
        _pendingDecodes.CompleteAdding();
        foreach (var worker in _workers) worker.Join();
        _pendingDecodes.Dispose();
    }

    private void RunWorker()
    {
        foreach (var decode in _pendingDecodes.GetConsumingEnumerable()) decode();
    }

    private sealed class CachedContent
    {
        public CachedContent(object handle) => Handle = handle;

        public object Handle { get; }

        public int ReferenceCount { get; set; } = 1;
    }
}
