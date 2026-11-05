using Gw2.Formats;
using Gw2.Dat.Content;
using Gw2.Dat.Archive;

namespace Gw2.Client.Model;

[Serializable]
public sealed class Gw2SkillBarState
{
    public Profession profession = Profession.Guardian;
    public WeaponType mainHandWeapon = WeaponType.Greatsword;
    public WeaponType offHandWeapon = WeaponType.Any;
    public WeaponType alternateMainHandWeapon = WeaponType.Greatsword;
    public WeaponType alternateOffHandWeapon = WeaponType.Any;
    public uint professionState = SkillSelection.NoProfessionState;
    public uint eliteSpecializationDataId;
    public uint mechanicPaletteDataId;
    public uint alternateProfessionState = SkillSelection.NoProfessionState;
    public GameMode gameMode = GameMode.PvE;
    public uint[] specializationDataIds = new uint[SpecializationSlotCount];
    public uint[] majorTraitDataIds = new uint[MajorTraitSlotCount];

    public const int SpecializationSlotCount = 3;
    public const int MajorTraitSlotCount = 9;

    public bool SameAs(Gw2SkillBarState other) =>
        other != null && profession == other.profession && mainHandWeapon == other.mainHandWeapon && offHandWeapon == other.offHandWeapon &&
        alternateMainHandWeapon == other.alternateMainHandWeapon && alternateOffHandWeapon == other.alternateOffHandWeapon &&
        professionState == other.professionState && eliteSpecializationDataId == other.eliteSpecializationDataId &&
        mechanicPaletteDataId == other.mechanicPaletteDataId && alternateProfessionState == other.alternateProfessionState && gameMode == other.gameMode &&
        specializationDataIds.SequenceEqual(other.specializationDataIds) && majorTraitDataIds.SequenceEqual(other.majorTraitDataIds);

    public Gw2SkillBarState Copy()
    {
        var copy = (Gw2SkillBarState)MemberwiseClone();
        copy.specializationDataIds = (uint[])specializationDataIds.Clone();
        copy.majorTraitDataIds = (uint[])majorTraitDataIds.Clone();
        return copy;
    }
}
