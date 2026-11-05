using System;
using Gw2.Client.Net.Game;
using Gw2.Protocol.MsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public sealed class AgentWeaponStancesTests
{
    private static readonly byte[] LiveAnimatedDraw = Convert.FromHexString("3103");
    private static readonly byte[] LiveAnimatedStow = Convert.FromHexString("3102");
    private static readonly byte[] LiveInstantStow = Convert.FromHexString("3100");

    [TestMethod]
    public void TheLiveAnimatedDrawDrawsWeapons()
    {
        var stance = AgentWeaponStances.Decode(709, LiveAnimatedDraw)!.Value;
        Assert.IsTrue(stance.WeaponsDrawn);
        Assert.IsTrue(stance.AnimatesChange);
    }

    [TestMethod]
    public void TheLastChangePerAgentWins()
    {
        var stances = new AgentWeaponStances();
        stances.Handle(Message(709, LiveAnimatedDraw));
        Assert.IsTrue(stances.WeaponsDrawn(709));
        stances.Handle(Message(709, LiveAnimatedStow));
        Assert.IsFalse(stances.WeaponsDrawn(709));
        stances.Handle(Message(709, LiveInstantStow));
        Assert.AreEqual(3, stances.TakeChanges().Count);
        Assert.IsFalse(AgentWeaponStances.Decode(709, LiveInstantStow)!.Value.AnimatesChange);
    }

    private static DecodedMessage Message(uint agentId, byte[] blob)
    {
        var values = new object[] { (ulong)AgentLifeState.AgentCommandData, (ulong)agentId, 0UL, 0x90UL, 0UL, blob };
        var fields = new DecodedField[values.Length];
        for (int i = 0; i < values.Length; i++) fields[i] = new DecodedField(0, true, values[i]);
        return new DecodedMessage(AgentLifeState.AgentCommandData, 0, 0, fields);
    }
}
