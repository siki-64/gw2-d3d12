using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Gw2.Protocol.MsgPack;

namespace Gw2.Client.Net.Game;

public readonly record struct AgentSkillEffect(uint SourceAgentId, uint TargetAgentId, uint EffectId, uint DurationMilliseconds, uint Flags, byte Mode, uint TrackedEffectId)
{
    public const uint Untracked = 0;
    public const byte ContentDirectionMode = 2;

    public bool IsTracked => TrackedEffectId != Untracked;

    public bool PlaysOnlyDuringContentDirection => Mode == ContentDirectionMode;
}

public sealed class AgentSkillEffects
{
    public const byte SkillEffectStartEvent = 0x36;
    public const byte TrackedEffectEndEvent = 0x35;
    public const int SkillEffectStartLength = 123;
    public const int TrackedEffectEndLength = 5;
    public const byte GadgetSkillEffectEvent = 0x90;
    public const int GadgetSkillEffectLength = 67;

    private const int AgentDataFlagsField = 3;
    private const int AgentDataBlobField = 5;
    private const byte AgentEventDataFlag = 0x10;
    private const int SourceAgentOffset = 1;
    private const int TargetAgentOffset = 5;
    private const int EffectIdOffset = 9;
    private const int DurationOffset = 13;
    private const int FlagsOffset = 17;
    private const int ModeOffset = 21;
    private const int TrackedEffectIdOffset = 54;
    private const int EndedTrackedEffectIdOffset = 1;
    private const int MaximumPendingEffects = 256;

    private readonly Queue<AgentSkillEffect> _pending = [];
    private readonly Dictionary<uint, AgentSkillEffect> _trackedById = [];

    public IReadOnlyDictionary<uint, AgentSkillEffect> TrackedEffects => _trackedById;

    public void Handle(DecodedMessage message)
    {
        if (message.MessageId is not (AgentLifeState.AgentData or AgentLifeState.AgentCommandData) || message.Fields.Count <= AgentDataBlobField) return;
        if ((Convert.ToUInt64(message.Fields[AgentDataFlagsField].Value) & AgentEventDataFlag) == 0) return;
        if (message.Fields[AgentDataBlobField].Value is not byte[] blob) return;
        if (EndedTrackedEffectId(blob) is { } endedId)
        {
            _trackedById.Remove(endedId);
            return;
        }
        if (Decode(blob) is not { } effect) return;
        if (effect.IsTracked) _trackedById[effect.TrackedEffectId] = effect;
        if (_pending.Count == MaximumPendingEffects) _pending.Dequeue();
        _pending.Enqueue(effect);
    }

    public IReadOnlyList<AgentSkillEffect> TakeStartedEffects()
    {
        var effects = _pending.ToArray();
        _pending.Clear();
        return effects;
    }

    public static AgentSkillEffect? Decode(ReadOnlySpan<byte> blob)
    {
        if (blob.Length != SkillEffectStartLength || blob[0] != SkillEffectStartEvent) return null;
        return DecodeSharedLayout(blob);
    }

    public static AgentSkillEffect? DecodeGadgetEffect(ReadOnlySpan<byte> blob)
    {
        if (blob.Length != GadgetSkillEffectLength || blob[0] != GadgetSkillEffectEvent) return null;
        return DecodeSharedLayout(blob);
    }

    private static AgentSkillEffect DecodeSharedLayout(ReadOnlySpan<byte> blob)
    {
        return new AgentSkillEffect(
            BinaryPrimitives.ReadUInt32LittleEndian(blob[SourceAgentOffset..]),
            BinaryPrimitives.ReadUInt32LittleEndian(blob[TargetAgentOffset..]),
            BinaryPrimitives.ReadUInt32LittleEndian(blob[EffectIdOffset..]),
            BinaryPrimitives.ReadUInt32LittleEndian(blob[DurationOffset..]),
            BinaryPrimitives.ReadUInt32LittleEndian(blob[FlagsOffset..]),
            blob[ModeOffset],
            BinaryPrimitives.ReadUInt32LittleEndian(blob[TrackedEffectIdOffset..]));
    }

    public static uint? EndedTrackedEffectId(ReadOnlySpan<byte> blob) =>
        blob.Length == TrackedEffectEndLength && blob[0] == TrackedEffectEndEvent
            ? BinaryPrimitives.ReadUInt32LittleEndian(blob[EndedTrackedEffectIdOffset..])
            : null;
}
