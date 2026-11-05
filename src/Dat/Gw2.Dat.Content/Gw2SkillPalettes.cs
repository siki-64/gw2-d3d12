using Gw2.Dat.Archive;
using Gw2.Formats;

namespace Gw2.Dat.Content;

public sealed class Gw2SkillPalettes
{
    public const int WeaponSkillCount = 5;
    private const uint WeaponPaletteType = 2;
    private const uint AnyProfession = 0;
    private const uint FirstWeaponSlotKind = 3;
    private const int FirstOffHandWeaponSlot = 3;
    private const uint PveRulesetCondition = 0;
    private const uint CompetitiveRulesetCondition = 1;
    private const uint GameModeMaskCondition = 2;
    private const uint AlwaysCondition = 3;
    private const uint AnyGameModeBit = 1;
    private const uint UnlockSpecializationAndReference = 1;
    private const uint UnlockSpecializationOrReference = 2;
    private const uint UnlockReference = 4;
    private const uint UnlockSpecialization = 8;
    private const uint CompanionContentType = 0x0A;
    private const int CompanionBeastmodePaletteOffset = 0x68;
    private const uint HasGroundTargetedCastFlag = 0x10000000;
    private const int FirstWeaponBarSlot = 5;
    private const int PageCostOverrideSize = 8;
    private const int PageCostOverrideValueOffset = 4;
    private const uint AnyKindPaletteType = 10;
    private const uint TransformationPaletteType = 11;
    private const int TransformationSlotCount = 10;
    private const uint FirstTransformationSlotKind = 18;
    private const uint FirstTransformationReplacedKind = 18;
    private const uint LastTransformationReplacedKind = 22;
    private static readonly uint?[] SlotKindByBarSlot = [2, 17, 17, 17, 0, 3, 4, 5, 6, 7, 16, 8, 9, 10, 11, 12, 13, 14, 15, 1, 29, 28, 29];

    private readonly Gw2ContentIndex _index;
    private readonly SkillPaletteContentLayout _layout;
    private readonly Dictionary<WeaponType, Gw2ContentLocation[]> _weaponPalettes = [];

    public Gw2SkillPalettes(Gw2ContentIndex index, SkillPaletteContentLayout layout)
    {
        _index = index;
        _layout = layout;
        foreach (var palettesOfWeapon in index.All(layout.PaletteContentType)
                     .Where(palette => index.ReadUInt32(palette, layout.PaletteTypeOffset) == WeaponPaletteType)
                     .OrderBy(palette => index.UidOf(layout.PaletteContentType, palette))
                     .GroupBy(palette => (WeaponType)index.ReadUInt32(palette, layout.WeaponTypeOffset)))
            _weaponPalettes[palettesOfWeapon.Key] = palettesOfWeapon.ToArray();
    }

    public uint?[] WeaponSkills(SkillSelection selection)
    {
        var skills = new uint?[WeaponSkillCount];
        for (int slot = 0; slot < WeaponSkillCount; slot++)
        {
            var weapon = slot >= FirstOffHandWeaponSlot && selection.OffHandWeapon != WeaponType.Any ? selection.OffHandWeapon : selection.MainHandWeapon;
            skills[slot] = _weaponPalettes.TryGetValue(weapon, out var palettes) ? FirstDefault(palettes, selection, FirstWeaponSlotKind + (uint)slot) : null;
        }
        return skills;
    }

    public uint? LegendMechanicPaletteDataId(LegendSkillPaletteLayout legends, uint legendProfessionState)
    {
        int legend = (int)(legendProfessionState - legends.FirstLegendProfessionState);
        if (legend < 0 || legend >= legends.PaletteConfigKeys.Count) return null;
        return ConfigPaletteDataId(legends, legends.PaletteConfigKeys[legend]);
    }

    public uint? ProfessionMechanicPaletteDataId(LegendSkillPaletteLayout configs, ProfessionMechanicPaletteLayout mechanics, Profession profession) =>
        mechanics.PaletteConfigKeysByProfession.TryGetValue((uint)profession, out var key) ? ConfigPaletteDataId(configs, key) : null;

    private uint? ConfigPaletteDataId(LegendSkillPaletteLayout configs, Guid configKey)
    {
        if (_index.FindByContentKey(configs.ConfigContentType, configKey) is not { } config) return null;
        int value = configs.ConfigValueOffset;
        if (_index.ReadUInt32(config, value + LegendSkillPaletteLayout.ConfigValueTypeOffset) != _layout.PaletteContentType) return null;
        return _index.ReadPointer(config, value + LegendSkillPaletteLayout.ConfigValueObjectOffset) is { } palette
            ? _index.DataIdOf(_layout.PaletteContentType, palette)
            : null;
    }

