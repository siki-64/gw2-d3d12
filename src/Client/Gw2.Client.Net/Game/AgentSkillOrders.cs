using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Gw2.Protocol.MsgPack;

namespace Gw2.Client.Net.Game;

public sealed record AgentSkillOrder(uint OrderId, uint SkillId)
{
    public byte ChargeLevel { get; internal set; }
    public bool IsStopped { get; internal set; }
}

public sealed class AgentSkillOrders
{
    public const int AgentOrder = 0x23;
    public const byte SkillOrderKind = 0x78;
    public const byte SkillOrderStart = 0x01;
    public const byte ChargeLevelEvent = 0x44;
    public const byte SkillStopEvent = 0x45;

    private const int AgentIdField = 1;
    private const int FlagsField = 3;
    private const int OrderIdField = 4;
    private const int DataField = 5;
    private const byte AgentEventDataFlag = 0x10;
    private const byte OrderTrackBit = 0x20;
    private const int SkillOrderStartLength = 13;
    private const int SkillOrderSkillIdOffset = 2;
    private const int ChargeLevelEventLength = 2;

    private readonly Dictionary<uint, AgentSkillOrder> _orderByAgent = [];

    public AgentSkillOrder? OrderOf(uint agentId) => _orderByAgent.GetValueOrDefault(agentId);

    public void Handle(DecodedMessage message)
    {
        if (message.MessageId is not (AgentOrder or AgentLifeState.AgentData or AgentLifeState.AgentCommandData) || message.Fields.Count <= DataField) return;
        if (message.Fields[DataField].Value is not byte[] data || data.Length == 0) return;
        var agentId = (uint)Convert.ToUInt64(message.Fields[AgentIdField].Value);
        var orderId = (uint)Convert.ToUInt64(message.Fields[OrderIdField].Value);
        if (message.MessageId == AgentOrder)
        {
            if (data.Length == SkillOrderStartLength && data[0] == SkillOrderKind && (data[1] & ~OrderTrackBit) == SkillOrderStart)
                _orderByAgent[agentId] = new AgentSkillOrder(orderId, BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(SkillOrderSkillIdOffset)));
            return;
        }
        if ((Convert.ToUInt64(message.Fields[FlagsField].Value) & AgentEventDataFlag) == 0) return;
        if (!_orderByAgent.TryGetValue(agentId, out var order) || order.OrderId != orderId) return;
        if (data[0] == SkillStopEvent) order.IsStopped = true;
        else if (data[0] == ChargeLevelEvent && data.Length == ChargeLevelEventLength) order.ChargeLevel = data[1];
    }
}
