using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Gw2.Protocol.Capture;
using Gw2.Protocol.MsgPack;
using Gw2.Protocol.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public class ProtocolConnectionTests
{
    [TestMethod]
    public void EveryRecordedHelloIsSentFromItsRecordedSeed()
    {
        var capture = RecordedSession.Capture();
        var seeds = capture.EntropyOutputs().Where(output => output.Plaintext.Length == HandshakeKeyExchange.SeedLength).ToList();
        var hellos = capture.Streams().Where(stream => stream.Direction == CaptureRecordKind.HandshakeOutbound).ToList();
        if (hellos.Count == 0 || seeds.Count == 0) Assert.Inconclusive("the capture holds no handshake or no entropy records");

        var nextSeed = 0;
        foreach (var hello in hellos)
        {
            var recorded = hello.Bytes.Slice(0, hello.Bytes[1]).ToArray();
            var match = seeds.FindIndex(nextSeed, seed => SentHello(seed.Plaintext).SequenceEqual(recorded));
            Assert.IsTrue(match >= 0, $"no recorded seed from index {nextSeed} makes the connection send the hello of connection {hello.ConnectionOrder}");
            nextSeed = match + 1;
        }
    }

    [TestMethod]
    public void EveryRecordedStreamPassesThroughTheConnectionUnchanged()
    {
        var capture = RecordedSession.Capture();
        var decoded = CaptureDecoding.DecodeAll(capture, RecordedSession.Schemas);
        var connections = decoded.GroupBy(result => result.Stream.ConnectionOrder).ToList();
        Assert.IsTrue(connections.Count > 0, "the capture holds no cipher streams");

        foreach (var connection in connections)
        {
            var corpusName = connection.First().Corpus;
            var corpus = corpusName == "login" ? RecordedSession.Schemas.Login : RecordedSession.Schemas.Game;
            var inbound = connection.SingleOrDefault(result => result.Stream.Direction == CaptureRecordKind.Inbound);
            var outbound = connection.SingleOrDefault(result => result.Stream.Direction == CaptureRecordKind.Outbound);

            var seed = RandomNumberGenerator.GetBytes(HandshakeKeyExchange.SeedLength);
            var server = new LoopbackServer(RecordedSession.Group, seed);
            var client = new ProtocolConnection(corpus, RecordedSession.Group, seed, server.OnClientBytes);
            Assert.IsNotNull(server.Hello);
            Assert.AreEqual(0, client.Receive(server.KeyFrame()).Count);
            Assert.IsTrue(client.Established);

            if (inbound is not null) AssertInboundPassesThrough(client, server, inbound);
            if (outbound is not null) AssertOutboundPassesThrough(client, server, outbound);
            Console.WriteLine($"connection {connection.Key} as {corpusName}: {inbound?.Messages.Count ?? 0} inbound and {outbound?.Messages.Count ?? 0} outbound messages unchanged");
        }
    }

    [TestMethod]
    public void AKeyFrameSplitAcrossReadsStillEstablishes()
    {
        var seed = RandomNumberGenerator.GetBytes(HandshakeKeyExchange.SeedLength);
        var server = new LoopbackServer(RecordedSession.Group, seed);
        var client = new ProtocolConnection(RecordedSession.Schemas.Login, RecordedSession.Group, seed, server.OnClientBytes);
        var keyFrame = server.KeyFrame();

        client.Receive(keyFrame.AsSpan(0, 1));
        Assert.IsFalse(client.Established);
        client.Receive(keyFrame.AsSpan(1));
        Assert.IsTrue(client.Established);
    }

    [TestMethod]
    public void AnErrorFrameRefusesTheConnection()
    {
        var seed = RandomNumberGenerator.GetBytes(HandshakeKeyExchange.SeedLength);
        var server = new LoopbackServer(RecordedSession.Group, seed);
        var client = new ProtocolConnection(RecordedSession.Schemas.Login, RecordedSession.Group, seed, server.OnClientBytes);

        Assert.ThrowsExactly<ProtocolConnectionRefusedException>(() => client.Receive(new byte[] { 2, 10, 0, 0, 0, 0, 0, 0, 0, 0 }));
    }

    private static byte[] SentHello(ReadOnlySpan<byte> seed)
    {
        byte[]? sent = null;
        _ = new ProtocolConnection(RecordedSession.Schemas.Login, RecordedSession.Group, seed, bytes => sent ??= bytes);
        return sent!;
    }

    private static void AssertInboundPassesThrough(ProtocolConnection client, LoopbackServer server, StreamDecodeResult recorded)
    {
        var received = new List<DecodedMessage>();
        var start = 0;
        foreach (var end in recorded.Stream.ChunkEnds())
        {
            received.AddRange(client.Receive(server.Encrypt(recorded.Stream.Bytes[start..end])));
            start = end;
        }
        CollectionAssert.AreEqual(recorded.Messages.Select(message => Convert.ToHexString(message.Wire!)).ToList(),
            received.Select(message => Convert.ToHexString(message.Wire!)).ToList(),
            $"connection {recorded.Stream.ConnectionOrder} inbound");
    }

    private static void AssertOutboundPassesThrough(ProtocolConnection client, LoopbackServer server, StreamDecodeResult recorded)
    {
        var chunkEnds = recorded.Stream.ChunkEnds();
        var flushPoints = chunkEnds.ToHashSet();
        foreach (var message in recorded.Messages)
        {
            client.Send(message.MessageId, message.Fields);
            if (flushPoints.Contains(message.WireOffset + message.WireLength)) client.EndFrame();
        }

        var recordedChunks = new List<string>();
        var start = 0;
        foreach (var end in chunkEnds)
        {
            recordedChunks.Add(Convert.ToHexString(recorded.Stream.Bytes[start..end]));
            start = end;
        }
        CollectionAssert.AreEqual(recordedChunks, server.ReceivedPlaintext.Select(Convert.ToHexString).ToList(),
            $"connection {recorded.Stream.ConnectionOrder} outbound");
    }
}
