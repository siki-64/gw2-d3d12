using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Gw2.Protocol.MsgPack;

namespace Gw2.Client.Net.Game;

public enum BreakBarState : byte
{
    Active = 0,
    Recovering = 1,
    Immune = 2,
}

public readonly record struct AgentBreakBar(float FractionFull, BreakBarState State, uint SkillId);

public sealed class AgentBreakBars
{
    public const byte BreakBarSetEvent = 0x2E;
    public const byte BreakBarRemoveEvent = 0x2F;
    public const byte BreakBarChangeEvent = 0x30;

    private const int AgentIdField = 1;
    private const int AgentDataFlagsField = 3;
    private const int AgentDataBlobField = 5;
    private const byte AgentEventDataFlag = 0x10;
    private const int BreakBarSetLength = 10;
    private const int BreakBarRemoveLength = 1;
    private const int BreakBarChangeLength = 22;
    private const int ValueOffset = 1;
    private const int SetStateOffset = 5;
    private const int SetSkillOffset = 6;
    private const int ChangeStateOffset = 17;
    private const int ChangeSkillOffset = 18;

    private readonly Dictionary<uint, AgentBreakBar> _barByAgent = [];

    public AgentBreakBar? BreakBarOf(uint agentId) => _barByAgent.TryGetValue(agentId, out var bar) ? bar : null;

    public void Handle(DecodedMessage message)
    {
        if (message.MessageId is not (AgentLifeState.AgentData or AgentLifeState.AgentCommandData) || message.Fields.Count <= AgentDataBlobField) return;
        if ((Convert.ToUInt64(message.Fields[AgentDataFlagsField].Value) & AgentEventDataFlag) == 0) return;
        if (message.Fields[AgentDataBlobField].Value is not byte[] blob || blob.Length == 0) return;
        var agentId = (uint)Convert.ToUInt64(message.Fields[AgentIdField].Value);
        ReadOnlySpan<byte> data = blob;
        switch (data[0], data.Length)
        {
            case (BreakBarSetEvent, BreakBarSetLength):
                _barByAgent[agentId] = new AgentBreakBar(Value(data), (BreakBarState)data[SetStateOffset], BinaryPrimitives.ReadUInt32LittleEndian(data[SetSkillOffset..]));
                return;
            case (BreakBarRemoveEvent, BreakBarRemoveLength):
                _barByAgent.Remove(agentId);
                return;
            case (BreakBarChangeEvent, BreakBarChangeLength) when _barByAgent.ContainsKey(agentId):
                _barByAgent[agentId] = new AgentBreakBar(Value(data), (BreakBarState)data[ChangeStateOffset], BinaryPrimitives.ReadUInt32LittleEndian(data[ChangeSkillOffset..]));
                return;
        }
    }

    private static float Value(ReadOnlySpan<byte> data) => BinaryPrimitives.ReadSingleLittleEndian(data[ValueOffset..]);
}
