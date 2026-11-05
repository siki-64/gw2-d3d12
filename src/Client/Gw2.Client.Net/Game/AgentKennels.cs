using System.Collections.Generic;
using Gw2.Protocol.MsgPack;

namespace Gw2.Client.Net.Game;

public sealed class AgentKennels
{
    public const int ActivePetSet = 0x151;
    public const uint NoEquipSlot = 4;

    private const int OwnerField = 1;
    private const int EquipSlotField = 2;
    private const int PetAgentField = 3;

    private readonly Dictionary<uint, uint> _activePetByOwner = [];

    public uint? ActivePetOf(uint ownerAgentId) => _activePetByOwner.TryGetValue(ownerAgentId, out var pet) ? pet : null;

    public void Handle(DecodedMessage message)
    {
        if (message.MessageId != ActivePetSet || message.Fields.Count <= PetAgentField) return;
        var owner = (uint)MessageNumbers.Number(message, OwnerField);
        var equipSlot = (uint)MessageNumbers.Number(message, EquipSlotField);
        var pet = (uint)MessageNumbers.Number(message, PetAgentField);
        if (equipSlot == NoEquipSlot || pet == 0) _activePetByOwner.Remove(owner);
        else _activePetByOwner[owner] = pet;
    }
}
