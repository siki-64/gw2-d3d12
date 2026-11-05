using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Gw2.Client.Net.Transport;

public sealed class SocketTransport : IDisposable
{
    public const int AuthServerPort = 6112;
    private const int ReceiveBufferBytes = 0x10000;

    private readonly Socket _socket;
    private readonly ConcurrentQueue<byte[]> _received = new ConcurrentQueue<byte[]>();
    private readonly CancellationTokenSource _closing = new CancellationTokenSource();
    private Task? _receiving;

    private SocketTransport(Socket socket) => _socket = socket;

    public bool Closed { get; private set; }

    public static async Task<SocketTransport> ConnectAsync(string host, int port, CancellationToken cancellation)
    {
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        await socket.ConnectAsync(host, port, cancellation).ConfigureAwait(false);
        var transport = new SocketTransport(socket);
        transport._receiving = transport.ReceiveUntilClosedAsync();
        return transport;
    }

    public void Send(byte[] bytes)
    {
        var sent = 0;
        while (sent < bytes.Length) sent += _socket.Send(bytes, sent, bytes.Length - sent, SocketFlags.None);
    }

    public bool TryTakeReceived(out byte[] bytes) => _received.TryDequeue(out bytes!);

    public void Dispose()
    {
        _closing.Cancel();
        _socket.Dispose();
        try { _receiving?.Wait(); } catch (AggregateException) { }
        _closing.Dispose();
    }

    private async Task ReceiveUntilClosedAsync()
    {
        var buffer = new byte[ReceiveBufferBytes];
        try
        {
            while (true)
            {
                var count = await _socket.ReceiveAsync(buffer.AsMemory(), SocketFlags.None, _closing.Token).ConfigureAwait(false);
                if (count == 0) break;
                _received.Enqueue(buffer.AsSpan(0, count).ToArray());
            }
        }
        catch (Exception failure) when (failure is OperationCanceledException or SocketException or ObjectDisposedException)
        {
        }
        Closed = true;
    }
}
