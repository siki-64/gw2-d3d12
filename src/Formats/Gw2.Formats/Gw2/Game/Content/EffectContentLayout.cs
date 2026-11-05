namespace Gw2.Formats;

public sealed record EffectModelLayout(
    int ModelFileOffset,
    int ScaleOffset,
    int OffsetInGameUnitsOffset,
    int AnimationTokenOffset,
    int AnimationTimeScaleOffset,
    int DelayMillisecondsOffset,
    int FlagsOffset,
    int PlacementBoneTokenOffset);

public sealed record EffectAttachedModelLayout(int ModelFileOffset, int AnimationTokenOffset);

public sealed record EffectDecalLayout(
    int TextureFilesOffset,
    int TextureFileCountOffset,
    int ProjectionSizeOffset,
    int ProjectionDepthOffset,
    int MaterialFileOffset,
    int DelayMillisecondsOffset,
    int DurationMillisecondsOffset);

public sealed record EffectContentLayout(
    uint EffectContentType,
    int KindOffset,
    int PayloadOffset,
    EffectModelLayout Model,
    EffectAttachedModelLayout BoneModel,
    EffectAttachedModelLayout WeaponTrail,
    EffectDecalLayout Decal)
{
    public const uint DecalKind = 2;
    public const uint BoneModelKind = 4;
    public const uint ModelKind = 6;
    public const uint WeaponTrailKind = 10;

    private const int ArrayCountAfterPointer = 8;

    private static readonly CodeAnchor EffectLookup = CodeAnchor.Site(
        "E8 ?? ?? ?? ?? BA ?? ?? ?? ?? 48 8B 88 E0 00 00 00 4C 8B 01 4D 8B 48 58 44 8B 03 41 FF D1 48 8B D8 48 85 C0 75", 0);
    private static readonly CodeAnchor ModelFile = CodeAnchor.Site(
        "8B 48 ?? 83 F9 06 75 ?? 48 8B 40 ?? 48 8B 48 ?? EB ?? 83 F9 04 75 ?? 48 8B 40 ?? 48 8B 48 ?? EB ?? " +
        "83 F9 0A 75 ?? 48 8B 48 ?? EB ?? 48 8B CD 48 8B 49 ??", 0);
    private static readonly CodeAnchor AnimationToken = CodeAnchor.Site(
        "8B 48 ?? 83 F9 04 74 ?? 83 F9 06 74 ?? 83 F9 0A 74 ?? 8B D7 EB ?? 48 8B 40 ?? 48 8B 50 ?? EB ?? " +
        "48 8B 40 ?? 48 8B 50 ?? EB ?? 48 8B 40 ?? 48 8B 50 ??", 0);
    private static readonly CodeAnchor ModelDelay = CodeAnchor.Site("48 8B 40 ?? 48 85 C0 74 ?? 8B 70 ?? 3B B3", 0);
    private static readonly CodeAnchor ModelTimeScale = CodeAnchor.Site("48 8B 40 ?? F3 0F 59 70 ?? F3 0F 59 78 ??", 0);
    private static readonly CodeAnchor ModelScale = CodeAnchor.Site(
        "48 8B 5B ?? F3 0F 10 4B ?? F3 0F 10 43 ?? E8 ?? ?? ?? ?? F7 87 ?? ?? ?? ?? ?? ?? ?? ?? F3 0F 59 43 ??", 0);
    private static readonly CodeAnchor ModelPlacementBone = CodeAnchor.Site("48 8B 45 ?? 48 8D 5D ?? 48 8B 40 ??", 0);
    private static readonly CodeAnchor ModelFlags = CodeAnchor.Site("48 8B 41 ?? F6 40 ?? 02", 0);
    private static readonly CodeAnchor ModelOffset = CodeAnchor.Site(
        "F3 0F 10 93 ?? ?? ?? ?? 0F 57 15 ?? ?? ?? ?? 41 0F 2E D0 F3 0F 10 8B ?? ?? ?? ?? 0F 57 0D", 0);
    private static readonly CodeAnchor DecalDelay = CodeAnchor.Site("83 78 ?? 02 75 ?? 48 8B 40 ?? EB 02 33 C0 8B 40 ??", 0);
    private static readonly CodeAnchor DecalTextureCount = CodeAnchor.Site("48 89 73 ?? 89 73 ?? 89 73 ?? 45 8B 6E ??", 0);
    private static readonly CodeAnchor DecalMaterial = CodeAnchor.Site("49 8B 56 ?? E8 ?? ?? ?? ?? 41 B9 01 00 00 00", 0);
    private static readonly CodeAnchor DecalDuration = CodeAnchor.Site("F3 49 0F 2A 4E ?? F3 0F 5E C8", 0);
    private static readonly CodeAnchor DecalProjection = CodeAnchor.Site("F2 41 0F 10 4E ?? F2 0F 11 4D ?? 41 8B 46 ??", 0);

    private static EffectContentLayout? _current;

    public static EffectContentLayout Current => _current ??= FromExecutable(ExecutableImage.Current);

    public static EffectContentLayout FromExecutable(string executablePath) => FromExecutable(ExecutableImage.Open(executablePath));

    internal static EffectContentLayout FromExecutable(ExecutableImage executable)
    {
        ulong lookup = EffectLookup.Locate(executable);
        ulong file = ModelFile.Locate(executable);
        ulong animation = AnimationToken.Locate(executable);
        ulong projection = DecalProjection.Locate(executable);
        int textureCount = Byte(executable, DecalTextureCount.Locate(executable) + 13);
        return new EffectContentLayout(
            EffectContentType: executable.Dword(lookup + 6),
            KindOffset: Byte(executable, file + 2),
            PayloadOffset: Byte(executable, file + 11),
            Model: new EffectModelLayout(
                ModelFileOffset: Byte(executable, file + 15),
                ScaleOffset: Byte(executable, ModelScale.Locate(executable) + 33),
                OffsetInGameUnitsOffset: (int)executable.Dword(ModelOffset.Locate(executable) + 4),
                AnimationTokenOffset: Byte(executable, animation + 39),
                AnimationTimeScaleOffset: Byte(executable, ModelTimeScale.Locate(executable) + 8),
                DelayMillisecondsOffset: Byte(executable, ModelDelay.Locate(executable) + 11),
                FlagsOffset: Byte(executable, ModelFlags.Locate(executable) + 6),
                PlacementBoneTokenOffset: Byte(executable, ModelPlacementBone.Locate(executable) + 11)),
            BoneModel: new EffectAttachedModelLayout(Byte(executable, file + 30), Byte(executable, animation + 49)),
            WeaponTrail: new EffectAttachedModelLayout(Byte(executable, file + 50), Byte(executable, animation + 29)),
            Decal: new EffectDecalLayout(
                TextureFilesOffset: textureCount - ArrayCountAfterPointer,
                TextureFileCountOffset: textureCount,
                ProjectionSizeOffset: Byte(executable, projection + 5),
                ProjectionDepthOffset: Byte(executable, projection + 14),
                MaterialFileOffset: Byte(executable, DecalMaterial.Locate(executable) + 3),
                DelayMillisecondsOffset: Byte(executable, DecalDelay.Locate(executable) + 16),
                DurationMillisecondsOffset: Byte(executable, DecalDuration.Locate(executable) + 5)));
    }

    private static byte Byte(ExecutableImage executable, ulong address) => executable.Bytes(address, 1)[0];
}
