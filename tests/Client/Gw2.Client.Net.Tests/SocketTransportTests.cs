using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Gw2.Client.Net.Transport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public class SocketTransportTests
{
    [TestMethod]
    public async Task BytesCrossALocalSocketInBothDirections()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var accepting = listener.AcceptSocketAsync();
        using var transport = await SocketTransport.ConnectAsync("127.0.0.1", port, CancellationToken.None);
        using var server = await accepting;

        transport.Send(new byte[] { 1, 2, 3 });
        var fromClient = new byte[3];
        Assert.AreEqual(3, server.Receive(fromClient));
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, fromClient);

        server.Send(new byte[] { 4, 5 });
        var received = Array.Empty<byte>();
        var waited = Stopwatch.StartNew();
        while (received.Length < 2 && waited.ElapsedMilliseconds < 5000)
            if (transport.TryTakeReceived(out var bytes)) received = received.Concat(bytes).ToArray();
        CollectionAssert.AreEqual(new byte[] { 4, 5 }, received);

        server.Shutdown(SocketShutdown.Both);
        while (!transport.Closed && waited.ElapsedMilliseconds < 5000) Thread.Yield();
        Assert.IsTrue(transport.Closed);
    }

    [TestMethod]
    public void TheAuthHostIsBuiltLikeTheExe() =>
        Assert.AreEqual("Auth1.101.ArenaNetworks.com", AuthServerAddress.HostName(1, 101));
}
