using Gw2.Client.Net.Game;
using Gw2.Protocol.MsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public sealed class AgentEnduranceTests
{
    [TestMethod]
    public void RefusesTheDodgeBelowTheCostAsRecorded()
    {
        var endurance = new AgentEndurance();
        Assert.IsTrue(endurance.CanDodge(6550));
        endurance.Handle(Message(AgentEndurance.EnduranceCreate, 6550, 100, 100, 100, 50));
        Assert.IsTrue(endurance.CanDodge(6550));
        endurance.Handle(Message(AgentEndurance.EnduranceSet, 6550, 6));
        Assert.IsFalse(endurance.CanDodge(6550));
        endurance.Handle(Message(AgentEndurance.EnduranceSet, 6550, 50));
        Assert.IsTrue(endurance.CanDodge(6550));
        Assert.AreEqual(50u, endurance.PointsOf(6550));
    }

    private static DecodedMessage Message(int messageId, params ulong[] values)
    {
        var fields = new DecodedField[values.Length + 1];
        fields[0] = new DecodedField(0, true, (ulong)messageId);
        for (int i = 0; i < values.Length; i++) fields[i + 1] = new DecodedField(0, true, values[i]);
        return new DecodedMessage(messageId, 0, 0, fields);
    }
}
