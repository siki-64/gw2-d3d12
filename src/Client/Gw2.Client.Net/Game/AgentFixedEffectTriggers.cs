using System;
using System.Collections.Generic;
using Gw2.Protocol.MsgPack;

namespace Gw2.Client.Net.Game;

public sealed class AgentFixedEffectTriggers
{
    public const byte FixedEffectEvent = 0x46;
    public const int FixedEffectEventLength = 1;

    private const int AgentIdField = 1;
    private const int AgentDataFlagsField = 3;
    private const int AgentDataBlobField = 5;
    private const byte AgentEventDataFlag = 0x10;
    private const int MaximumPendingTriggers = 256;

    private readonly Queue<uint> _pendingAgents = [];

    public void Handle(DecodedMessage message)
    {
        if (TriggeringAgentOf(message) is not { } agentId) return;
        if (_pendingAgents.Count == MaximumPendingTriggers) _pendingAgents.Dequeue();
        _pendingAgents.Enqueue(agentId);
    }

    public IReadOnlyList<uint> TakeTriggeringAgents()
    {
        var agents = _pendingAgents.ToArray();
        _pendingAgents.Clear();
        return agents;
    }

    public static uint? TriggeringAgentOf(DecodedMessage message)
    {
        if (message.MessageId is not (AgentLifeState.AgentData or AgentLifeState.AgentCommandData) || message.Fields.Count <= AgentDataBlobField) return null;
        if ((Convert.ToUInt64(message.Fields[AgentDataFlagsField].Value) & AgentEventDataFlag) == 0) return null;
        if (message.Fields[AgentDataBlobField].Value is not byte[] { Length: FixedEffectEventLength } blob || blob[0] != FixedEffectEvent) return null;
        return (uint)Convert.ToUInt64(message.Fields[AgentIdField].Value);
    }
}
