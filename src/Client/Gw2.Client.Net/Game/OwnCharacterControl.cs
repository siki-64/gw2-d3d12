using System;
using System.Collections.Generic;
using System.Numerics;

namespace Gw2.Client.Net.Game;

public sealed class OwnCharacterControl
{
    public const int ControlMessageId = 0x53;
    public const int ControlAppliedMessageId = 0x5;
    public const int MotionStateMessageId = 0x1a;

    private const int ControlAgentField = 1;
    private const int ControlSequenceField = 2;
    private const byte NoCommandFlags = 0;
    private const uint NoFlags = 0;
    private const byte NoMotionFlags = 0;

    private readonly OwnCharacterSteps _steps = new();
    private uint? _agentId;
    private uint _controlSequence;
    private bool _controlApplied;
    private bool _motionStateSent;

    public bool IsControlling => _agentId is not null;

    public uint? ControlledAgentId => _controlApplied && _motionStateSent ? _agentId : null;

    public void Handle(Gw2.Protocol.MsgPack.DecodedMessage message)
    {
        if (message.MessageId != ControlMessageId) return;
        _agentId = (uint)(ulong)message.Fields[ControlAgentField].Value!;
        _controlSequence = (uint)(ulong)message.Fields[ControlSequenceField].Value!;
        _controlApplied = false;
        _motionStateSent = false;
    }

    public IReadOnlyList<(int MessageId, object[] Values)> MessagesForStep(long step, Vector3 positionInCollisionUnits, bool isAirborne = false, OwnPhysicsState? physics = null)
    {
        var messages = new List<(int, object[])>();
        if (_agentId is not { } agentId) return messages;
        if (!_controlApplied)
        {
            _controlApplied = true;
            messages.Add((ControlAppliedMessageId, [agentId, (uint)step, _controlSequence]));
        }
        else if (!_motionStateSent)
        {
            _motionStateSent = true;
            messages.Add((MotionStateMessageId, MotionStateValues(agentId, step, positionInCollisionUnits)));
        }
        if (_steps.StateToSend(step, positionInCollisionUnits, isAirborne, physics) is { } state) messages.Add((GameMessageIds.OwnCharacterState, state));
        return messages;
    }

    private static object[] MotionStateValues(uint agentId, long step, Vector3 position)
    {
        var positionWithPadding = new byte[16];
        BitConverter.TryWriteBytes(positionWithPadding.AsSpan(0, 4), position.X);
        BitConverter.TryWriteBytes(positionWithPadding.AsSpan(4, 4), position.Y);
        BitConverter.TryWriteBytes(positionWithPadding.AsSpan(8, 4), position.Z);
        return [agentId, (uint)step, NoCommandFlags, positionWithPadding, NoFlags, new byte[16], new byte[16], NoMotionFlags, NoMotionFlags];
    }
}
