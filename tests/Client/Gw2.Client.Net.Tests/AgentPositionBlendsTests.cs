using System;
using Gw2.Client.Net.Game;
using Gw2.Protocol.MsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public sealed class AgentPositionBlendsTests
{
    private static readonly byte[] LivePositionBlend = Convert.FromHexString("3B");
    private static readonly byte[] LiveAnimatedDraw = Convert.FromHexString("3103");

    [TestMethod]
    public void TheLiveBlendNamesItsAgent()
    {
        Assert.AreEqual(1094u, AgentPositionBlends.BlendingAgentOf(Message(1094, LivePositionBlend)));
        Assert.IsNull(AgentPositionBlends.BlendingAgentOf(Message(1094, LiveAnimatedDraw)));
    }

    private static DecodedMessage Message(uint agentId, byte[] blob)
    {
        var values = new object[] { (ulong)AgentLifeState.AgentData, (ulong)agentId, 0UL, 0x10UL, 0UL, blob };
        var fields = new DecodedField[values.Length];
        for (int i = 0; i < values.Length; i++) fields[i] = new DecodedField(0, true, values[i]);
        return new DecodedMessage(AgentLifeState.AgentData, 0, 0, fields);
    }
}
