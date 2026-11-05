using Gw2.Client.Net.Game;
using Gw2.Protocol.MsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public sealed class AgentFixedEffectTriggersTests
{
    [TestMethod]
    public void TheLiveOneByteEventTriggersTheAgent()
    {
        var triggers = new AgentFixedEffectTriggers();
        triggers.Handle(Message(AgentLifeState.AgentCommandData, 3780u, 6750u, 16u, 437332u, new byte[] { 0x46 }));
        CollectionAssert.AreEqual(new uint[] { 3780 }, (System.Collections.ICollection)triggers.TakeTriggeringAgents());
    }

    [TestMethod]
    public void LongerEventsStartingWith0x46AreNotTriggers()
    {
        Assert.IsNull(AgentFixedEffectTriggers.TriggeringAgentOf(Message(AgentLifeState.AgentCommandData, 3780u, 6750u, 16u, 437332u, new byte[] { 0x46, 0x00 })));
    }

    private static DecodedMessage Message(int messageId, params object[] values)
    {
        var fields = new DecodedField[values.Length + 1];
        fields[0] = new DecodedField(0, true, (ulong)messageId);
        for (int i = 0; i < values.Length; i++) fields[i + 1] = new DecodedField(0, true, values[i] is uint u ? (ulong)u : values[i]);
        return new DecodedMessage(messageId, 0, 0, fields);
    }
}
