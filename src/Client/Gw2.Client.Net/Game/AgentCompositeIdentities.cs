using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Gw2.Protocol.MsgPack;

namespace Gw2.Client.Net.Game;

public readonly record struct AgentCompositeIdentity(ulong RaceToken, ulong? VariantToken);

public sealed class AgentCompositeIdentities
{
    public const int CharacterComposite = 0x183;

    private const int AgentIdField = 1;
    private const int CompositeDataField = 4;
    private const int CompositeDataRaceIndex = 1;
    private const int VariantTokenField = 2;

    private readonly Dictionary<uint, ulong> _raceTokenByAgent = [];
    private readonly Dictionary<uint, ulong> _variantTokenByAgent = [];

    public AgentCompositeIdentity? IdentityOf(uint agentId) =>
        _raceTokenByAgent.TryGetValue(agentId, out var race) && race != 0
            ? new AgentCompositeIdentity(race, _variantTokenByAgent.TryGetValue(agentId, out var variant) && variant != 0 ? variant : null)
            : null;

    public void Handle(DecodedMessage message)
    {
        switch (message.MessageId)
        {
            case GameAgents.CharacterName when message.Fields.Count > CompositeDataField && message.Fields[CompositeDataField].Value is DecodedField[] { Length: > CompositeDataRaceIndex } compositeData
                && compositeData[CompositeDataRaceIndex].Value is byte[] { Length: sizeof(ulong) } raceToken:
                _raceTokenByAgent[AgentIdOf(message)] = BinaryPrimitives.ReadUInt64LittleEndian(raceToken);
                break;
            case CharacterComposite when message.Fields.Count > VariantTokenField && message.Fields[VariantTokenField].Value is byte[] { Length: sizeof(ulong) } variantToken:
                _variantTokenByAgent[AgentIdOf(message)] = BinaryPrimitives.ReadUInt64LittleEndian(variantToken);
                break;
        }
    }

    private static uint AgentIdOf(DecodedMessage message) => (uint)Convert.ToUInt64(message.Fields[AgentIdField].Value);
}
