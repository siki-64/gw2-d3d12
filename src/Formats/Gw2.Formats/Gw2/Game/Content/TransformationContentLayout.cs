namespace Gw2.Formats;

public sealed record TransformationContentLayout(
    uint TransformationContentType,
    int BundleOffset,
    int BundleMainHandItemOffset,
    int BundleOffHandItemOffset,
    int BundlePaletteOffset,
    int BundleDownedPaletteOffset,
    int WeaponPaletteOffset)
{
    public const uint BundleTransformationType = 5;

    private static readonly CodeAnchor TransformationLookup = CodeAnchor.Site("83 7B 06 00 48 8B B8 ?? ?? ?? ?? 74 ?? E8 ?? ?? ?? ?? 44 8B 43 06 BA ?? ?? 00 00", 0);
    private static readonly CodeAnchor BundlePalettes = CodeAnchor.Site(
        "BA 05 00 00 00 FF 50 08 48 85 C0 0F 84 ?? ?? ?? ?? 48 8B 58 ?? 48 85 DB 0F 84 ?? ?? ?? ?? 48 8B 53 ?? 48 85 D2 74 ?? " +
        "49 8B 06 48 8D 4C 24 ?? 48 89 44 24 ?? E8 ?? ?? ?? ?? 85 C0 75 ?? 48 8B 53 ?? 48 85 D2 74 ?? " +
        "49 8B 06 48 8D 4C 24 ?? 48 89 44 24 ?? E8 ?? ?? ?? ?? 85 C0 75 ?? 48 8B 3B", 0);
    private const int BundleOffsetImmediate = 0x14;
    private const int BundlePaletteImmediate = 0x21;
    private const int BundleDownedPaletteImmediate = 0x40;
    private const int MainHandItemInstruction = 0x5C;
    private const int WeaponPaletteImmediate = 0x97;
    private const int OffHandItemImmediate = 0xBD;
    private const byte ReadThroughRbxWithoutDisplacement = 0x3B;

    private static TransformationContentLayout? _current;

    public static TransformationContentLayout Current => _current ??= FromExecutable(ExecutableImage.Current);

    public static TransformationContentLayout FromExecutable(string executablePath) => FromExecutable(ExecutableImage.Open(executablePath));

    internal static TransformationContentLayout FromExecutable(ExecutableImage executable)
    {
        var contentTypes = TransformationLookup.LocateAll(executable).Select(lookup => executable.Dword(lookup + 0x17)).Distinct().ToArray();
        if (contentTypes.Length != 1) throw new InvalidOperationException("The transformation message handlers disagree on the content type.");
        ulong bundle = BundlePalettes.Locate(executable);
        if (Byte(executable, bundle + MainHandItemInstruction + 2) != ReadThroughRbxWithoutDisplacement)
            throw new InvalidOperationException("The bundle main-hand item read has moved.");
        return new TransformationContentLayout(
            TransformationContentType: contentTypes[0],
            BundleOffset: Byte(executable, bundle + BundleOffsetImmediate),
            BundleMainHandItemOffset: 0,
            BundleOffHandItemOffset: Byte(executable, bundle + OffHandItemImmediate),
            BundlePaletteOffset: Byte(executable, bundle + BundlePaletteImmediate),
            BundleDownedPaletteOffset: Byte(executable, bundle + BundleDownedPaletteImmediate),
            WeaponPaletteOffset: Byte(executable, bundle + WeaponPaletteImmediate));
    }

    private static byte Byte(ExecutableImage executable, ulong address) => executable.Bytes(address, 1)[0];
}
