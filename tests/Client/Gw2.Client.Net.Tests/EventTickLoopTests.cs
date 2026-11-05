using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Gw2.Client.Net.Ticks;
using Gw2.Protocol.Capture;
using Gw2.Protocol.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public class EventTickLoopTests
{
    [TestMethod]
    public void AStepAdvancesAtMostFiveHundredMillisecondsAndCarriesTheRest()
    {
        var clock = new EventTickClock(1000, limitsBacklog: false);

        Assert.AreEqual(500u, clock.Advance(new EventTickTime(2200, 0)).AdvancedMilliseconds);
        Assert.AreEqual(700u, clock.BacklogMilliseconds);
        Assert.AreEqual(500u, clock.Advance(new EventTickTime(2300, 0)).AdvancedMilliseconds);
        Assert.AreEqual(300u, clock.BacklogMilliseconds);
        Assert.AreEqual(300u, clock.Advance(new EventTickTime(2300, 0)).AdvancedMilliseconds);
        Assert.AreEqual(0u, clock.BacklogMilliseconds);
    }

    [TestMethod]
    public void ALimitedBacklogDropsMoreThanFiveHundredMillisecondsOfOverflow()
    {
        var clock = new EventTickClock(0, limitsBacklog: true);

        Assert.AreEqual(500u, clock.Advance(new EventTickTime(3000, 0)).AdvancedMilliseconds);
        Assert.AreEqual(500u, clock.BacklogMilliseconds);
    }

    [TestMethod]
    public void TimeGoingBackwardsAdvancesNothing()
    {
        var clock = new EventTickClock(5000, limitsBacklog: false);

        Assert.AreEqual(0u, clock.Advance(new EventTickTime(4000, 0)).AdvancedMilliseconds);
        Assert.AreEqual(10u, clock.Advance(new EventTickTime(4010, 0)).AdvancedMilliseconds);
    }

    [TestMethod]
    public void TheRecordedTicksAdvanceByTheRecordedTime()
    {
        var ticks = RecordedTicks(RecordedSession.Capture());
        var clock = new EventTickClock(ticks[0].Milliseconds, limitsBacklog: false);
        ulong advanced = 0;
        foreach (var tick in ticks) advanced += clock.Advance(tick).AdvancedMilliseconds;

        Assert.AreEqual((ulong)(ticks[^1].Milliseconds - ticks[0].Milliseconds), advanced + clock.BacklogMilliseconds);
    }

    [TestMethod]
    public void ReplayedTicksFlushTheRecordedSendsInTheRecordedTicks()
    {
        var capture = RecordedSession.Capture();
        var ticks = RecordedTicks(capture);
        var flushTicksByConnection = RecordedFlushTicks(capture);
        var outbound = CaptureDecoding.DecodeAll(capture, RecordedSession.Schemas).Where(result => result.Stream.Direction == CaptureRecordKind.Outbound).ToList();
        Assert.IsTrue(outbound.Count > 0, "the capture holds no outbound streams");

        foreach (var recorded in outbound)
        {
            var flushTicks = flushTicksByConnection[recorded.Stream.ConnectionOrder];
            var corpus = recorded.Corpus == "login" ? RecordedSession.Schemas.Login : RecordedSession.Schemas.Game;
            var seed = RandomNumberGenerator.GetBytes(HandshakeKeyExchange.SeedLength);
            var server = new LoopbackServer(RecordedSession.Group, seed);
            var client = new ProtocolConnection(corpus, RecordedSession.Group, seed, server.OnClientBytes);
            client.Receive(server.KeyFrame());

            var messagesByTick = recorded.Messages.ToLookup(message => flushTicks[recorded.Stream.ChunkAt(message.WireOffset + message.WireLength - 1)]);
            var flushedTicks = new List<long>();
            var loop = new EventTickLoop(new RecordedEventTickTimeSource(ticks), limitsBacklog: false);
            loop.Attach(client);
            loop.Ticked += _ =>
            {
                foreach (var message in messagesByTick[loop.TickCount]) client.Send(message.MessageId, message.Fields);
            };
            while (true)
            {
                var flushedBefore = server.ReceivedPlaintext.Count;
                if (!loop.RunOnce()) break;
                for (var flush = flushedBefore; flush < server.ReceivedPlaintext.Count; flush++) flushedTicks.Add(loop.TickCount - 1);
            }

            var recordedChunks = new List<string>();
            var start = 0;
            foreach (var end in recorded.Stream.ChunkEnds())
            {
                recordedChunks.Add(Convert.ToHexString(recorded.Stream.Bytes[start..end]));
                start = end;
            }
            Console.WriteLine($"connection {recorded.Stream.ConnectionOrder}: {recordedChunks.Count} recorded flushes, {server.ReceivedPlaintext.Count} replayed over {loop.TickCount} ticks");
            CollectionAssert.AreEqual(recordedChunks, server.ReceivedPlaintext.Select(Convert.ToHexString).ToList(), $"connection {recorded.Stream.ConnectionOrder} flush bytes");
            CollectionAssert.AreEqual(flushTicks, flushedTicks, $"connection {recorded.Stream.ConnectionOrder} flush ticks");
        }
    }

    private static List<EventTickTime> RecordedTicks(NetworkCapture capture)
    {
        var ticks = capture.EventTicks().Select(tick => new EventTickTime(tick.Milliseconds, tick.FileTime)).ToList();
        if (ticks.Count == 0) Assert.Inconclusive("the capture holds no event tick records");
        return ticks;
    }

    private static Dictionary<int, List<long>> RecordedFlushTicks(NetworkCapture capture)
    {
        var connectionOrder = new Dictionary<ulong, int>();
        var flushTicks = new Dictionary<int, List<long>>();
        long tick = -1;
        foreach (var chunk in capture.Chunks)
        {
            if (chunk.Kind == CaptureRecordKind.EventTickFileTime) tick++;
            if (chunk.Kind is not (CaptureRecordKind.Inbound or CaptureRecordKind.Outbound or CaptureRecordKind.HandshakeOutbound)) continue;
            if (!connectionOrder.TryGetValue(chunk.Connection, out int order)) connectionOrder[chunk.Connection] = order = connectionOrder.Count;
            if (chunk.Kind != CaptureRecordKind.Outbound) continue;
            if (!flushTicks.TryGetValue(order, out var list)) flushTicks[order] = list = new List<long>();
            list.Add(tick);
        }
        return flushTicks;
    }
}
