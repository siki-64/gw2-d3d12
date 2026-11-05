using System;
using System.Collections.Generic;
using Gw2.Protocol.MsgPack;

namespace Gw2.Client.Net.Game;

[Flags]
public enum WeaponStanceFlags : byte
{
    None = 0,
    WeaponsDrawn = 0x1,
    AnimateChange = 0x2,
    AlternateAnimation = 0x4,
}

public readonly record struct AgentWeaponStance(uint AgentId, WeaponStanceFlags Flags)
{
    public bool WeaponsDrawn => Flags.HasFlag(WeaponStanceFlags.WeaponsDrawn);

    public bool AnimatesChange => Flags.HasFlag(WeaponStanceFlags.AnimateChange);
}

public sealed class AgentWeaponStances
{
    public const byte WeaponStanceEvent = 0x31;
    public const int WeaponStanceEventLength = 2;

    private const int AgentIdField = 1;
    private const int AgentDataFlagsField = 3;
    private const int AgentDataBlobField = 5;
    private const byte AgentEventDataFlag = 0x10;
    private const int FlagsOffset = 1;
    private const int MaximumPendingChanges = 256;

    private readonly Dictionary<uint, AgentWeaponStance> _stanceByAgent = [];
    private readonly Queue<AgentWeaponStance> _pending = [];

    public bool WeaponsDrawn(uint agentId) => _stanceByAgent.TryGetValue(agentId, out var stance) && stance.WeaponsDrawn;

    public void Handle(DecodedMessage message)
    {
        if (message.MessageId is not (AgentLifeState.AgentData or AgentLifeState.AgentCommandData) || message.Fields.Count <= AgentDataBlobField) return;
        if ((Convert.ToUInt64(message.Fields[AgentDataFlagsField].Value) & AgentEventDataFlag) == 0) return;
        if (message.Fields[AgentDataBlobField].Value is not byte[] blob) return;
        if (Decode((uint)Convert.ToUInt64(message.Fields[AgentIdField].Value), blob) is not { } stance) return;
        _stanceByAgent[stance.AgentId] = stance;
        if (_pending.Count == MaximumPendingChanges) _pending.Dequeue();
        _pending.Enqueue(stance);
    }

    public IReadOnlyList<AgentWeaponStance> TakeChanges()
    {
        var changes = _pending.ToArray();
        _pending.Clear();
        return changes;
    }

    public static AgentWeaponStance? Decode(uint agentId, ReadOnlySpan<byte> blob) =>
        blob.Length == WeaponStanceEventLength && blob[0] == WeaponStanceEvent
            ? new AgentWeaponStance(agentId, (WeaponStanceFlags)blob[FlagsOffset])
            : null;
}
