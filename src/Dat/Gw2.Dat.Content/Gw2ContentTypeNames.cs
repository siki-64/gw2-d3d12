namespace Gw2.Dat.Content;

public static class Gw2ContentTypeNames
{
    public const string PlaceholderPrefix = "UnknownContentType_0x";

    private static readonly Dictionary<uint, string> Names = new()
    {
        [0x01] = "AchievementCategoryDef",
        [0x07] = "CinematicDef",
        [0x08] = "CollectionDef",
        [0x09] = "ColorDef",
        [0x0A] = "CompanionDef",
        [0x0C] = "CraftingRecipeDef",
        [0x0E] = "CurrencyDef",
        [0x10] = "DecoSettingsDef",
        [0x12] = "ImageListDef",
        [0x13] = "EmoteDef",
        [0x17] = "GliderSkin",
        [0x18] = "MissionTarget",
        [0x19] = "PermissionDef",
        [0x1B] = "GuildUpgradeDef",
        [0x22] = "InstanceEntryDef",
        [0x23] = "ItemDef",
        [0x26] = "JadeBotSkin",
        [0x29] = "LimitedCinematicDef",
        [0x2A] = "MagicDoorSkin",
        [0x2B] = "MailDef",
        [0x2C] = "MailCarrierDef",
        [0x2D] = "MapDef",
        [0x30] = "Minipet",
        [0x33] = "Outfit",
        [0x34] = "PointOfInterestDef",
        [0x35] = "ProgressDef",
        [0x39] = "SuccessorQuestDef",
        [0x3F] = "SectorDef",
        [0x40] = "SkillDef",
        [0x41] = "SkillPaletteDef",
        [0x42] = "Skin",
        [0x43] = "SpeciesDef",
        [0x48] = "SubRegionCommonDef",
        [0x4B] = "TrainingPackage",
        [0x4C] = "TrainingPoint",
        [0x4D] = "Trait",
        [0x4E] = "WorldDef",
        [0x4F] = "WvwDef",
        [0x52] = "WvwObjectiveDef",
        [0x54] = "UpgradeLineDef",
        [0x55] = "WvwRankDef",
        [0x64] = "AnimationListenerDef",
        [0x65] = "AnimBlendTreeDef",
        [0x69] = "ComboDef",
        [0x89] = "CharCreateConfig",
        [0x90] = "IconDef",
        [0x93] = "ColorPaletteDef",
        [0x94] = "ModelAudioDef",
        [0x96] = "Configuration",
        [0x98] = "ContentAccess",
        [0x99] = "ContentAccessGroup",
        [0xA0] = "ExclusionTag",
        [0xA6] = "CreditsDef",
        [0xA8] = "DayNightCycle",
        [0xB4] = "DynamicCameraDef",
        [0xB5] = "DynamicCameraTransitionDef",
        [0xB7] = "Effect",
        [0xC8] = "GsItemDef",
        [0xCA] = "GsPackageDef",
        [0xCC] = "Audio",
        [0xD7] = "MissionOpDef",
        [0xDF] = "HomesteadSettings",
        [0xE3] = "InfiniteDungeonIslandDef",
        [0xED] = "ItemConversionArrayDef",
        [0xEE] = "ItemCrestDef",
        [0xEF] = "ItemDefaultDef",
        [0xF4] = "LeaderboardDef",
        [0xFA] = "LevelBoost",
        [0xFF] = "LfgDef",
        [0x104] = "MadLib",
        [0x10C] = "ContentDirectionDef",
        [0x125] = "MarkerDef",
        [0x12A] = "MountDefinition",
        [0x12E] = "MountSkin",
        [0x12F] = "MovementModifierDef",
        [0x130] = "MovementPackageDef",
        [0x131] = "MovementSettingsDef",
        [0x13B] = "ProfessionDef",
        [0x141] = "PvpDef",
        [0x14D] = "SeasonEventDef",
        [0x158] = "RaceDef",
        [0x15D] = "RandomUnlockTableDef",
        [0x15E] = "Reward",
        [0x161] = "RewardTrack",
        [0x170] = "SeasonDef",
        [0x182] = "Specialization",
        [0x192] = "TerrainEffectTableDef",
        [0x193] = "TerrainDecalTableDef",
        [0x19A] = "ToyDef",
        [0x1A1] = "UnlockDef",
    };

    public static IReadOnlyDictionary<uint, string> All => Names;

    public static bool TryGet(uint type, out string name) => Names.TryGetValue(type, out name!);

    public static bool IsUnnamed(uint type) => !Names.ContainsKey(type);

    public static string Label(uint type) =>
        Names.TryGetValue(type, out var name) ? name : PlaceholderPrefix + type.ToString("X");

    public static bool IsPlaceholder(string label) =>
        label is not null && label.StartsWith(PlaceholderPrefix, StringComparison.Ordinal);
}
