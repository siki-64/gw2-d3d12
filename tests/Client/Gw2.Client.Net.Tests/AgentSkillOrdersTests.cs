using System;
using Gw2.Client.Net.Game;
using Gw2.Protocol.MsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public sealed class AgentSkillOrdersTests
{
    private const uint OwnAgent = 6564;
    private const uint CapturedOrderId = 270419902;
    private const uint CapturedSkillId = 28253;
    private static readonly byte[] CapturedSkillOrderStart = Convert.FromHexString("78215D6E000006000000000001");
    private static readonly byte[] CapturedSkillOrderStep = Convert.FromHexString("78250103");
    private static readonly byte[] CapturedSkillStop = [AgentSkillOrders.SkillStopEvent];

    [TestMethod]
    public void TheCapturedOrderStartsTheSkillAndItsStopEndsIt()
    {
        var orders = new AgentSkillOrders();
        orders.Handle(Message(AgentSkillOrders.AgentOrder, OwnAgent, 0x31u, CapturedOrderId, CapturedSkillOrderStart));
        orders.Handle(Message(AgentSkillOrders.AgentOrder, OwnAgent, 0x33u, CapturedOrderId, CapturedSkillOrderStep));

        Assert.AreEqual(new AgentSkillOrder(CapturedOrderId, CapturedSkillId), orders.OrderOf(OwnAgent));
        Assert.IsFalse(orders.OrderOf(OwnAgent)!.IsStopped);

        orders.Handle(Message(AgentLifeState.AgentCommandData, OwnAgent, 0x32u, CapturedOrderId, CapturedSkillStop));
        Assert.IsTrue(orders.OrderOf(OwnAgent)!.IsStopped);
    }

    [TestMethod]
    public void StopsAndChargeLevelsOfOtherOrdersAreIgnored()
    {
        var orders = new AgentSkillOrders();
        orders.Handle(Message(AgentSkillOrders.AgentOrder, OwnAgent, 0x31u, CapturedOrderId, CapturedSkillOrderStart));
        orders.Handle(Message(AgentLifeState.AgentCommandData, OwnAgent, 0x32u, CapturedOrderId - 1, CapturedSkillStop));
        orders.Handle(Message(AgentLifeState.AgentCommandData, OwnAgent, 0x32u, CapturedOrderId, new byte[] { AgentSkillOrders.ChargeLevelEvent, 2 }));

        Assert.IsFalse(orders.OrderOf(OwnAgent)!.IsStopped);
        Assert.AreEqual(2, orders.OrderOf(OwnAgent)!.ChargeLevel);
    }

    private static DecodedMessage Message(int messageId, uint agentId, uint flags, uint orderId, byte[] data)
    {
        DecodedField[] fields =
        [
            new(0, true, (ulong)messageId),
            new(0, true, (ulong)agentId),
            new(0, true, 0UL),
            new(0, true, (ulong)flags),
            new(0, true, (ulong)orderId),
            new(0, true, data),
        ];
        return new DecodedMessage(messageId, 0, 0, fields);
    }
}
