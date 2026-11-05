namespace Gw2.Formats;

public sealed record ColorPaletteContentLayout(
    uint PaletteContentType,
    int ColorsOffset,
    int ColorCountOffset,
    int ColorEntrySize,
    int VariantsOffset,
    int VariantCountOffset,
    int VariantSize,
    int VariantMaterialKeyOffset,
    float DefaultBrightness,
    float DefaultContrast,
    float DefaultHue,
    float DefaultSaturation,
    float DefaultLightness)
{
    private const int PointerArrayEntryStride = 24;

    private static readonly CodeAnchor FindColorSet = CodeAnchor.Site(
        "BA ?? ?? ?? ?? 48 8B CE FF D7 48 8B D8 48 85 C0 75 ?? BA 14 00 00 00 48 8B CD E8 ?? ?? ?? ?? EB ?? " +
        "33 D2 41 8B C6 F7 73 ?? 8B FA 3B 53 ?? 72 ?? 41 B8 ?? ?? ?? ?? 48 8D 15 ?? ?? ?? ?? 48 8D 0D ?? ?? ?? ?? " +
        "E8 ?? ?? ?? ?? 48 8B 4B ?? 4C 8D 0C 7F", 0);
    private static readonly CodeAnchor ResolveColor = CodeAnchor.Site(
        "4C 8B 49 ?? 45 33 D2 8B 41 ?? 48 8D 0C 40 4D 8D 1C C9 4D 3B CB 74 ?? 66 0F 1F 84 00 00 00 00 00 " +
        "41 8B 41 ?? 41 3B C0 0F 84 ?? ?? ?? ?? 85 C0 4D 0F 44 D1 49 83 C1 ?? 4D 3B CB 75 ?? 4D 85 D2 75 ?? 4C 8D 15", 0);

    private static ColorPaletteContentLayout? _current;

    public static ColorPaletteContentLayout Current => _current ??= FromExecutable(ExecutableImage.Current);

    public static ColorPaletteContentLayout FromExecutable(string executablePath) => FromExecutable(ExecutableImage.Open(executablePath));

    internal static ColorPaletteContentLayout FromExecutable(ExecutableImage executable)
    {
        ulong find = FindColorSet.Locate(executable);
        ulong resolve = ResolveColor.Locate(executable);
        ulong defaultVariant = resolve + 72 + (ulong)(long)(int)executable.Dword(resolve + 68);
        return new ColorPaletteContentLayout(
            PaletteContentType: executable.Dword(find + 1),
            ColorsOffset: Byte(executable, find + 76),
            ColorCountOffset: Byte(executable, find + 40),
            ColorEntrySize: PointerArrayEntryStride,
            VariantsOffset: Byte(executable, resolve + 3),
            VariantCountOffset: Byte(executable, resolve + 9),
            VariantSize: Byte(executable, resolve + 54),
            VariantMaterialKeyOffset: Byte(executable, resolve + 35),
            DefaultBrightness: executable.Single(defaultVariant),
            DefaultContrast: executable.Single(defaultVariant + 4),
            DefaultHue: executable.Single(defaultVariant + 8),
            DefaultSaturation: executable.Single(defaultVariant + 12),
            DefaultLightness: executable.Single(defaultVariant + 16));
    }

    private static byte Byte(ExecutableImage executable, ulong address) => executable.Bytes(address, 1)[0];
}
