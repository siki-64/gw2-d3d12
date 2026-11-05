using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Gw2.Protocol.MsgPack;

namespace Gw2.Client.Net.Game;

public enum AvoidanceKind : byte
{
    Block = 0x2D,
    Evade = 0x38,
    Interrupt = 0x3C,
    Invulnerable = 0x3D,
    Miss = 0x42,
    MissShownToAvoider = 0x43,
}

public readonly record struct AgentAvoidance(uint AvoidingAgentId, uint AttackingAgentId, uint SkillId, AvoidanceKind Kind);

public sealed class AgentAvoidances
{
    public const int AvoidanceEventLength = 9;

    private const int AgentIdField = 1;
    private const int AgentDataFlagsField = 3;
    private const int AgentDataBlobField = 5;
    private const byte AgentEventDataFlag = 0x10;
    private const int AttackingAgentOffset = 1;
    private const int SkillIdOffset = 5;
    private const int MaximumPendingAvoidances = 256;

    private readonly Queue<AgentAvoidance> _pending = [];

    public void Handle(DecodedMessage message)
    {
        if (message.MessageId is not (AgentLifeState.AgentData or AgentLifeState.AgentCommandData) || message.Fields.Count <= AgentDataBlobField) return;
        if ((Convert.ToUInt64(message.Fields[AgentDataFlagsField].Value) & AgentEventDataFlag) == 0) return;
        if (message.Fields[AgentDataBlobField].Value is not byte[] blob) return;
        if (Decode((uint)Convert.ToUInt64(message.Fields[AgentIdField].Value), blob) is not { } avoidance) return;
        if (_pending.Count == MaximumPendingAvoidances) _pending.Dequeue();
        _pending.Enqueue(avoidance);
    }

    public IReadOnlyList<AgentAvoidance> TakeAvoidances()
    {
        var avoidances = _pending.ToArray();
        _pending.Clear();
        return avoidances;
    }

    public static AgentAvoidance? Decode(uint avoidingAgentId, ReadOnlySpan<byte> blob)
    {
        if (blob.Length != AvoidanceEventLength || !Enum.IsDefined((AvoidanceKind)blob[0])) return null;
        return new AgentAvoidance(
            avoidingAgentId,
            BinaryPrimitives.ReadUInt32LittleEndian(blob[AttackingAgentOffset..]),
            BinaryPrimitives.ReadUInt32LittleEndian(blob[SkillIdOffset..]),
            (AvoidanceKind)blob[0]);
    }
}
