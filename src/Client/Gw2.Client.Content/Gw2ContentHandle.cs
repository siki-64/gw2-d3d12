using System;
using System.Threading.Tasks;

namespace Gw2.Client.Content;

public sealed class Gw2ContentHandle<T> where T : class
{
    private readonly Task<T?> _decode;

    internal Gw2ContentHandle(uint fileId, Task<T?> decode)
    {
        FileId = fileId;
        _decode = decode;
    }

    public uint FileId { get; }

    public bool IsCompleted => _decode.IsCompleted;

    public bool IsFaulted => _decode.IsFaulted;

    public Exception? Error => _decode.Exception?.GetBaseException();

    public T? Result => _decode.IsCompletedSuccessfully ? _decode.Result : null;

    public Task<T?> Completion => _decode;
}