    public uint? DownedPaletteDataId(CommonSkillPaletteLayout common) =>
        _index.FindByContentKey(_layout.PaletteContentType, common.DownedPaletteKey) is { } palette
            ? _index.DataIdOf(_layout.PaletteContentType, palette)
            : null;

    public uint?[] DownedWeaponSkills(CommonSkillPaletteLayout common, SkillSelection selection, Gw2Bundle? bundle = null)
    {
        var skills = new uint?[WeaponSkillCount];
        if ((bundle?.DownedPaletteDataId ?? DownedPaletteDataId(common)) is not { } palette) return skills;
        for (int slot = 0; slot < WeaponSkillCount; slot++) skills[slot] = BarSlotSkill(palette, FirstWeaponBarSlot + slot, selection);
        return skills;
    }

    public uint?[] ItemWeaponSkills(uint mainHandPaletteDataId, uint? offHandPaletteDataId, SkillSelection selection)
    {
        var skills = new uint?[WeaponSkillCount];
        for (int slot = 0; slot < WeaponSkillCount; slot++)
            skills[slot] = BarSlotSkill(slot >= FirstOffHandWeaponSlot ? offHandPaletteDataId ?? mainHandPaletteDataId : mainHandPaletteDataId, FirstWeaponBarSlot + slot, selection);
        return skills;
    }

    public uint? BarSlotSkill(uint paletteDataId, int barSlot, SkillSelection selection)
    {
        if (_index.FindTyped(_layout.PaletteContentType, paletteDataId) is not { } palette) return null;
        uint paletteType = _index.ReadUInt32(palette, _layout.PaletteTypeOffset);
        if (paletteType == AnyKindPaletteType) return PaletteSkill(paletteDataId, selection);
        return SlotKindOf(paletteType, barSlot) is { } kind ? PaletteSkill(paletteDataId, selection, kind) : null;
    }

    public uint? SpecializationKindOf(uint specializationDataId) =>
        _index.Find(_layout.SpecializationContentType, specializationDataId) is { } specialization
            ? _index.ReadUInt32(specialization, SkillPaletteContentLayout.SpecializationKindOffset)
            : null;

    public static bool TransformationReplacesBarSlot(int barSlot) => SlotKindOf(TransformationPaletteType, barSlot) is >= FirstTransformationReplacedKind and <= LastTransformationReplacedKind;

    private static uint? SlotKindOf(uint paletteType, int barSlot) =>
        paletteType == TransformationPaletteType && barSlot < TransformationSlotCount
            ? FirstTransformationSlotKind + (uint)barSlot
            : barSlot >= 0 && barSlot < SlotKindByBarSlot.Length ? SlotKindByBarSlot[barSlot] : null;

    public uint? CompanionBeastmodePaletteDataId(uint companionDataId) =>
        _index.FindTyped(CompanionContentType, companionDataId) is { } companion &&
        _index.ReadPointer(companion, CompanionBeastmodePaletteOffset) is { } palette
            ? _index.DataIdOf(_layout.PaletteContentType, palette)
            : null;

    public uint? PaletteSkill(uint paletteDataId, SkillSelection selection, uint? slotKind = null) =>
        PaletteSkill(paletteDataId, selection, slotKind, castsGroundTargeted: false);

    public uint? PaletteCastSkill(uint paletteDataId, SkillSelection selection, uint? slotKind = null) =>
        PaletteSkill(paletteDataId, selection, slotKind, castsGroundTargeted: true);

