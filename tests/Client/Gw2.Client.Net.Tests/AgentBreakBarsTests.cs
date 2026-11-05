using System;
using Gw2.Client.Net.Game;
using Gw2.Protocol.MsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public sealed class AgentBreakBarsTests
{
    private static readonly byte[] LiveBreakBarSet = Convert.FromHexString("2E0000803F007CCA0000");
    private static readonly byte[] LiveBreakBarHit = Convert.FromHexString("303333333F00004040160300000000000000CE6D0000");
    private static readonly byte[] LiveBreak = Convert.FromHexString("300000000000004040590300000000000001F66E0000");
    private static readonly byte[] LiveRecoveryTick = Convert.FromHexString("306E12033C0AD7A33D00000000000000000100000000");
    private static readonly byte[] LiveBreakBarRemove = Convert.FromHexString("2F");

    [TestMethod]
    public void TheLiveSetCreatesAFullBar()
    {
        var bars = new AgentBreakBars();
        bars.Handle(Message(4100u, LiveBreakBarSet));
        Assert.AreEqual(new AgentBreakBar(1f, BreakBarState.Active, 51836), bars.BreakBarOf(4100));
    }

    [TestMethod]
    public void TheLiveHitLowersTheBar()
    {
        var bars = new AgentBreakBars();
        bars.Handle(Message(4100u, LiveBreakBarSet));
        bars.Handle(Message(4100u, LiveBreakBarHit));
        Assert.AreEqual(0.7f, bars.BreakBarOf(4100)!.Value.FractionFull, 1e-6f);
        Assert.AreEqual(28110u, bars.BreakBarOf(4100)!.Value.SkillId);
    }

    [TestMethod]
    public void TheLiveBreakEmptiesTheBarAndStartsRecovery()
    {
        var bars = new AgentBreakBars();
        bars.Handle(Message(1163u, LiveBreakBarSet));
        bars.Handle(Message(1163u, LiveBreak));
        Assert.AreEqual(new AgentBreakBar(0f, BreakBarState.Recovering, 28406), bars.BreakBarOf(1163));
        bars.Handle(Message(1163u, LiveRecoveryTick));
        Assert.AreEqual(BreakBarState.Recovering, bars.BreakBarOf(1163)!.Value.State);
        Assert.AreEqual(0.008f, bars.BreakBarOf(1163)!.Value.FractionFull, 1e-4f);
    }

    [TestMethod]
    public void AChangeWithoutABarIsIgnored()
    {
        var bars = new AgentBreakBars();
        bars.Handle(Message(4100u, LiveBreakBarHit));
        Assert.IsNull(bars.BreakBarOf(4100));
    }

    [TestMethod]
    public void TheLiveRemoveDropsTheBar()
    {
        var bars = new AgentBreakBars();
        bars.Handle(Message(4100u, LiveBreakBarSet));
        bars.Handle(Message(4100u, LiveBreakBarRemove));
        Assert.IsNull(bars.BreakBarOf(4100));
    }

    private static DecodedMessage Message(uint agentId, byte[] blob)
    {
        var values = new object[] { (ulong)AgentLifeState.AgentCommandData, (ulong)agentId, 0UL, 0x90UL, 0UL, blob };
        var fields = new DecodedField[values.Length];
        for (int i = 0; i < values.Length; i++) fields[i] = new DecodedField(0, true, values[i]);
        return new DecodedMessage(AgentLifeState.AgentCommandData, 0, 0, fields);
    }
}
