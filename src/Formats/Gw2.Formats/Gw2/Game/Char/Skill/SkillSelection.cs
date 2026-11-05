using System.Collections.Generic;

namespace Gw2.Formats;

public enum Profession : uint
{
    Guardian = 1,
    Warrior = 2,
    Engineer = 3,
    Ranger = 4,
    Thief = 5,
    Elementalist = 6,
    Mesmer = 7,
    Necromancer = 8,
    Revenant = 9,
}

public enum WeaponType : uint
{
    Sword = 0,
    Hammer = 1,
    Longbow = 2,
    Shortbow = 3,
    Axe = 4,
    Dagger = 5,
    Greatsword = 6,
    Mace = 7,
    Pistol = 8,
    Rifle = 10,
    Scepter = 11,
    Staff = 12,
    Focus = 13,
    Torch = 14,
    Warhorn = 15,
    Shield = 16,
    Harpoon = 19,
    Speargun = 20,
    Trident = 21,
    Any = 24,
    Unarmed = 25,
}

public enum GameMode : uint
{
    PvE = 1,
    PvP = 2,
    WvW = 3,
}

public sealed record SkillSelection(
    Profession Profession,
    WeaponType MainHandWeapon,
    WeaponType OffHandWeapon,
    uint ProfessionState = SkillSelection.NoProfessionState,
    uint SecondaryProfessionState = SkillSelection.NoProfessionState,
    uint Level = SkillSelection.MaximumLevel,
    GameMode GameMode = GameMode.PvE,
    uint? MechanicPaletteDataId = null,
    uint? EliteSpecializationDataId = null,
    uint? ActiveBuffDataId = null,
    uint? WeaponPaletteDataId = null,
    IReadOnlyCollection<uint>? ActiveTraitDataIds = null,
    IReadOnlyCollection<uint>? ActiveBuffDataIds = null,
    IReadOnlyCollection<uint>? SelectedSpecializationDataIds = null)
{
    public bool UsesCompetitiveRuleset => GameMode == GameMode.PvP;

    public const uint NoProfessionState = 0x17;
    public const uint MaximumLevel = 80;
}
