namespace Gw2.Formats;

public sealed record ItemContentLayout(uint ItemContentType, int ItemTypeOffset, uint WeaponItemType, int SubRecordOffset, int WeaponTypeOffset, int NameTextOffset, int DefaultSkinOffset)
{
    private static readonly CodeAnchor ItemConfigLookup = CodeAnchor.Site(
        "4C 8D 44 24 ?? BA ?? 00 00 00 B9 9B 00 00 00 E8 ?? ?? ?? ?? 48 8B F0 39 7C 24", 0);
    private static readonly CodeAnchor WeaponSetFlags = CodeAnchor.Site(
        "C7 07 00 00 00 00 83 7E ?? ?? 0F 85 ?? ?? ?? ?? 48 8B 76 ?? 48 85 F6 75 ?? 41 B8 ?? ?? ?? ?? " +
        "48 8D 15 ?? ?? ?? ?? 48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 8B 4E ?? 8D 43 E6", 0);

    private static readonly CodeAnchor DefaultSkinLookup = CodeAnchor.Site(
        "8B 41 2C 85 C0 74 ?? 83 F8 02 74 ?? 83 F8 09 74 ?? 83 F8 18 75 ?? 48 8B 41 ?? 48 8B", 0);
    private const int DefaultSkinLoadOffset = 28;
    private const byte LoadWithoutDisplacement = 0x00;
    private const byte LoadWithByteDisplacement = 0x40;

    private static readonly CodeAnchor ItemAgentName = CodeAnchor.Site(
        "48 8B 4B 78 48 8B 01 FF 50 08 8B 88 ?? ?? ?? ?? 89 4C 24 ?? 48 8D 4C 24 ?? E8", 0);

    private static ItemContentLayout? _current;

    public static ItemContentLayout Current => _current ??= FromExecutable(ExecutableImage.Current);

    public static ItemContentLayout FromExecutable(string executablePath) => FromExecutable(ExecutableImage.Open(executablePath));

    internal static ItemContentLayout FromExecutable(ExecutableImage executable)
    {
        ulong lookup = ItemConfigLookup.Locate(executable);
        ulong weapon = WeaponSetFlags.Locate(executable);
        return new ItemContentLayout(
            ItemContentType: executable.Dword(lookup + 6),
            ItemTypeOffset: Byte(executable, weapon + 8),
            WeaponItemType: Byte(executable, weapon + 9),
            SubRecordOffset: Byte(executable, weapon + 19),
            WeaponTypeOffset: Byte(executable, weapon + 52),
            NameTextOffset: (int)executable.Dword(ItemAgentName.Locate(executable) + 12),
            DefaultSkinOffset: DefaultSkinOffsetOf(executable, DefaultSkinLookup.Locate(executable) + DefaultSkinLoadOffset));
    }

    private static int DefaultSkinOffsetOf(ExecutableImage executable, ulong load) => Byte(executable, load) switch
    {
        LoadWithoutDisplacement => 0,
        LoadWithByteDisplacement => Byte(executable, load + 1),
        var mode => throw new System.IO.InvalidDataException($"Default skin load uses ModRM 0x{mode:X2}."),
    };

    private static byte Byte(ExecutableImage executable, ulong address) => executable.Bytes(address, 1)[0];
}
