using System;
using Gw2.Protocol.MsgPack;

namespace Gw2.Client.Net.Game;

public static class AgentPositionBlends
{
    public const byte PositionBlendEvent = 0x3B;
    public const int PositionBlendEventLength = 1;
    public static readonly TimeSpan BlendDuration = TimeSpan.FromSeconds(0.5);

    private const int AgentIdField = 1;
    private const int AgentDataFlagsField = 3;
    private const int AgentDataBlobField = 5;
    private const byte AgentEventDataFlag = 0x10;

    public static uint? BlendingAgentOf(DecodedMessage message)
    {
        if (message.MessageId is not (AgentLifeState.AgentData or AgentLifeState.AgentCommandData) || message.Fields.Count <= AgentDataBlobField) return null;
        if ((Convert.ToUInt64(message.Fields[AgentDataFlagsField].Value) & AgentEventDataFlag) == 0) return null;
        if (message.Fields[AgentDataBlobField].Value is not byte[] blob || !IsPositionBlend(blob)) return null;
        return (uint)Convert.ToUInt64(message.Fields[AgentIdField].Value);
    }

    public static bool IsPositionBlend(ReadOnlySpan<byte> blob) => blob.Length == PositionBlendEventLength && blob[0] == PositionBlendEvent;
}
