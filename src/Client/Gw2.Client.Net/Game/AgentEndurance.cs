using System.Collections.Generic;
using Gw2.Protocol.MsgPack;

namespace Gw2.Client.Net.Game;

public sealed class AgentEndurance
{
    public const int EnduranceCreate = 0xd9;
    public const int EnduranceSet = 0xdb;

    private const int AgentIdField = 1;
    private const int CreatePointsField = 2;
    private const int CreateDodgeCostField = 5;
    private const int CreateEffectiveMaximumField = 4;
    private const int SetPointsField = 2;

    private readonly Dictionary<uint, uint> _pointsByAgent = [];
    private readonly Dictionary<uint, uint> _dodgeCostByAgent = [];
    private readonly Dictionary<uint, uint> _maximumByAgent = [];

    public uint? MaximumOf(uint agentId) => _maximumByAgent.TryGetValue(agentId, out var maximum) ? maximum : null;

    public uint? PointsOf(uint agentId) => _pointsByAgent.TryGetValue(agentId, out var points) ? points : null;

    public bool CanDodge(uint agentId) =>
        !_pointsByAgent.TryGetValue(agentId, out var points) ||
        !_dodgeCostByAgent.TryGetValue(agentId, out var cost) ||
        points >= cost;

    public void Handle(DecodedMessage message)
    {
        switch (message.MessageId)
        {
            case EnduranceCreate:
                _pointsByAgent[U32(message, AgentIdField)] = U32(message, CreatePointsField);
                _dodgeCostByAgent[U32(message, AgentIdField)] = U32(message, CreateDodgeCostField);
                _maximumByAgent[U32(message, AgentIdField)] = U32(message, CreateEffectiveMaximumField);
                break;
            case EnduranceSet:
                _pointsByAgent[U32(message, AgentIdField)] = U32(message, SetPointsField);
                break;
        }
    }

    private static uint U32(DecodedMessage message, int field) => (uint)(ulong)message.Fields[field].Value!;
}
