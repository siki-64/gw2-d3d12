using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Gw2.Protocol.MsgPack;

namespace Gw2.Client.Net.Game;

public sealed class AgentActivationSpeeds
{
    public const byte ActivationSpeedEvent = 0x4A;
    public const int ActivationSpeedEventLength = 5;
    public const float NormalSpeed = 1.0f;

    private const int AgentIdField = 1;
    private const int AgentDataFlagsField = 3;
    private const int AgentDataBlobField = 5;
    private const byte AgentEventDataFlag = 0x10;
    private const int SpeedOffset = 1;

    private readonly Dictionary<uint, float> _speedByAgent = [];

    public float SpeedOf(uint agentId) => _speedByAgent.TryGetValue(agentId, out var speed) ? speed : NormalSpeed;

    public void Handle(DecodedMessage message)
    {
        if (message.MessageId is not (AgentLifeState.AgentData or AgentLifeState.AgentCommandData) || message.Fields.Count <= AgentDataBlobField) return;
        if ((Convert.ToUInt64(message.Fields[AgentDataFlagsField].Value) & AgentEventDataFlag) == 0) return;
        if (message.Fields[AgentDataBlobField].Value is not byte[] blob) return;
        if (Decode(blob) is not { } speed) return;
        _speedByAgent[(uint)Convert.ToUInt64(message.Fields[AgentIdField].Value)] = speed;
    }

    public static float? Decode(ReadOnlySpan<byte> blob) =>
        blob.Length == ActivationSpeedEventLength && blob[0] == ActivationSpeedEvent
            ? BinaryPrimitives.ReadSingleLittleEndian(blob[SpeedOffset..])
            : null;
}
