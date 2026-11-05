namespace Gw2.Formats;

public sealed record SpeciesContentLayout(int NameTextOffset, int ModelFileOffset)
{
    private static readonly CodeAnchor CharacterNameFromSpecies = CodeAnchor.Site(
        "FF 90 ?? ?? ?? ?? 48 8B 44 24 ?? 8B 88 ?? ?? ?? ?? 89 4C 24 ?? 48 8D 4C 24 ?? E8", 0);

    private static readonly CodeAnchor SpeciesModelFile = CodeAnchor.Site(
        "48 8B 9F ?? ?? ?? ?? BA 01 00 00 00 48 8B CB E8 ?? ?? ?? ?? 48 85 DB 4C 8D 3D", 0);

    private static SpeciesContentLayout? _current;

    public static SpeciesContentLayout Current => _current ??= FromExecutable(ExecutableImage.Current);

    public static SpeciesContentLayout FromExecutable(string executablePath) => FromExecutable(ExecutableImage.Open(executablePath));

    internal static SpeciesContentLayout FromExecutable(ExecutableImage executable) =>
        new(
            (int)executable.Dword(CharacterNameFromSpecies.Locate(executable) + 13),
            (int)executable.Dword(SpeciesModelFile.Locate(executable) + 3));
}
