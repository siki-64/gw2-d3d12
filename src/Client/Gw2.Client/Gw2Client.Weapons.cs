using System.Collections.Generic;
using Gw2.Client.Net.Game;
using Gw2.Client.Scene;
using Gw2.Dat.Content;
using Gw2.Formats;

namespace Gw2.Client;

public sealed partial class Gw2Client
{
    private const string OffHandHeldBone = "lefthand";
    private const string ShieldHeldBone = "shield";
    private const uint ShieldWeaponKind = (uint)WeaponType.Shield;

    private readonly List<(CharacterAttachment Placement, Gw2AttachedModel Drawn)> _weaponAttachments = [];
    private CharacterScene? _weaponCharacter;
    private OwnWeaponLook? _weaponLook;
    private Gw2Items? _weaponItems;
    private Gw2Skins? _weaponSkins;

    private readonly record struct OwnWeaponLook(EquippedWeapon? MainHand, EquippedWeapon? OffHand, bool WeaponsDrawn);

    private void SyncOwnWeapons(Gw2PropLoader propLoader)
    {
        if (_character is null || _reader is null || _login?.ControlledAgentId is not { } ownAgentId) return;
        var (mainHand, offHand) = _world.Equipment.ActiveWeaponsOf(ownAgentId);
        var look = new OwnWeaponLook(mainHand, offHand, _world.WeaponStances.WeaponsDrawn(ownAgentId));
        if (ReferenceEquals(_weaponCharacter, _character) && _weaponLook == look) return;
        DetachOwnWeapons(propLoader);
        _weaponCharacter = _character;
        _weaponLook = look;
        AttachOwnWeapon(propLoader, mainHand, isOffHand: false, look.WeaponsDrawn);
        AttachOwnWeapon(propLoader, offHand, isOffHand: true, look.WeaponsDrawn);
    }

    private void AttachOwnWeapon(Gw2PropLoader propLoader, EquippedWeapon? weapon, bool isOffHand, bool weaponsDrawn)
    {
        if (weapon is null || SkinOf(weapon) is not { DefaultModelFileId: { } modelFileId } skin) return;
        var socket = weaponsDrawn ? HeldBoneOf(skin, isOffHand) : EquipmentSockets.StowedBone(skin, 0);
        if (socket is null) return;
        _weaponAttachments.Add((_character!.Attach(modelFileId, socket, EquipmentSockets.GripBone(socket)), propLoader.AttachModel(modelFileId)));
    }

    private static string HeldBoneOf(Gw2Skin skin, bool isOffHand) =>
        !isOffHand ? EquipmentSockets.HeldBone(skin, 0)
        : skin.WeaponKind == ShieldWeaponKind ? ShieldHeldBone
        : OffHandHeldBone;

    private Gw2Skin? SkinOf(EquippedWeapon weapon)
    {
        var skinLayout = SkinContentLayout.Current;
        _weaponItems ??= new Gw2Items(_reader!.ContentIndex, ItemContentLayout.Current);
        _weaponSkins ??= new Gw2Skins(_reader!.ContentIndex, skinLayout);
        var skinDataId = weapon.WardrobeSkinDataId ?? _weaponItems.DefaultSkinDataIdOf(skinLayout.SkinContentType, weapon.ItemDataId);
        return skinDataId is { } id ? _weaponSkins.Find(id) : null;
    }

    private void DetachOwnWeapons(Gw2PropLoader? propLoader)
    {
        foreach (var (placement, drawn) in _weaponAttachments)
        {
            _weaponCharacter?.Detach(placement);
            propLoader?.DetachModel(drawn);
        }
        _weaponAttachments.Clear();
        _weaponCharacter = null;
        _weaponLook = null;
    }
}
