using System;
using System.Collections.Generic;
using Gw2.Protocol.MsgPack;

namespace Gw2.Client.Net.Game;

public enum LifeState : byte
{
    Alive = 0,
    Defeated = 1,
    Downed = 2,
}

public sealed class AgentLifeState
{
    public const int CharacterCreate = 0x25;
    public const int CharacterRemove = 0x29;
    public const int AgentData = 0x2A;
    public const int AgentCommandData = 0x54;
    public const int HealthCreate = 0x100;

    public const byte DefeatedEvent = 0x33;
    public const byte DownedEvent = 0x34;
    public const byte RevivedEvent = 0x49;

    private const int AgentIdField = 1;
    private const int AgentDataFlagsField = 3;
    private const int AgentDataBlobField = 5;
    private const int HealthCreateLifeStateField = 2;
    private const byte AgentEventDataFlag = 0x10;
    private const int DefeatedOrDownedEventLength = 13;
    private const int RevivedEventLength = 3;

    private readonly Dictionary<uint, LifeState> _stateByAgent = [];
    private readonly HashSet<uint> _agentsWithHealth = [];

    public LifeState? StateOf(uint agentId) => _stateByAgent.TryGetValue(agentId, out var state) ? state : null;

    public bool AllowsMovement(uint agentId, bool isInWater) => StateOf(agentId) switch
    {
        LifeState.Defeated => false,
        LifeState.Downed => isInWater,
        _ => true,
    };

    public void Handle(DecodedMessage message)
    {
        if (message.MessageId is not (CharacterCreate or CharacterRemove or HealthCreate or AgentData or AgentCommandData) || message.Fields.Count <= AgentIdField) return;
        var agentId = (uint)Convert.ToUInt64(message.Fields[AgentIdField].Value);
        switch (message.MessageId)
        {
            case CharacterCreate:
                _stateByAgent[agentId] = LifeState.Alive;
                _agentsWithHealth.Remove(agentId);
                return;
            case CharacterRemove:
                _stateByAgent.Remove(agentId);
                _agentsWithHealth.Remove(agentId);
                return;
            case HealthCreate:
                if (_stateByAgent.ContainsKey(agentId) && _agentsWithHealth.Add(agentId) && message.Fields.Count > HealthCreateLifeStateField)
                    _stateByAgent[agentId] = (LifeState)Convert.ToByte(message.Fields[HealthCreateLifeStateField].Value);
                return;
            case AgentData or AgentCommandData:
                if (!_stateByAgent.ContainsKey(agentId) || message.Fields.Count <= AgentDataBlobField) return;
                if ((Convert.ToUInt64(message.Fields[AgentDataFlagsField].Value) & AgentEventDataFlag) == 0) return;
                if (message.Fields[AgentDataBlobField].Value is byte[] blob) ReceiveEvent(agentId, blob);
                return;
        }
    }

    private void ReceiveEvent(uint agentId, byte[] blob)
    {
        if (blob.Length == 0) return;
        var next = (blob[0], blob.Length) switch
        {
            (DefeatedEvent, DefeatedOrDownedEventLength) => LifeState.Defeated,
            (DownedEvent, DefeatedOrDownedEventLength) => LifeState.Downed,
            (RevivedEvent, RevivedEventLength) => LifeState.Alive,
            _ => (LifeState?)null,
        };
        if (next is { } state) _stateByAgent[agentId] = state;
    }
}
