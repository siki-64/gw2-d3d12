using System;
using Gw2.Client.Net.Game;
using Gw2.Protocol.MsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public sealed class GadgetCombatEventsTests
{
    private static readonly byte[] LiveGadgetHit = Convert.FromHexString("8E0000234300000080880500000C11000005000005");
    private static readonly byte[] LiveGadgetHealth = Convert.FromHexString("9400007844000000000A03B34600000000");

    [TestMethod]
    public void TheLiveGadgetHitCarriesDamageSkillAndAttacker()
    {
        var hit = GadgetCombatEvents.DecodeHit(764, LiveGadgetHit);
        Assert.AreEqual(new GadgetHit(764, 163f, -0f, 1416, 4364, 5, 0, 5), hit);
    }

    [TestMethod]
    public void TheLiveGadgetHealthSetIsKeptPerGadget()
    {
        var events = new GadgetCombatEvents();
        events.Handle(Message(AgentLifeState.AgentCommandData, 764u, 572839u, 16u, 17751433u, LiveGadgetHealth));
        var health = events.HealthOf(764) ?? throw new AssertFailedException("health expected");
        Assert.AreEqual(992f, health.Current);
        Assert.AreEqual(0f, health.Barrier);
        Assert.AreEqual(0f, health.MeterElapsedSeconds);
        Assert.AreEqual(0, events.TakeHits().Count);
    }

    [TestMethod]
    public void TheOneByteDetachEventQueuesTheGadget()
    {
        var events = new GadgetCombatEvents();
        events.Handle(Message(AgentLifeState.AgentCommandData, 764u, 572839u, 16u, 17751433u, new byte[] { GadgetCombatEvents.DetachEvent }));
        CollectionAssert.AreEqual(new uint[] { 764 }, (System.Collections.ICollection)events.TakeDetachedGadgets());
    }

    [TestMethod]
    public void CharacterHitsAreNotGadgetHits()
    {
        Assert.IsNull(GadgetCombatEvents.DecodeHit(821, Convert.FromHexString("32C50200008171000000030B04000000000000010005")));
    }

    private static DecodedMessage Message(int messageId, params object[] values)
    {
        var fields = new DecodedField[values.Length + 1];
        fields[0] = new DecodedField(0, true, (ulong)messageId);
        for (int i = 0; i < values.Length; i++) fields[i + 1] = new DecodedField(0, true, values[i] is uint u ? (ulong)u : values[i]);
        return new DecodedMessage(messageId, 0, 0, fields);
    }
}
