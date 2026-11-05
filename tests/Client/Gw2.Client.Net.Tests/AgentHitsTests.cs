using System;
using Gw2.Client.Net.Game;
using Gw2.Protocol.MsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public sealed class AgentHitsTests
{
    private static readonly byte[] LiveCriticalHit = Convert.FromHexString("32C50200008171000000030B04000000000000010005");
    private static readonly byte[] LiveNormalHit = Convert.FromHexString("32C5020000817100000003E401000000000000000005");

    [TestMethod]
    public void TheLiveCriticalHitCarriesTheCriticalFlag()
    {
        var hits = new AgentHits();
        hits.Handle(Message(AgentLifeState.AgentCommandData, 821u, 449594u, 144u, 12763201u, LiveCriticalHit));
        var hit = hits.TakeHits() is [var only] ? only : throw new AssertFailedException("one hit expected");
        Assert.AreEqual(new AgentHit(821, 709, 29057, 1035, 0, HitFlags.Critical, 0, 3, 5), hit);
        Assert.IsTrue(hit.IsCritical);
        Assert.IsTrue(hit.WaitsForSwing);
    }

    [TestMethod]
    public void TheLiveNormalHitOfTheSameSkillIsNotCritical()
    {
        var hit = AgentHits.Decode(826, LiveNormalHit);
        Assert.AreEqual(new AgentHit(826, 709, 29057, 484, 0, HitFlags.None, 0, 3, 5), hit);
    }

    [TestMethod]
    public void TheLiveHitOnADownedTargetCarriesTheDownedFlag()
    {
        var hit = AgentHits.Decode(1058, Convert.FromHexString("325A020000663D0000FFFF2901000000000000040002"));
        Assert.AreEqual(new AgentHit(1058, 602, 15718, 297, 0, HitFlags.TargetDowned, 0xFF, 0xFF, 2), hit);
        Assert.IsTrue(hit!.Value.HitsDownedTarget);
    }

    [TestMethod]
    public void HitsWithoutTheEventFlagAreIgnored()
    {
        var hits = new AgentHits();
        hits.Handle(Message(AgentLifeState.AgentCommandData, 821u, 449594u, 0x80u, 12763201u, LiveCriticalHit));
        Assert.AreEqual(0, hits.TakeHits().Count);
    }

    [TestMethod]
    public void OtherAgentEventsAreNotHits()
    {
        Assert.IsNull(AgentHits.Decode(6939, [0x33, 0x00, 0x00, 0x05, 0xDE, 0x30, 0x00, 0x00, 0x1A, 0x1B, 0x00, 0x00, 0x00]));
    }

    private static DecodedMessage Message(int messageId, params object[] values)
    {
        var fields = new DecodedField[values.Length + 1];
        fields[0] = new DecodedField(0, true, (ulong)messageId);
        for (int i = 0; i < values.Length; i++) fields[i + 1] = new DecodedField(0, true, values[i] is uint u ? (ulong)u : values[i]);
        return new DecodedMessage(messageId, 0, 0, fields);
    }
}
