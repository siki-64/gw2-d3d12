using System.Collections.Generic;
using System.Linq;
using Gw2.Protocol.MsgPack;

namespace Gw2.Client.Net.Game;

public sealed record EquippedWeapon(uint ItemDataId, uint? WardrobeSkinDataId);

public sealed record EquippedArmor(uint Slot, uint SkinDataId, IReadOnlyList<ushort> DyeDataIds);

public sealed class AgentEquipment
{
    public const int CoreStats = 0xb6;
    public const int WeaponItem = 0x3cb;
    public const int ArmorItem = 0x3c0;
    public const uint FirstArmorSlot = 2;
    public const uint LastArmorSlot = 7;
    public const int ItemPlace = 0x121;
    public const int ActiveWeaponSet = 0x12b;
    public const int HiddenEquipmentSlots = 0x12e;
    public const int EquipItem = 0x120;
    public const uint EquipmentContainer = 2;
    public const uint FirstLandWeaponSet = 4;
    public const uint FirstLandMainHandSlot = 29;
    public const uint WeaponSlotsPerSet = 2;
    public const uint LandWeaponSetCount = 2;

    private const int AgentIdField = 1;
    private const int CoreStatsProfessionField = 5;
    private const int ItemInstanceField = 1;
    private const int ItemDataIdField = 2;
    private const int ArmorSkinField = 4;
    private const int WeaponSkinField = 4;
    private const int ArmorDyesField = 5;
    private const int DyeIdField = 0;
    private const int PlaceInstanceField = 2;
    private const int PlaceContainerField = 3;
    private const int PlaceSlotField = 4;
    private const int ActiveWeaponSetField = 2;
    private const int EquipInstanceField = 2;
    private const int EquipSlotField = 3;
    private const int HiddenSlotWordsField = 2;
    private const int HiddenSlotWordField = 0;
    private const int SlotsPerHiddenSlotWord = 32;
    private static readonly uint[] MainHandSlotByWeaponSet = [0x18, 0x19, 0x1A, 0x1B, 0x1D, 0x1F];

    private readonly Dictionary<uint, uint> _itemDataIdByInstance = [];
    private readonly Dictionary<uint, uint> _weaponSkinByInstance = [];
    private readonly Dictionary<uint, (uint SkinDataId, ushort[] DyeDataIds)> _armorByInstance = [];
    private readonly Dictionary<(uint Agent, uint Slot), uint> _instanceByEquipmentSlot = [];
    private readonly Dictionary<uint, uint> _professionByAgent = [];
    private readonly Dictionary<uint, uint> _activeSetByAgent = [];
    private readonly Dictionary<uint, uint[]> _hiddenSlotWordsByAgent = [];

    public uint? ProfessionOf(uint agentId) => _professionByAgent.TryGetValue(agentId, out var profession) ? profession : null;

    public uint? ItemDataIdOfInstance(uint itemInstanceId) => _itemDataIdByInstance.TryGetValue(itemInstanceId, out var dataId) ? dataId : null;

    public uint? ActiveSetOf(uint agentId) => _activeSetByAgent.TryGetValue(agentId, out var set) ? set : null;

    public void Handle(DecodedMessage message)
    {
        switch (message.MessageId)
        {
            case WeaponItem:
                _itemDataIdByInstance[U32(message, ItemInstanceField)] = U32(message, ItemDataIdField);
                _weaponSkinByInstance[U32(message, ItemInstanceField)] = U32(message, WeaponSkinField);
                break;
            case ArmorItem:
                _armorByInstance[U32(message, ItemInstanceField)] = (U32(message, ArmorSkinField), DyesOf(message));
                break;
            case CoreStats:
                _professionByAgent[U32(message, AgentIdField)] = U32(message, CoreStatsProfessionField);
                break;
            case ItemPlace when U32(message, PlaceContainerField) == EquipmentContainer:
                _instanceByEquipmentSlot[(U32(message, AgentIdField), U32(message, PlaceSlotField))] = U32(message, PlaceInstanceField);
                break;
            case EquipItem:
                _instanceByEquipmentSlot[(U32(message, AgentIdField), U32(message, EquipSlotField))] = U32(message, EquipInstanceField);
                break;
            case HiddenEquipmentSlots:
                _hiddenSlotWordsByAgent[U32(message, AgentIdField)] = HiddenSlotWordsOf(message);
                break;
            case ActiveWeaponSet:
                _activeSetByAgent[U32(message, AgentIdField)] = U32(message, ActiveWeaponSetField);
                break;
        }
    }

