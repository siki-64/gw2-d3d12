using Gw2.Formats;

namespace Gw2.Dat.Content;

public sealed record OwnSkillState(
    Profession Profession,
    uint? MainHandItemDataId,
    uint? OffHandItemDataId,
    uint? ProfessionState,
    uint? InactiveLegend,
    IReadOnlyList<uint> Specializations,
    IReadOnlyCollection<uint> Traits,
    IReadOnlyCollection<uint> Buffs,
    Func<int, uint?> SlotPaletteOf,
    uint? ActiveCompanionDataId = null,
    Func<uint, uint>? SkillOverrideOf = null);

public sealed class Gw2OwnSkills
{
    public const int LegendSwapMechanic = 0;
    private const int SpecializationLineCount = 3;
    private const int EliteSpecializationLine = 2;
    private const uint FirstMechanicSlotKind = 9;
    private const uint BeastmodeProfessionState = 14;

    private readonly Gw2SkillPalettes _palettes;
    private readonly Gw2Items _items;
    private readonly LegendSkillPaletteLayout _legends;
    private readonly ProfessionMechanicPaletteLayout _mechanics;

    public Gw2OwnSkills(Gw2SkillPalettes palettes, Gw2Items items, LegendSkillPaletteLayout legends, ProfessionMechanicPaletteLayout mechanics)
    {
        _palettes = palettes;
        _items = items;
        _legends = legends;
        _mechanics = mechanics;
    }

    public Gw2SkillPalettes Palettes => _palettes;

    public SkillSelection SelectionOf(OwnSkillState state)
    {
        var mainHandWeapon = state.MainHandItemDataId is { } main ? _items.WeaponTypeOf(main) ?? WeaponType.Unarmed : WeaponType.Unarmed;
        var offHandWeapon = state.OffHandItemDataId is { } off ? _items.WeaponTypeOf(off) ?? WeaponType.Any : WeaponType.Any;
        var specializations = state.Specializations;
        return new SkillSelection(
            state.Profession,
            mainHandWeapon,
            offHandWeapon,
            ProfessionState: state.ProfessionState ?? SkillSelection.NoProfessionState,
            EliteSpecializationDataId: specializations.Count == SpecializationLineCount && specializations[EliteSpecializationLine] != 0 ? specializations[EliteSpecializationLine] : null,
            ActiveTraitDataIds: state.Traits,
            ActiveBuffDataIds: state.Buffs,
            SelectedSpecializationDataIds: specializations);
    }

    public const uint MechanicPaletteFillsWholeBarState = 7;

    public uint? WholeBarMechanicPaletteOf(OwnSkillState state) =>
        state.ProfessionState == MechanicPaletteFillsWholeBarState ? MechanicPaletteOf(state, state.ProfessionState) : null;

    private uint? MechanicPaletteOf(OwnSkillState state, uint? legend) =>
        state.Profession == Profession.Revenant
            ? legend is { } legendState ? _palettes.LegendMechanicPaletteDataId(_legends, legendState) : null
            : _palettes.ProfessionMechanicPaletteDataId(_legends, _mechanics, state.Profession);

    public static bool SwapsLegend(OwnSkillState state, int mechanic) =>
        mechanic == LegendSwapMechanic && state.Profession == Profession.Revenant;

    public uint? MechanicSkill(OwnSkillState state, int mechanic) =>
        UnresolvedMechanicSkill(state, mechanic) is { } skill ? state.SkillOverrideOf?.Invoke(skill) ?? skill : null;

    private uint? UnresolvedMechanicSkill(OwnSkillState state, int mechanic)
    {
        var selection = SelectionOf(state);
        uint? legend = SwapsLegend(state, mechanic) ? state.InactiveLegend : state.ProfessionState;
        uint? paletteDataId = MechanicPaletteOf(state, legend);
        if (state.Profession == Profession.Ranger && state.ProfessionState == BeastmodeProfessionState &&
            state.ActiveCompanionDataId is { } companion &&
            _palettes.CompanionBeastmodePaletteDataId(companion) is { } beastmodePalette &&
            _palettes.PaletteCastSkill(beastmodePalette, selection, FirstMechanicSlotKind + (uint)mechanic) is { } beastmodeSkill)
            return beastmodeSkill;
        uint? skill = paletteDataId is { } palette ? _palettes.PaletteCastSkill(palette, selection, FirstMechanicSlotKind + (uint)mechanic) : null;
        if (skill is null && _mechanics.ToolbeltProfessions.Contains((uint)state.Profession) &&
            state.SlotPaletteOf(mechanic) is { } toolbeltSourcePalette &&
            _palettes.PaletteSkill(toolbeltSourcePalette, selection) is { } toolbeltSourceSkill)
            skill = _palettes.ToolbeltCastSkill(toolbeltSourceSkill, selection);
        return skill;
    }
}
