using System;
using System.Linq;
using Gw2.Client.Net.Game;
using Gw2.Protocol.MsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public sealed class AgentAvoidancesTests
{
    private static readonly byte[] LiveBlock = Convert.FromHexString("2D1B030000DA380000");
    private static readonly byte[] LiveInvulnerable = Convert.FromHexString("3D0E04000041290000");
    private static readonly byte[] LiveMiss = Convert.FromHexString("420204000038160000");
    private static readonly byte[] LiveKillingBlow = Convert.FromHexString("32E2200000F72C0100FFFF8A16000000000000030005");
    private static readonly byte[] LiveGlancingHit = Convert.FromHexString("32F7020000EB270000FFFF9100000000000000100000");

    [TestMethod]
    public void TheLiveBlockNamesAttackerAndSkill()
    {
        Assert.AreEqual(new AgentAvoidance(4100, 795, 14554, AvoidanceKind.Block), AgentAvoidances.Decode(4100, LiveBlock));
    }

    [TestMethod]
    public void TheLiveEventsAreQueuedInOrder()
    {
        var avoidances = new AgentAvoidances();
        avoidances.Handle(Message(4100u, LiveInvulnerable));
        avoidances.Handle(Message(4100u, LiveMiss));
        var taken = avoidances.TakeAvoidances();
        CollectionAssert.AreEqual(new[] { AvoidanceKind.Invulnerable, AvoidanceKind.Miss }, taken.Select(avoidance => avoidance.Kind).ToArray());
        Assert.AreEqual(10561u, taken[0].SkillId);
        Assert.AreEqual(1026u, taken[1].AttackingAgentId);
        Assert.AreEqual(0, avoidances.TakeAvoidances().Count);
    }

    [TestMethod]
    public void AHitIsNotAnAvoidance()
    {
        Assert.IsNull(AgentAvoidances.Decode(4100, LiveGlancingHit));
    }

    [TestMethod]
    public void TheLiveGlancingHitCarriesTheGlancingFlag()
    {
        var hit = AgentHits.Decode(4100, LiveGlancingHit)!.Value;
        Assert.IsTrue(hit.IsGlancing);
        Assert.IsFalse(hit.IsCritical);
        Assert.AreEqual(145, hit.Damage);
    }

    [TestMethod]
    public void TheLiveKillingBlowCarriesItsFlagAndCategory()
    {
        var hit = AgentHits.Decode(5470, LiveKillingBlow)!.Value;
        Assert.IsTrue(hit.IsKillingBlow);
        Assert.IsTrue(hit.IsCritical);
        Assert.AreEqual(5, hit.DamageCategory);
        Assert.AreEqual(5770, hit.Damage);
    }

    private static DecodedMessage Message(uint agentId, byte[] blob)
    {
        var values = new object[] { (ulong)AgentLifeState.AgentCommandData, (ulong)agentId, 0UL, 0x90UL, 0UL, blob };
        var fields = new DecodedField[values.Length];
        for (int i = 0; i < values.Length; i++) fields[i] = new DecodedField(0, true, values[i]);
        return new DecodedMessage(AgentLifeState.AgentCommandData, 0, 0, fields);
    }
}
