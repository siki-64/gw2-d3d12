namespace Gw2.Formats;

public sealed record CommonSkillPaletteLayout(Guid DownedPaletteKey)
{
    private const int FirstKeyDwordOffset = 0x1F;
    private const int KeyDwordStride = 7;
    private const int KeyDwordCount = 4;

    private static readonly CodeAnchor CommonPaletteSwitch = CodeAnchor.Site("83 FB 22 0F 87 ?? ?? ?? ?? 48 8D 15 ?? ?? ?? ?? 8B 8C 9A ?? ?? ?? ?? 48 03 CA FF E1 C7 45 ?? ?? ?? ?? ?? C7 45", 0);

    private static CommonSkillPaletteLayout? _current;

    public static CommonSkillPaletteLayout Current => _current ??= FromExecutable(ExecutableImage.Current);

    public static CommonSkillPaletteLayout FromExecutable(string executablePath) => FromExecutable(ExecutableImage.Open(executablePath));

    internal static CommonSkillPaletteLayout FromExecutable(ExecutableImage executable)
    {
        ulong commonPaletteSwitch = CommonPaletteSwitch.Locate(executable);
        var key = new byte[KeyDwordCount * sizeof(uint)];
        for (int dword = 0; dword < KeyDwordCount; dword++)
            BitConverter.GetBytes(executable.Dword(commonPaletteSwitch + FirstKeyDwordOffset + (ulong)(dword * KeyDwordStride))).CopyTo(key, dword * sizeof(uint));
        return new CommonSkillPaletteLayout(new Guid(key));
    }
}