    private uint? PaletteSkill(uint paletteDataId, SkillSelection selection, uint? slotKind, bool castsGroundTargeted)
    {
        if (_index.FindTyped(_layout.PaletteContentType, paletteDataId) is not { } palette) return null;
        uint groupCount = _index.ReadUInt32(palette, _layout.GroupCountOffset);
        if (groupCount == 0 || _index.ReadPointer(palette, _layout.GroupsOffset) is not { } groups) return null;
        for (int group = 0; group < groupCount; group++)
        {
            int groupOffset = checked(group * _layout.GroupSize);
            if (!IsGroupOf(_index.ReadUInt32(groups, groupOffset + SkillPaletteContentLayout.GroupProfessionOffset), selection)) continue;
            if (slotKind is { } kind && _index.ReadUInt32(groups, groupOffset + _layout.GroupSlotKindOffset) != kind) continue;
            uint entryCount = _index.ReadUInt32(groups, groupOffset + _layout.GroupEntryCountOffset);
            if (entryCount == 0 || _index.ReadPointer(groups, groupOffset + _layout.GroupEntriesOffset) is not { } entries) continue;
            foreach (int entry in BuffGatedEntriesFirst(entries, entryCount))
                if (DefaultSkillOf(entries, checked(entry * _layout.EntrySize), selection) is { } skill)
                    return castsGroundTargeted && HasGroundTargetedCast(skill)
                        ? GroundTargetedCastAfter(entries, entry, entryCount, selection) ?? skill
                        : skill;
        }
        return null;
    }

    private IEnumerable<int> BuffGatedEntriesFirst(Gw2ContentLocation entries, uint entryCount)
    {
        var all = Enumerable.Range(0, (int)entryCount);
        bool IsBuffGated(int entry) => _index.ReadPointer(entries, checked(entry * _layout.EntrySize) + SkillPaletteContentLayout.EntryRequiredSkillOffset) is not null;
        return all.Where(IsBuffGated).Concat(all.Where(entry => !IsBuffGated(entry)));
    }

    private bool HasGroundTargetedCast(uint skillId) =>
        _index.FindTyped(_layout.SkillContentType, skillId) is { } skill &&
        (_index.ReadUInt32(skill, _layout.SkillFlagsOffset) & HasGroundTargetedCastFlag) != 0;

    private uint? GroundTargetedCastAfter(Gw2ContentLocation entries, int barEntry, uint entryCount, SkillSelection selection)
    {
        int barEntryOffset = checked(barEntry * _layout.EntrySize);
        for (int entry = barEntry + 1; entry < entryCount; entry++)
        {
            int entryOffset = checked(entry * _layout.EntrySize);
            if (!HasSameConditions(entries, barEntryOffset, entryOffset)) continue;
            if (DefaultSkillOf(entries, entryOffset, selection) is { } skill && IsGroundTargeted(skill)) return skill;
        }
        return null;
    }

    private bool HasSameConditions(Gw2ContentLocation entries, int firstEntry, int secondEntry) =>
        HasSameBytes(entries, firstEntry, secondEntry, SkillPaletteContentLayout.EntryRequiredSkillOffset, sizeof(ulong)) &&
        HasSameBytes(entries, firstEntry, secondEntry, SkillPaletteContentLayout.EntryConditionTypeOffset, SkillPaletteContentLayout.EntryMinimumLevelOffset - SkillPaletteContentLayout.EntryConditionTypeOffset) &&
        HasSameBytes(entries, firstEntry, secondEntry, SkillPaletteContentLayout.EntryMinimumLevelOffset + sizeof(uint), _layout.EntrySize - SkillPaletteContentLayout.EntryMinimumLevelOffset - sizeof(uint));

    private bool HasSameBytes(Gw2ContentLocation entries, int firstEntry, int secondEntry, int offset, int length) =>
        _index.ReadBytes(entries, firstEntry + offset, length).SequenceEqual(_index.ReadBytes(entries, secondEntry + offset, length));

    private uint? FirstDefault(IEnumerable<Gw2ContentLocation> palettes, SkillSelection selection, uint slotKind)
    {
        foreach (var palette in palettes)
        {
            uint groupCount = _index.ReadUInt32(palette, _layout.GroupCountOffset);
            if (groupCount == 0 || _index.ReadPointer(palette, _layout.GroupsOffset) is not { } groups) continue;
            for (int group = 0; group < groupCount; group++)
            {
                int groupOffset = checked(group * _layout.GroupSize);
                if (_index.ReadUInt32(groups, groupOffset + SkillPaletteContentLayout.GroupProfessionOffset) != (uint)selection.Profession) continue;
                if (_index.ReadUInt32(groups, groupOffset + _layout.GroupSlotKindOffset) != slotKind) continue;
                uint entryCount = _index.ReadUInt32(groups, groupOffset + _layout.GroupEntryCountOffset);
                if (entryCount == 0 || _index.ReadPointer(groups, groupOffset + _layout.GroupEntriesOffset) is not { } entries) continue;
                for (int entry = 0; entry < entryCount; entry++)
                    if (DefaultSkillOf(entries, checked(entry * _layout.EntrySize), selection) is { } skill) return skill;
            }
        }
        return null;
    }

