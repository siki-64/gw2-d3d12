using System;
using System.Linq;
using System.Collections.Generic;
using Gw2.Protocol.MsgPack;

namespace Gw2.Client.Net.Game;

public sealed class OwnContentFunctionState
{
    public const int BindingAdd = 0x027e;
    public const int BindingRemove = 0x027f;
    public const int MovementModifierAdd = 0x018d;
    public const int MovementModifierRemove = 0x018e;
    public const int InstanceStart = 0x010c;
    public const int InputPress = 0x010b;
    public const int ConditionResult = 0x010d;
    public const int SkillActivation = 0x010e;
    public const uint DodgeInputId = 6;

    private const int BindingField = 1;
    private const int AgentIdField = 1;
    private const int MovementModifierField = 2;
    private const byte InputPressed = 1;
    private const byte NoInstanceStartFlags = 0;

    private readonly HashSet<uint> _activeBindings = [];
    private readonly Dictionary<uint, HashSet<uint>> _movementModifiersByAgent = [];
    private readonly Dictionary<(uint AgentId, uint MovementModifierUid), long> _movementModifierExpiries = [];

    public IReadOnlyCollection<uint> ActiveBindingUids => _activeBindings;

    public bool HasMovementModifier(uint agentId, uint movementModifierUid) =>
        _movementModifiersByAgent.TryGetValue(agentId, out var modifiers) && modifiers.Contains(movementModifierUid);

    public void ApplyMovementModifier(uint agentId, uint movementModifierUid, long? expiresAtMilliseconds)
    {
        ModifiersOf(agentId).Add(movementModifierUid);
        if (expiresAtMilliseconds is { } expiry) _movementModifierExpiries[(agentId, movementModifierUid)] = expiry;
        else _movementModifierExpiries.Remove((agentId, movementModifierUid));
    }

    public void ExpireMovementModifiers(long nowMilliseconds)
    {
        foreach (var (key, expiry) in _movementModifierExpiries.ToArray())
            if (nowMilliseconds >= expiry) RemoveMovementModifier(key.AgentId, key.MovementModifierUid);
    }

    public void RemoveMovementModifier(uint agentId, uint movementModifierUid)
    {
        _movementModifierExpiries.Remove((agentId, movementModifierUid));
        if (_movementModifiersByAgent.TryGetValue(agentId, out var modifiers)) modifiers.Remove(movementModifierUid);
    }

    public void Handle(DecodedMessage message)
    {
        switch (message.MessageId)
        {
            case BindingAdd:
                _activeBindings.Add(U32(message, BindingField));
                break;
            case BindingRemove:
                _activeBindings.Remove(U32(message, BindingField));
                break;
            case MovementModifierAdd:
                ModifiersOf(U32(message, AgentIdField)).Add(U32(message, MovementModifierField));
                break;
            case MovementModifierRemove:
                ModifiersOf(U32(message, AgentIdField)).Remove(U32(message, MovementModifierField));
                break;
        }
    }

    public static (int MessageId, object[] Values) InstanceStartMessage(uint instanceId, uint targetAgentId, uint source, uint functionUid, uint inputId) =>
        (InstanceStart, [instanceId, targetAgentId, source, functionUid, (ushort)inputId, NoInstanceStartFlags]);

    public static (int MessageId, object[] Values) InputPressMessage(uint functionUid) =>
        (InputPress, [functionUid, InputPressed]);

    public static (int MessageId, object[] Values) ConditionResultMessage(uint instanceId, uint rootFunctionUid, uint functionUid, int nodeIndex, bool result) =>
        (ConditionResult, [instanceId, rootFunctionUid, functionUid, (uint)nodeIndex, result ? (byte)1 : (byte)0]);

    public static (int MessageId, object[] Values) SkillActivationMessage(uint functionUid, int nodeIndex, uint? skillId)
    {
        var unsetTarget = new byte[16];
        for (var axis = 0; axis < 3; axis++) System.BitConverter.TryWriteBytes(unsetTarget.AsSpan(axis * 4, 4), float.PositiveInfinity);
        return (SkillActivation, [functionUid, (uint)nodeIndex, skillId ?? 0, unsetTarget]);
    }

    private HashSet<uint> ModifiersOf(uint agentId)
    {
        if (!_movementModifiersByAgent.TryGetValue(agentId, out var modifiers)) _movementModifiersByAgent[agentId] = modifiers = [];
        return modifiers;
    }

    private static uint U32(DecodedMessage message, int field) => (uint)(ulong)message.Fields[field].Value!;
}
