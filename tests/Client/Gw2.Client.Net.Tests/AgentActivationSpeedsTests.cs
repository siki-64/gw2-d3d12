using System;
using Gw2.Client.Net.Game;
using Gw2.Protocol.MsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public sealed class AgentActivationSpeedsTests
{
    private static readonly byte[] LiveQuickened = Convert.FromHexString("4A770BBF3F");
    private static readonly byte[] LiveSlowed = Convert.FromHexString("4AABAA2A3F");
    private static readonly byte[] LiveNormal = Convert.FromHexString("4A0000803F");

    [TestMethod]
    public void TheLiveSpeedsDecodeAsActivationMultipliers()
    {
        Assert.AreEqual(1.4925f, AgentActivationSpeeds.Decode(LiveQuickened)!.Value, 0.0001f);
        Assert.AreEqual(2f / 3f, AgentActivationSpeeds.Decode(LiveSlowed)!.Value, 0.0001f);
    }

    [TestMethod]
    public void TheLastSpeedPerAgentWins()
    {
        var speeds = new AgentActivationSpeeds();
        Assert.AreEqual(AgentActivationSpeeds.NormalSpeed, speeds.SpeedOf(709));
        speeds.Handle(Message(709, LiveQuickened));
        Assert.AreEqual(1.4925f, speeds.SpeedOf(709), 0.0001f);
        speeds.Handle(Message(709, LiveNormal));
        Assert.AreEqual(AgentActivationSpeeds.NormalSpeed, speeds.SpeedOf(709));
    }

    private static DecodedMessage Message(uint agentId, byte[] blob)
    {
        var values = new object[] { (ulong)AgentLifeState.AgentCommandData, (ulong)agentId, 0UL, 0x90UL, 0UL, blob };
        var fields = new DecodedField[values.Length];
        for (int i = 0; i < values.Length; i++) fields[i] = new DecodedField(0, true, values[i]);
        return new DecodedMessage(AgentLifeState.AgentCommandData, 0, 0, fields);
    }
}
