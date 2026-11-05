using System;
using System.Collections.Generic;
using Gw2.Protocol.MsgPack;

namespace Gw2.Client.Net.Game;

public sealed class AgentMountWardrobe(Func<uint, uint?>? mountDefinitionOfSkin = null)
{
    public const int MountDyes = 0x185;
    public const int MountSkin = 0x186;
    public const int DyeChannelCount = 4;

    private const int AgentIdField = 1;
    private const int FirstDyeField = 2;
    private const int MountDefinitionField = 6;
    private const int SkinField = 2;

    private readonly Dictionary<(uint AgentId, uint MountDefinitionId), ushort[]> _dyesByMount = [];
    private readonly Dictionary<uint, Dictionary<uint, uint>> _skinByMountByAgent = [];

    public IReadOnlyList<ushort>? DyesOf(uint agentId, uint mountDefinitionId) =>
        _dyesByMount.TryGetValue((agentId, mountDefinitionId), out var dyes) ? dyes : null;

    public IReadOnlyCollection<uint> SkinsOf(uint agentId) =>
        _skinByMountByAgent.TryGetValue(agentId, out var skins) ? skins.Values : [];

    public uint? SkinOf(uint agentId, uint mountDefinitionId) =>
        _skinByMountByAgent.TryGetValue(agentId, out var skins) && skins.TryGetValue(mountDefinitionId, out var skin) ? skin : null;

    public void Handle(DecodedMessage message)
    {
        switch (message.MessageId)
        {
            case MountDyes:
                var dyes = new ushort[DyeChannelCount];
                for (int channel = 0; channel < DyeChannelCount; channel++)
                    dyes[channel] = (ushort)U32(message, FirstDyeField + channel);
                _dyesByMount[(U32(message, AgentIdField), U32(message, MountDefinitionField))] = dyes;
                break;
            case MountSkin:
                uint agentId = U32(message, AgentIdField);
                if (!_skinByMountByAgent.TryGetValue(agentId, out var skins))
                    _skinByMountByAgent[agentId] = skins = [];
                uint skin = U32(message, SkinField);
                skins[mountDefinitionOfSkin?.Invoke(skin) ?? skin] = skin;
                break;
        }
    }

    private static uint U32(DecodedMessage message, int field) => (uint)(ulong)message.Fields[field].Value!;
}
