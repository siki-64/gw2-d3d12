using System;
using System.Collections.Generic;
using Gw2.Protocol.MsgPack;

namespace Gw2.Client.Net.Game;

public sealed class AgentTransformations
{
    public const int Set = 0x2AE;
    public const int SetWithDuration = 0x2AF;
    public const uint TypeCount = 6;
    public const uint BundleType = 5;
    private const uint ViewerListedType = 3;
    private const uint DebugSpeciesType = 4;
    private const int AgentIdField = 1;
    private const int TransformationField = 2;
    private const int TypeField = 3;

    private readonly Dictionary<uint, uint[]> _transformationsByAgent = [];

    public void Handle(DecodedMessage message)
    {
        if (message.MessageId is not (Set or SetWithDuration)) return;
        uint type = U32(message, TypeField);
        if (type >= TypeCount) return;
        uint agent = U32(message, AgentIdField);
        if (!_transformationsByAgent.TryGetValue(agent, out var transformations)) _transformationsByAgent[agent] = transformations = new uint[TypeCount];
        transformations[type] = U32(message, TransformationField);
    }

    public uint? TransformationOf(uint agentId, uint type) =>
        _transformationsByAgent.TryGetValue(agentId, out var transformations) && type < TypeCount && transformations[type] != 0 ? transformations[type] : null;

    public uint? ActiveTransformationOf(uint agentId)
    {
        if (TransformationOf(agentId, BundleType) is { } bundle) return bundle;
        for (uint type = 0; type < TypeCount; type++)
            if (type is not (ViewerListedType or DebugSpeciesType) && TransformationOf(agentId, type) is { } transformation) return transformation;
        return null;
    }

    private static uint U32(DecodedMessage message, int field) => Convert.ToUInt32(message.Fields[field].Value);
}
