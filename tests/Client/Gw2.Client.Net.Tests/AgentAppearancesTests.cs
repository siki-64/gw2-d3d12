using System;
using System.Linq;
using Gw2.Client.Net.Game;
using Gw2.Protocol.Capture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public sealed class AgentAppearancesTests
{
    [TestMethod]
    public void ReadsPlayerRacesAndLeavesOtherAgentsWithoutOne()
    {
        var decoded = CaptureDecoding.DecodeAll(RecordedSession.Capture(), RecordedSession.Schemas);
        var inbound = decoded.FirstOrDefault(result => result.Corpus == "game" && result.Stream.Direction == CaptureRecordKind.Inbound);
        if (inbound is null) Assert.Inconclusive("the capture holds no game connection");
        var appearances = new AgentAppearances();
        foreach (var message in inbound.Messages) appearances.Handle(message);

        Assert.IsTrue(appearances.All.Count > 0);
        foreach (var (agent, _) in appearances.All)
            Console.WriteLine($"agent {agent} race {appearances.RaceOf(agent)} sex {appearances.SexOf(agent)}");
        Assert.IsTrue(appearances.All.Keys.All(agent => appearances.RaceOf(agent) <= 5 && appearances.SexOf(agent) <= 2));
        Assert.IsTrue(appearances.All.Keys.Any(agent => appearances.RaceOf(agent) < 5 && appearances.SexOf(agent) < 2));
    }
}