    private uint? DefaultSkillOf(Gw2ContentLocation entries, int entry, SkillSelection selection)
    {
        if (_index.ReadPointer(entries, entry + SkillPaletteContentLayout.EntrySkillOffset) is not { } skill) return null;
        if (_index.ReadPointer(entries, entry + SkillPaletteContentLayout.EntryRequiredSkillOffset) is { } requiredSkill &&
            !HasBuff(UidOfSkill(requiredSkill), selection)) return null;
        if (!MatchesCondition(entries, entry, selection)) return null;
        if (_index.ReadUInt32(entries, entry + SkillPaletteContentLayout.EntryMinimumLevelOffset) > selection.Level) return null;
        if (!MatchesProfessionState(entries, entry, selection)) return null;
        if (_index.ReadPointer(entries, entry + SkillPaletteContentLayout.EntryRequiredProgressOffset) is not null) return null;
        if (_index.ReadUInt32(entries, entry + SkillPaletteContentLayout.EntryRequiredRaceOffset) != 0) return null;
        if (_index.ReadUInt32(entries, entry + SkillPaletteContentLayout.EntryUnlockFlagsOffset) != 0) return null;
        if (!IsUnlocked(entries, entry, selection)) return null;
        if (_index.ReadPointer(entries, entry + SkillPaletteContentLayout.EntryRequiredTraitOffset) is { } trait && !IsTraitActive(trait, selection)) return null;
        if (!Matches((WeaponType)_index.ReadUInt32(entries, entry + SkillPaletteContentLayout.EntryMainHandWeaponOffset), selection.MainHandWeapon)) return null;
        if (!Matches((WeaponType)_index.ReadUInt32(entries, entry + SkillPaletteContentLayout.EntryOffHandWeaponOffset), selection.OffHandWeapon)) return null;
        return UidOfSkill(skill);
    }

    public uint? ToolbeltSkill(uint barSkillId, SkillSelection selection) => ToolbeltSkill(barSkillId, selection, castsGroundTargeted: false);

    public uint? ToolbeltCastSkill(uint barSkillId, SkillSelection selection) => ToolbeltSkill(barSkillId, selection, castsGroundTargeted: true);

    private uint? ToolbeltSkill(uint barSkillId, SkillSelection selection, bool castsGroundTargeted)
    {
        if (_index.FindTyped(_layout.SkillContentType, barSkillId) is not { } skill) return null;
        if (_index.ReadUInt32(skill, _layout.SkillPayloadTypeOffset) != SkillPaletteContentLayout.AbilitySkillPayloadType) return null;
        if (_index.ReadPointer(skill, _layout.SkillPayloadOffset) is not { } ability) return null;
        if (_index.ReadPointer(ability, _layout.AbilityMechanicPaletteOffset) is { } palette)
            return PaletteSkill(_index.DataIdOf(_layout.PaletteContentType, palette), selection, null, castsGroundTargeted);
        return _index.ReadPointer(ability, _layout.AbilityToolbeltSkillOffset) is { } toolbelt ? UidOfSkill(toolbelt) : null;
    }

    private static bool IsGroupOf(uint groupProfession, SkillSelection selection) =>
        groupProfession == AnyProfession || groupProfession == (uint)selection.Profession;

    public int? PageCostOf(uint skillId, uint? gameMode = null)
    {
        if (_index.FindTyped(_layout.SkillContentType, skillId) is not { } skill) return null;
        if (_index.ReadUInt32(skill, _layout.SkillPayloadTypeOffset) != SkillPaletteContentLayout.AbilitySkillPayloadType) return null;
        if (_index.ReadPointer(skill, _layout.SkillPayloadOffset) is not { } ability) return null;
        if (gameMode is { } mode && _index.ReadPointer(ability, _layout.AbilityPageCostOverridesOffset) is { } overrides)
        {
            uint overrideCount = _index.ReadUInt32(ability, _layout.AbilityPageCostOverrideCountOffset);
            for (int entry = 0; entry < overrideCount; entry++)
                if (_index.ReadUInt32(overrides, entry * PageCostOverrideSize) == mode) return (int)_index.ReadUInt32(overrides, entry * PageCostOverrideSize + PageCostOverrideValueOffset);
        }
        return (int)_index.ReadUInt32(ability, _layout.AbilityPageCostOffset);
    }

