using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Gw2.Protocol.MsgPack;

namespace Gw2.Client.Net.Game;

[Flags]
public enum HitFlags : ushort
{
    None = 0,
    Critical = 0x1,
    KillingBlow = 0x2,
    TargetDowned = 0x4,
    Glancing = 0x10,
}

public readonly record struct AgentHit(uint TargetAgentId, uint SourceAgentId, uint SkillId, int Damage, int BarrierDamage, HitFlags Flags, byte AnimationMoment, byte AnimationMomentIndex, byte DamageCategory)
{
    public const byte NoAnimationMoment = 0xFF;

    public bool IsCritical => (Flags & HitFlags.Critical) != 0;
    public bool IsGlancing => (Flags & HitFlags.Glancing) != 0;
    public bool HitsDownedTarget => (Flags & HitFlags.TargetDowned) != 0;
    public bool IsKillingBlow => (Flags & HitFlags.KillingBlow) != 0;
    public bool WaitsForSwing => AnimationMoment != NoAnimationMoment && AnimationMomentIndex != NoAnimationMoment;
}

public sealed class AgentHits
{
    public const byte HitEvent = 0x32;
    public const int HitEventLength = 22;

    private const int AgentIdField = 1;
    private const int AgentDataFlagsField = 3;
    private const int AgentDataBlobField = 5;
    private const byte AgentEventDataFlag = 0x10;
    private const int SourceAgentOffset = 1;
    private const int SkillIdOffset = 5;
    private const int AnimationMomentOffset = 9;
    private const int AnimationMomentIndexOffset = 10;
    private const int DamageOffset = 11;
    private const int BarrierDamageOffset = 15;
    private const int FlagsOffset = 19;
    private const int DamageCategoryOffset = 21;
    private const int MaximumPendingHits = 256;

    private readonly Queue<AgentHit> _pending = [];

    public void Handle(DecodedMessage message)
    {
        if (message.MessageId is not (AgentLifeState.AgentData or AgentLifeState.AgentCommandData) || message.Fields.Count <= AgentDataBlobField) return;
        if ((Convert.ToUInt64(message.Fields[AgentDataFlagsField].Value) & AgentEventDataFlag) == 0) return;
        if (message.Fields[AgentDataBlobField].Value is not byte[] blob) return;
        if (Decode((uint)Convert.ToUInt64(message.Fields[AgentIdField].Value), blob) is not { } hit) return;
        if (_pending.Count == MaximumPendingHits) _pending.Dequeue();
        _pending.Enqueue(hit);
    }

    public IReadOnlyList<AgentHit> TakeHits()
    {
        var hits = _pending.ToArray();
        _pending.Clear();
        return hits;
    }

    public static AgentHit? Decode(uint targetAgentId, ReadOnlySpan<byte> blob)
    {
        if (blob.Length != HitEventLength || blob[0] != HitEvent) return null;
        return new AgentHit(
            targetAgentId,
            BinaryPrimitives.ReadUInt32LittleEndian(blob[SourceAgentOffset..]),
            BinaryPrimitives.ReadUInt32LittleEndian(blob[SkillIdOffset..]),
            BinaryPrimitives.ReadInt32LittleEndian(blob[DamageOffset..]),
            BinaryPrimitives.ReadInt32LittleEndian(blob[BarrierDamageOffset..]),
            (HitFlags)BinaryPrimitives.ReadUInt16LittleEndian(blob[FlagsOffset..]),
            blob[AnimationMomentOffset],
            blob[AnimationMomentIndexOffset],
            blob[DamageCategoryOffset]);
    }
}
