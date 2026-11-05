namespace Gw2.Formats;

public sealed record SkinContentLayout(
    uint SkinContentType,
    int DefaultModelFileOffset,
    int SkinTypeOffset,
    int TypeDataOffset,
    int SocketOffset,
    int WeaponKindOffset)
{
    public const uint ArmorSkinType = 0;
    public const uint WeaponSkinType = 24;

    private static readonly CodeAnchor DecorationSkinLookup = CodeAnchor.Site(
        "44 8B C6 BA ?? ?? ?? ?? 48 8B 88 E0 00 00 00 48 8B 01 FF 50 70 48 85 C0 74 ?? 83 B8 ?? ?? ?? ?? 00 75 ?? 48 8B 88 ?? ?? ?? ??", 0);
    private static readonly CodeAnchor DefaultModel = CodeAnchor.Site("83 F9 03 74 ?? 48 83 C0 ?? 48 3B C2 75 ?? 48 8B 4F ?? 48 89 0B", 0);
    private static readonly CodeAnchor StowedSocket = CodeAnchor.Site("4C 63 8F ?? ?? ?? ?? 45 85 C9 0F 84", 0);
    private static readonly CodeAnchor HeldWeaponKind = CodeAnchor.Site("48 8B 87 ?? ?? ?? ?? 8B 48 ?? 83 C1 FE", 0);

    private static SkinContentLayout? _current;

    public static SkinContentLayout Current => _current ??= FromExecutable(ExecutableImage.Current);

    public static SkinContentLayout FromExecutable(string executablePath) => FromExecutable(ExecutableImage.Open(executablePath));

    internal static SkinContentLayout FromExecutable(ExecutableImage executable)
    {
        ulong lookup = DecorationSkinLookup.Locate(executable);
        return new SkinContentLayout(
            SkinContentType: executable.Dword(lookup + 4),
            DefaultModelFileOffset: executable.Bytes(DefaultModel.Locate(executable) + 17, 1)[0],
            SkinTypeOffset: (int)executable.Dword(lookup + 28),
            TypeDataOffset: (int)executable.Dword(lookup + 38),
            SocketOffset: (int)executable.Dword(StowedSocket.Locate(executable) + 3),
            WeaponKindOffset: executable.Bytes(HeldWeaponKind.Locate(executable) + 9, 1)[0]);
    }
}