    public (uint? MainHand, uint? OffHand) ActiveWeaponItemDataIdsOf(uint agentId)
    {
        if (ActiveSetOf(agentId) is not { } set || set >= MainHandSlotByWeaponSet.Length) return (null, null);
        uint mainHandSlot = MainHandSlotByWeaponSet[set];
        bool hasOffHandSlot = set >= FirstLandWeaponSet;
        return (WeaponIn(agentId, mainHandSlot), hasOffHandSlot ? WeaponIn(agentId, mainHandSlot + 1) : null);
    }

    public (EquippedWeapon? MainHand, EquippedWeapon? OffHand) ActiveWeaponsOf(uint agentId)
    {
        if (ActiveSetOf(agentId) is not { } set || set >= MainHandSlotByWeaponSet.Length) return (null, null);
        uint mainHandSlot = MainHandSlotByWeaponSet[set];
        return (EquippedWeaponIn(agentId, mainHandSlot), set >= FirstLandWeaponSet ? EquippedWeaponIn(agentId, mainHandSlot + 1) : null);
    }

    private EquippedWeapon? EquippedWeaponIn(uint agentId, uint slot)
    {
        if (!_instanceByEquipmentSlot.TryGetValue((agentId, slot), out var instance) || !_itemDataIdByInstance.TryGetValue(instance, out var dataId)) return null;
        return new EquippedWeapon(dataId, _weaponSkinByInstance.TryGetValue(instance, out var skin) && skin != 0 ? skin : null);
    }

    public bool IsSlotHidden(uint agentId, uint slot) =>
        _hiddenSlotWordsByAgent.TryGetValue(agentId, out var words)
        && slot / SlotsPerHiddenSlotWord < words.Length
        && (words[slot / SlotsPerHiddenSlotWord] & (1u << (int)(slot % SlotsPerHiddenSlotWord))) != 0;

    public IReadOnlyList<EquippedArmor> VisibleArmorOf(uint agentId) =>
        ArmorOf(agentId).Where(piece => !IsSlotHidden(agentId, piece.Slot)).ToList();

    public IReadOnlyList<EquippedArmor> ArmorOf(uint agentId)
    {
        var armor = new List<EquippedArmor>();
        for (var slot = FirstArmorSlot; slot <= LastArmorSlot; slot++)
            if (_instanceByEquipmentSlot.TryGetValue((agentId, slot), out var instance) && _armorByInstance.TryGetValue(instance, out var piece) && piece.SkinDataId != 0)
                armor.Add(new EquippedArmor(slot, piece.SkinDataId, piece.DyeDataIds));
        return armor;
    }

    private static uint[] HiddenSlotWordsOf(DecodedMessage message) =>
        ((List<DecodedField[]>)message.Fields[HiddenSlotWordsField].Value!).Select(row => (uint)System.Convert.ToUInt64(row[HiddenSlotWordField].Value)).ToArray();

    private static ushort[] DyesOf(DecodedMessage message)
    {
        var rows = (List<DecodedField[]>)message.Fields[ArmorDyesField].Value!;
        var dyes = new ushort[rows.Count];
        for (var index = 0; index < rows.Count; index++)
            dyes[index] = (ushort)(ulong)rows[index][DyeIdField].Value!;
        return dyes;
    }

    private uint? WeaponIn(uint agentId, uint slot) =>
        _instanceByEquipmentSlot.TryGetValue((agentId, slot), out var instance) && _itemDataIdByInstance.TryGetValue(instance, out var dataId) ? dataId : null;

    private static uint U32(DecodedMessage message, int field) => (uint)(ulong)message.Fields[field].Value!;
}
