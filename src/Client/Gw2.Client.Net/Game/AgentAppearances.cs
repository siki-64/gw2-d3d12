using System.Collections.Generic;
using Gw2.Protocol.MsgPack;

namespace Gw2.Client.Net.Game;

public sealed class AgentAppearances
{
    public const int OwnCoreStats = 0xb6;
    public const int OtherCoreStats = 0xb7;
    public const int AppearanceLength = 45;
    public const int RaceIndex = 0x1b;
    public const int SexIndex = 0x1d;

    private const int AgentIdField = 1;
    private const int AppearanceField = 2;

    private readonly Dictionary<uint, byte[]> _appearanceByAgent = [];

    public IReadOnlyDictionary<uint, byte[]> All => _appearanceByAgent;

    public byte[]? AppearanceOf(uint agentId) => _appearanceByAgent.GetValueOrDefault(agentId);

    public int? RaceOf(uint agentId) => AppearanceOf(agentId)?[RaceIndex];

    public int? SexOf(uint agentId) => AppearanceOf(agentId)?[SexIndex];

    public void Handle(DecodedMessage message)
    {
        if (message.MessageId is not (OwnCoreStats or OtherCoreStats)) return;
        var rows = (List<DecodedField[]>)message.Fields[AppearanceField].Value!;
        var appearance = new byte[AppearanceLength];
        for (var index = 0; index < AppearanceLength && index < rows.Count; index++)
            appearance[index] = (byte)System.Convert.ToUInt64(rows[index][0].Value);
        _appearanceByAgent[(uint)System.Convert.ToUInt64(message.Fields[AgentIdField].Value)] = appearance;
    }
}
