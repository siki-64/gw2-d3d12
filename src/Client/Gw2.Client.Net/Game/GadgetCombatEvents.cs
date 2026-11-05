using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Gw2.Protocol.MsgPack;

namespace Gw2.Client.Net.Game;

public readonly record struct GadgetHit(uint GadgetAgentId, float Damage, float BarrierDamage, uint SkillId, uint AttackingAgentId, byte DamageCategory, ushort Flags, byte Trailer);

public readonly record struct GadgetHealth(float Current, float Barrier, float MeterValue, float MeterElapsedSeconds);

public sealed class GadgetCombatEvents
{
    public const byte HitEvent = 0x8E;
    public const int HitEventLength = 21;
    public const byte HealthEvent = 0x94;
    public const int HealthEventLength = 17;
    public const byte DetachEvent = 0x84;
    public const int DetachEventLength = 1;

    private const int AgentIdField = 1;
    private const int AgentDataFlagsField = 3;
    private const int AgentDataBlobField = 5;
    private const byte AgentEventDataFlag = 0x10;
    private const int MaximumPendingHits = 256;

    private readonly Queue<GadgetHit> _pendingHits = [];
    private readonly Dictionary<uint, GadgetHealth> _healthByGadget = [];
    private readonly Queue<uint> _pendingDetaches = [];
    private readonly Queue<AgentSkillEffect> _pendingEffects = [];

    public GadgetHealth? HealthOf(uint gadgetAgentId) => _healthByGadget.TryGetValue(gadgetAgentId, out var health) ? health : null;

    public void Handle(DecodedMessage message)
    {
        if (message.MessageId is not (AgentLifeState.AgentData or AgentLifeState.AgentCommandData) || message.Fields.Count <= AgentDataBlobField) return;
        if ((Convert.ToUInt64(message.Fields[AgentDataFlagsField].Value) & AgentEventDataFlag) == 0) return;
        if (message.Fields[AgentDataBlobField].Value is not byte[] blob) return;
        var gadgetAgentId = (uint)Convert.ToUInt64(message.Fields[AgentIdField].Value);
        if (DecodeHit(gadgetAgentId, blob) is { } hit)
        {
            if (_pendingHits.Count == MaximumPendingHits) _pendingHits.Dequeue();
            _pendingHits.Enqueue(hit);
        }
        else if (DecodeHealth(blob) is { } health) _healthByGadget[gadgetAgentId] = health;
        else if (IsDetach(blob)) Enqueue(_pendingDetaches, gadgetAgentId);
        else if (AgentSkillEffects.DecodeGadgetEffect(blob) is { } effect) Enqueue(_pendingEffects, effect);
    }

    private static void Enqueue<T>(Queue<T> pending, T item)
    {
        if (pending.Count == MaximumPendingHits) pending.Dequeue();
        pending.Enqueue(item);
    }

    public static bool IsDetach(ReadOnlySpan<byte> blob) => blob.Length == DetachEventLength && blob[0] == DetachEvent;

    public IReadOnlyList<uint> TakeDetachedGadgets()
    {
        var detached = _pendingDetaches.ToArray();
        _pendingDetaches.Clear();
        return detached;
    }

    public IReadOnlyList<AgentSkillEffect> TakeStartedEffects()
    {
        var effects = _pendingEffects.ToArray();
        _pendingEffects.Clear();
        return effects;
    }

    public IReadOnlyList<GadgetHit> TakeHits()
    {
        var hits = _pendingHits.ToArray();
        _pendingHits.Clear();
        return hits;
    }

    public static GadgetHit? DecodeHit(uint gadgetAgentId, ReadOnlySpan<byte> blob)
    {
        if (blob.Length != HitEventLength || blob[0] != HitEvent) return null;
        return new GadgetHit(
            gadgetAgentId,
            BinaryPrimitives.ReadSingleLittleEndian(blob[1..]),
            BinaryPrimitives.ReadSingleLittleEndian(blob[5..]),
            BinaryPrimitives.ReadUInt32LittleEndian(blob[9..]),
            BinaryPrimitives.ReadUInt32LittleEndian(blob[13..]),
            blob[17],
            BinaryPrimitives.ReadUInt16LittleEndian(blob[18..]),
            blob[20]);
    }

    public static GadgetHealth? DecodeHealth(ReadOnlySpan<byte> blob)
    {
        if (blob.Length != HealthEventLength || blob[0] != HealthEvent) return null;
        return new GadgetHealth(
            BinaryPrimitives.ReadSingleLittleEndian(blob[1..]),
            BinaryPrimitives.ReadSingleLittleEndian(blob[5..]),
            BinaryPrimitives.ReadSingleLittleEndian(blob[9..]),
            BinaryPrimitives.ReadSingleLittleEndian(blob[13..]));
    }
}
