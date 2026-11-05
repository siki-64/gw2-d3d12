namespace Gw2.Formats;

public sealed record SkillPaletteContentLayout(
    uint PaletteContentType,
    uint SkillContentType,
    int PaletteTypeOffset,
    int WeaponTypeOffset,
    int GroupsOffset,
    int GroupCountOffset,
    int GroupSize,
    int GroupSlotKindOffset,
    int GroupEntriesOffset,
    int GroupEntryCountOffset,
    int EntrySize,
    int EntryRequiredSpecializationOffset,
    int SkillFlagsOffset,
    uint GroundTargetedSkillFlag,
    uint SpecializationContentType,
    uint TraitContentType,
    int SpecializationMinorTraitsOffset,
    int SpecializationMinorTraitCountOffset,
    int TraitSlotTypeOffset,
    int SkillPayloadTypeOffset,
    int SkillPayloadOffset,
    int AbilityMechanicPaletteOffset,
    int AbilityToolbeltSkillOffset,
    int SkillIconOffset,
    int AbilityPageCostOffset,
    int AbilityPageCostOverridesOffset,
    int AbilityPageCostOverrideCountOffset)
{
    public const int GroupProfessionOffset = 0x00;
    public const int SpecializationKindOffset = 0xC8;
    public const int EntrySkillOffset = 0x00;
    public const int EntryRequiredSkillOffset = 0x08;
    public const int EntryConditionTypeOffset = 0x14;
    public const int EntryGameModeMaskOffset = 0x18;
    public const int EntryMinimumLevelOffset = 0x1C;
    public const int EntryProfessionStateOffset = 0x24;
    public const int EntryAlternateProfessionStateOffset = 0x28;
    public const int EntryRequiredProgressOffset = 0x30;
    public const int EntryRequiredRaceOffset = 0x3C;
    public const int EntryRequiredTraitOffset = 0x40;
    public const int EntryMainHandWeaponOffset = 0x48;
    public const int EntryOffHandWeaponOffset = 0x4C;
    public const int EntryUnlockReferenceOffset = 0x58;
    public const int EntryUnlockModeOffset = 0x60;
    public const int EntryUnlockFlagsOffset = 0x64;
    private const int GroupStrideFromScaledIndex = 24;

    private static readonly CodeAnchor LegendSpecializationGroups = CodeAnchor.Site(
        "BA ?? 00 00 00 8B C8 E8 ?? ?? ?? ?? 33 F6 48 8B F8 39 74 24 ?? 74 ?? 48 85 C0 75 ?? 8B FE EB ?? " +
        "48 8D 48 ?? E8 ?? ?? ?? ?? 48 8B F8 8B DE 3B 5F ?? 0F 83 ?? ?? ?? ?? 8B C3 48 8D 0C 40 48 8B 47 ?? " +
        "83 7C C8 ?? 09 4C 8D 34 C8 75 ?? 8B EE 41 39 76 ??", 0);
    private static readonly CodeAnchor LegendSpecializationEntry = CodeAnchor.Site(
        "E8 ?? ?? ?? ?? 8B C5 48 6B C8 ?? 49 8B 46 ?? 48 8B 5C 01 ?? 48 85 DB 74 ?? 48 85 F6", 5);
    private static readonly CodeAnchor LoadoutSkipsSwapPalettes = CodeAnchor.Site(
        "48 85 ED 0F 84 ?? ?? ?? ?? 83 7D ?? 0B 0F 84 ?? ?? ?? ?? 49 8B 00 49 8B C8", 0);
    private static readonly CodeAnchor WeaponPaletteFilter = CodeAnchor.Site(
        "FF 90 38 02 00 00 48 8B D8 48 85 C0 0F 84 ?? ?? ?? ?? 83 78 ?? 18 7D ?? 44 8B 46 5C", 0);

    private static readonly CodeAnchor SkillConfigLookup = CodeAnchor.Site(
        "85 DB 0F 84 ?? ?? ?? ?? 4C 8D 84 24 ?? ?? ?? ?? BA ?? 00 00 00 B9 66 00 00 00 E8 ?? ?? ?? ?? 83 BC 24", 0);

    private static readonly CodeAnchor SkillbarActivateGroundTargetTest = CodeAnchor.Site(
        "E8 ?? ?? ?? ?? E9 ?? ?? ?? ?? F7 40 ?? ?? ?? ?? ?? 75 1A 48 8B 01 FF 90 20 02 00 00", 10);

    private static readonly CodeAnchor SpecializationSetLookup = CodeAnchor.Site(
        "44 8B C3 BA ?? ?? 00 00 48 8B 88 E0 00 00 00 48 8B 01 FF 50 58 48 8D 15", 0);

    private static readonly CodeAnchor SpecializationTraitLookup = CodeAnchor.Site(
        "44 8B C5 BA ?? 00 00 00 48 8B 88 E0 00 00 00 48 8B 01 FF 50 58 48 8D 4F", 4);

    public const uint BaselineTraitSlotType = 0;
    public const uint AbilitySkillPayloadType = 0;

    private static readonly CodeAnchor ToolbeltSkillResolve = CodeAnchor.Site(
        "8B D7 39 50 ?? 48 0F 44 50 ?? 48 85 D2 74 ?? 48 8B 8A ?? ?? 00 00 48 85 C9 74 ?? 4D 8B C6 8B D5 E8 ?? ?? ?? ?? " +
        "48 85 C0 74 ?? 48 8B 38 48 89 3B EB ?? 48 8B 82 ?? ?? 00 00", 0);

    private static readonly CodeAnchor SkillImageIconRead = CodeAnchor.Site(
        "48 85 C0 74 04 48 8B 78 ?? 48 8B 83 ?? ?? 00 00 48 85 C0 48 0F 45 F8 E8 ?? ?? ?? ?? 48 85 FF 4C 8D 05 ?? ?? ?? ?? 41 B9 29 00 00 00", 0);

    private static readonly CodeAnchor SkillbarPageCostRead = CodeAnchor.Site(
        "48 8D 93 ?? ?? 00 00 48 8B 83 ?? ?? 00 00 8B 8B ?? ?? 00 00 44 8B 0A 48 8D 14 C8", 0);

    private static readonly CodeAnchor TraitSlotTypeRead = CodeAnchor.Site("8B 5D ?? 83 FB 02 74 ?? 83 FB 03 74 ?? 41 B8", 2);

    private static readonly CodeAnchor BuildMinorTraitSlotWalk = CodeAnchor.Site(
        "33 DB 3B 5F ?? 73 ?? 48 8B 47 ?? 48 39 2C D8", 0);

    private static SkillPaletteContentLayout? _current;

    public static SkillPaletteContentLayout Current => _current ??= FromExecutable(ExecutableImage.Current);

    public static SkillPaletteContentLayout FromExecutable(string executablePath) => FromExecutable(ExecutableImage.Open(executablePath));

    internal static SkillPaletteContentLayout FromExecutable(ExecutableImage executable)
    {
        ulong groups = LegendSpecializationGroups.Locate(executable);
        ulong entry = LegendSpecializationEntry.Locate(executable);
        ulong loadout = LoadoutSkipsSwapPalettes.Locate(executable);
        ulong weapon = WeaponPaletteFilter.Locate(executable);
        ulong groundTarget = SkillbarActivateGroundTargetTest.Locate(executable);
        ulong minorTraits = BuildMinorTraitSlotWalk.Locate(executable);
        ulong toolbelt = ToolbeltSkillResolve.Locate(executable);
        ulong pageCost = SkillbarPageCostRead.Locate(executable);
        return new SkillPaletteContentLayout(
            PaletteContentType: executable.Dword(groups + 1),
            SkillContentType: executable.Dword(SkillConfigLookup.Locate(executable) + 17),
            PaletteTypeOffset: Byte(executable, loadout + 11),
            WeaponTypeOffset: Byte(executable, weapon + 20),
            GroupsOffset: Byte(executable, groups + 0x23),
            GroupCountOffset: Byte(executable, groups + 0x30),
            GroupSize: GroupStrideFromScaledIndex,
            GroupSlotKindOffset: Byte(executable, groups + 0x44),
            GroupEntriesOffset: Byte(executable, entry + 9),
            GroupEntryCountOffset: Byte(executable, groups + 0x51),
            EntrySize: Byte(executable, entry + 5),
            EntryRequiredSpecializationOffset: Byte(executable, entry + 14),
            SkillFlagsOffset: Byte(executable, groundTarget + 2),
            GroundTargetedSkillFlag: executable.Dword(groundTarget + 3),
            SpecializationContentType: executable.Dword(SpecializationSetLookup.Locate(executable) + 4),
            TraitContentType: executable.Dword(SpecializationTraitLookup.Locate(executable)),
            SpecializationMinorTraitsOffset: Byte(executable, minorTraits + 10),
            SpecializationMinorTraitCountOffset: Byte(executable, minorTraits + 4),
            TraitSlotTypeOffset: Byte(executable, TraitSlotTypeRead.Locate(executable)),
            SkillPayloadTypeOffset: Byte(executable, toolbelt + 4),
            SkillPayloadOffset: Byte(executable, toolbelt + 9),
            AbilityMechanicPaletteOffset: (int)executable.Dword(toolbelt + 18),
            AbilityToolbeltSkillOffset: (int)executable.Dword(toolbelt + 53),
            SkillIconOffset: Byte(executable, SkillImageIconRead.Locate(executable) + 8),
            AbilityPageCostOffset: (int)executable.Dword(pageCost + 3),
            AbilityPageCostOverridesOffset: (int)executable.Dword(pageCost + 10),
            AbilityPageCostOverrideCountOffset: (int)executable.Dword(pageCost + 16));
    }

    private static byte Byte(ExecutableImage executable, ulong address) => executable.Bytes(address, 1)[0];
}
