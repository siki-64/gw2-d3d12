namespace Gw2.Formats;

public sealed record TraitContentLayout(int NameTextOffset)
{
    private static readonly CodeAnchor TraitNameRead = CodeAnchor.Site(
        "48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 8B 47 ?? 48 8D 4C 24 ?? 89 44 24 ?? E8 ?? ?? ?? ?? 4C 8B C0 45 33 C9 BA 0D 00 00 00", 0);

    private static TraitContentLayout? _current;

    public static TraitContentLayout Current => _current ??= FromExecutable(ExecutableImage.Current);

    public static TraitContentLayout FromExecutable(string executablePath) => FromExecutable(ExecutableImage.Open(executablePath));

    internal static TraitContentLayout FromExecutable(ExecutableImage executable) =>
        new(executable.Bytes(TraitNameRead.Locate(executable) + 14, 1)[0]);
}
