using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Gw2.Client.Net.Login;
using Gw2.Client.Net.Ticks;
using Gw2.Protocol.Capture;
using Gw2.Protocol.MsgPack;
using Gw2.Protocol.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public class LoginSessionTests
{
    [TestMethod]
    public void TheReplayedLoginSendsTheRecordedBytesInTheRecordedTicks()
    {
        var capture = RecordedSession.Capture();
        var decoded = CaptureDecoding.DecodeAll(capture, RecordedSession.Schemas);
        var outbound = decoded.SingleOrDefault(result => result.Corpus == "login" && result.Stream.Direction == CaptureRecordKind.Outbound);
        if (outbound is null) Assert.Inconclusive("the capture holds no login connection");
        var loginOrder = outbound.Stream.ConnectionOrder;
        var ticks = capture.EventTicks().Select(tick => new EventTickTime(tick.Milliseconds, tick.FileTime)).ToList();
        if (ticks.Count == 0) Assert.Inconclusive("the capture holds no event tick records");

        var identity = IdentityFrom(outbound.Messages);
        var characterSelectFlushes = CharacterSelectFlushCount(outbound);
        var recordedFlushTicks = RecordedFlushTicks(capture, loginOrder).Take(characterSelectFlushes).ToList();
        var recordedKeyScheduleTick = RecordedKeyScheduleTick(capture, loginOrder);
        var keyFrameTick = recordedKeyScheduleTick ?? recordedFlushTicks[0];
        Console.WriteLine($"key schedule tick {(recordedKeyScheduleTick is null ? "not recorded" : recordedKeyScheduleTick.ToString())}, first flush tick {recordedFlushTicks[0]}");
        var logoutTick = recordedFlushTicks[^1];
        var logsOutAtCharacterSelect = characterSelectFlushes == outbound.Stream.ChunkEnds().Count && outbound.Messages[^1].MessageId == LoginMessageIds.Logout;
        var seed = RandomNumberGenerator.GetBytes(HandshakeKeyExchange.SeedLength);
        var server = new LoopbackServer(RecordedSession.Group, seed);
        var connection = new ProtocolConnection(RecordedSession.Schemas.Login, RecordedSession.Group, seed, server.OnClientBytes);
        uint latestMilliseconds = 0;
        var session = new LoginSession(connection, identity, () => latestMilliseconds);
        var loop = new EventTickLoop(new RecordedEventTickTimeSource(ticks), limitsBacklog: false);
        loop.Attach(connection);
        loop.Ticked += _ =>
        {
            session.OnTick();
            if (loop.TickCount == keyFrameTick) session.Receive(server.KeyFrame());
            if (loop.TickCount == logoutTick && logsOutAtCharacterSelect) session.Logout();
        };

        var connectionOrder = new Dictionary<ulong, int>();
        var flushedTicks = new List<long>();
        var tickOpen = false;
        void EndOpenTick()
        {
            if (!tickOpen) return;
            var flushedBefore = server.ReceivedPlaintext.Count;
            loop.EndTick();
            for (var flush = flushedBefore; flush < server.ReceivedPlaintext.Count; flush++) flushedTicks.Add(loop.TickCount - 1);
        }
        foreach (var chunk in capture.Chunks)
        {
            if (chunk.Kind == CaptureRecordKind.Milliseconds && chunk.Plaintext.Length == sizeof(ulong))
                latestMilliseconds = (uint)BitConverter.ToUInt64(chunk.Plaintext);
            if (chunk.Kind == CaptureRecordKind.EventTickFileTime)
            {
                EndOpenTick();
                tickOpen = loop.BeginTick();
            }
            if (chunk.Kind is not (CaptureRecordKind.Inbound or CaptureRecordKind.Outbound or CaptureRecordKind.HandshakeOutbound)) continue;
            if (!connectionOrder.TryGetValue(chunk.Connection, out int order)) connectionOrder[chunk.Connection] = order = connectionOrder.Count;
            if (order != loginOrder || chunk.Kind != CaptureRecordKind.Inbound) continue;
            session.Receive(server.Encrypt(chunk.Plaintext));
        }
        EndOpenTick();

        var recordedChunks = new List<string>();
        var start = 0;
        foreach (var end in outbound.Stream.ChunkEnds().Take(characterSelectFlushes))
        {
            recordedChunks.Add(Convert.ToHexString(outbound.Stream.Bytes[start..end]));
            start = end;
        }
        var replayedChunks = server.ReceivedPlaintext.Take(recordedChunks.Count).ToList();
        Console.WriteLine($"{recordedChunks.Count} recorded login flushes, {server.ReceivedPlaintext.Count} replayed; {session.Characters.Characters.Count} characters: {string.Join(", ", session.Characters.Characters.Select(character => character.Name))}");
        CollectionAssert.AreEqual(recordedChunks, replayedChunks.Select(Convert.ToHexString).ToList(), "login flush bytes");
        CollectionAssert.AreEqual(recordedFlushTicks, flushedTicks.Take(recordedFlushTicks.Count).ToList(), "login flush ticks");
        Assert.IsTrue(session.Characters.Characters.Count > 0, "no character arrived");
    }

    [TestMethod]
    public void TheAccountLoginUserIdIsTheIdTheGameServerSendsBack()
    {
        var capture = RecordedSession.Capture();
        var decoded = CaptureDecoding.DecodeAll(capture, RecordedSession.Schemas);
        var login = decoded.SingleOrDefault(result => result.Corpus == "login" && result.Stream.Direction == CaptureRecordKind.Outbound);
        if (login is null) Assert.Inconclusive("the capture holds no login connection");
        var accountLogin = login.Messages.First(message => message.MessageId == LoginMessageIds.AccountLogin);
        var userId = (byte[])accountLogin.Fields[2].Value!;
        var gameToken = (byte[])accountLogin.Fields[3].Value!;

        var loginOrder = login.Stream.ConnectionOrder;
        var otherConnections = capture.Streams().Where(stream => stream.ConnectionOrder != loginOrder).ToList();
        var gameInbound = otherConnections.Where(stream => stream.Direction == CaptureRecordKind.Inbound).ToList();
        if (gameInbound.Count == 0) Assert.Inconclusive("the capture holds no game connection");
        var sendingBack = gameInbound.Where(stream => stream.Bytes.ToArray().AsSpan().IndexOf(userId) >= 0).ToList();
        Console.WriteLine($"user id {new Guid(userId)}, game token {new Guid(gameToken)}; user id in {sendingBack.Count} of {gameInbound.Count} game connections");
        Assert.AreEqual(gameInbound.Count, sendingBack.Count);
    }

    private static int CharacterSelectFlushCount(StreamDecodeResult outbound)
    {
        var chunkEnds = outbound.Stream.ChunkEnds();
        var firstPlay = outbound.Messages.FirstOrDefault(message => message.MessageId == LoginPlayMessageId);
        if (firstPlay is null) return chunkEnds.Count;
        return outbound.Stream.ChunkAt(firstPlay.WireOffset);
    }

    private const int LoginPlayMessageId = 0xE;

    internal static LoginIdentity IdentityFrom(IReadOnlyList<DecodedMessage> sent)
    {
        DecodedMessage Message(int id) => sent.First(message => message.MessageId == id);
        object Field(int id, int index) => Message(id).Fields[index].Value!;
        return new LoginIdentity(
            (string)Field(LoginMessageIds.ClientNames, 1),
            (string)Field(LoginMessageIds.ClientNames, 2),
            (uint)(ulong)Field(LoginMessageIds.ClientBuild, 1),
            (byte[])Field(LoginMessageIds.ClientBuild, 2),
            (uint)(ulong)Field(LoginMessageIds.Config, 1),
            (uint)(ulong)Field(LoginMessageIds.AccountLogin, 1),
            (byte[])Field(LoginMessageIds.AccountLogin, 2),
            (byte[])Field(LoginMessageIds.AccountLogin, 3),
            (string)Field(LoginMessageIds.AccountLogin, 4),
            (string)Field(LoginMessageIds.AccountLogin, 5),
            (byte[])Field(LoginMessageIds.SystemRecord, 1),
            (uint)(ulong)Field(LoginMessageIds.ClientInfo, 1));
    }

    private static long? RecordedKeyScheduleTick(NetworkCapture capture, int connection)
    {
        var connectionOrder = new Dictionary<ulong, int>();
        var keyScheduleTicks = new Dictionary<ulong, long>();
        long tick = -1;
        foreach (var chunk in capture.Chunks)
        {
            if (chunk.Kind == CaptureRecordKind.EventTickFileTime) tick++;
            if (chunk.Kind == CaptureRecordKind.TransportKeySchedule) keyScheduleTicks.TryAdd(chunk.Connection, tick);
            if (chunk.Kind is not (CaptureRecordKind.Inbound or CaptureRecordKind.Outbound or CaptureRecordKind.HandshakeOutbound)) continue;
            connectionOrder.TryAdd(chunk.Connection, connectionOrder.Count);
        }
        foreach (var (key, order) in connectionOrder)
            if (order == connection && keyScheduleTicks.TryGetValue(key, out var keyTick)) return keyTick;
        return null;
    }

    private static List<long> RecordedFlushTicks(NetworkCapture capture, int connection)
    {
        var connectionOrder = new Dictionary<ulong, int>();
        var flushTicks = new List<long>();
        long tick = -1;
        foreach (var chunk in capture.Chunks)
        {
            if (chunk.Kind == CaptureRecordKind.EventTickFileTime) tick++;
            if (chunk.Kind is not (CaptureRecordKind.Inbound or CaptureRecordKind.Outbound or CaptureRecordKind.HandshakeOutbound)) continue;
            if (!connectionOrder.TryGetValue(chunk.Connection, out int order)) connectionOrder[chunk.Connection] = order = connectionOrder.Count;
            if (order == connection && chunk.Kind == CaptureRecordKind.Outbound) flushTicks.Add(tick);
        }
        return flushTicks;
    }
}