    public bool IsGroundTargeted(uint skillId) =>
        _index.FindTyped(_layout.SkillContentType, skillId) is { } skill &&
        (_index.ReadUInt32(skill, _layout.SkillFlagsOffset) & _layout.GroundTargetedSkillFlag) != 0;

    public uint? IconFileIdOf(uint skillId) =>
        _index.FindTyped(_layout.SkillContentType, skillId) is { } skill ? _index.ReadFileId(skill, _layout.SkillIconOffset) : null;

    private uint UidOfSkill(Gw2ContentLocation skill) => _index.DataIdOf(_layout.SkillContentType, skill);

    private bool MatchesCondition(Gw2ContentLocation entries, int entry, SkillSelection selection) =>
        _index.ReadUInt32(entries, entry + SkillPaletteContentLayout.EntryConditionTypeOffset) switch
        {
            AlwaysCondition => true,
            GameModeMaskCondition => (_index.ReadUInt32(entries, entry + SkillPaletteContentLayout.EntryGameModeMaskOffset) & ((1u << (int)selection.GameMode) | AnyGameModeBit)) != 0,
            PveRulesetCondition => !selection.UsesCompetitiveRuleset,
            CompetitiveRulesetCondition => selection.UsesCompetitiveRuleset,
            _ => false,
        };

    private bool MatchesProfessionState(Gw2ContentLocation entries, int entry, SkillSelection selection)
    {
        uint state = _index.ReadUInt32(entries, entry + SkillPaletteContentLayout.EntryProfessionStateOffset);
        uint alternate = _index.ReadUInt32(entries, entry + SkillPaletteContentLayout.EntryAlternateProfessionStateOffset);
        if (alternate == SkillSelection.NoProfessionState)
            return state == SkillSelection.NoProfessionState || state == selection.ProfessionState;
        return (selection.ProfessionState == state && selection.SecondaryProfessionState == alternate) ||
               (selection.ProfessionState == alternate && selection.SecondaryProfessionState == state);
    }

    private bool IsUnlocked(Gw2ContentLocation entries, int entry, SkillSelection selection)
    {
        bool hasReference = _index.ReadPointer(entries, entry + SkillPaletteContentLayout.EntryUnlockReferenceOffset) is not null;
        bool needsNoSpecialization = _index.ReadPointer(entries, entry + _layout.EntryRequiredSpecializationOffset) is not { } specialization ||
            HasSpecialization(_index.UidOf(_layout.SpecializationContentType, specialization), selection);
        return _index.ReadUInt32(entries, entry + SkillPaletteContentLayout.EntryUnlockModeOffset) switch
        {
            UnlockSpecializationAndReference => needsNoSpecialization && hasReference,
            UnlockSpecializationOrReference => needsNoSpecialization || hasReference,
            UnlockReference => hasReference,
            UnlockSpecialization => needsNoSpecialization,
            _ => true,
        };
    }

    private static bool HasSpecialization(uint specializationDataId, SkillSelection selection) =>
        specializationDataId == selection.EliteSpecializationDataId || selection.SelectedSpecializationDataIds?.Contains(specializationDataId) == true;

    private static bool HasBuff(uint buffSkillId, SkillSelection selection) =>
        buffSkillId == selection.ActiveBuffDataId || selection.ActiveBuffDataIds?.Contains(buffSkillId) == true;

    private bool IsTraitActive(Gw2ContentLocation trait, SkillSelection selection)
    {
        if (_index.ReadUInt32(trait, _layout.TraitSlotTypeOffset) == SkillPaletteContentLayout.BaselineTraitSlotType) return true;
        uint traitUid = _index.UidOf(_layout.TraitContentType, trait);
        if (selection.ActiveTraitDataIds?.Contains(traitUid) == true) return true;
        if (selection.EliteSpecializationDataId is not { } elite || _index.Find(_layout.SpecializationContentType, elite) is not { } specialization) return false;
        uint minorCount = _index.ReadUInt32(specialization, _layout.SpecializationMinorTraitCountOffset);
        if (minorCount == 0 || _index.ReadPointer(specialization, _layout.SpecializationMinorTraitsOffset) is not { } minors) return false;
        for (int minor = 0; minor < minorCount; minor++)
            if (_index.ReadPointerArrayElement(minors, minor) == trait) return true;
        return false;
    }

    private static bool Matches(WeaponType required, WeaponType equipped) =>
        required == WeaponType.Any ||
        (required == WeaponType.Unarmed ? equipped is WeaponType.Any or WeaponType.Unarmed : required == equipped);
}
