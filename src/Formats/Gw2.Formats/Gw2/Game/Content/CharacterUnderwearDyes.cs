namespace Gw2.Formats;

public sealed record CharacterUnderwearDyes(uint ColorContentType, IReadOnlyList<Guid> Colors)
{
    public const int RaceCount = 5;
    public const int SexCount = 2;
    public const int ColorsPerCharacter = 4;
    private const int GuidSize = 16;
    private const int TableBias = 8;
    private const int ColorContentTypeOffset = 28;

    private static readonly CodeAnchor UnderwearDyeTable = CodeAnchor.Reference(
        "48 63 C2 48 8D 1C 80 48 63 C1 48 03 D8 48 8D 05 ?? ?? ?? ?? 48 C1 E3 06 48 03 D8 83 7B F8 00", 16, 20);
    private static readonly CodeAnchor ColorTypeCheck = CodeAnchor.Site(
        "48 8D 53 F8 48 8B 88 ?? ?? ?? ?? 48 8B 01 FF 50 ?? 48 8B F8 48 85 C0 74 ?? 83 78 ?? ??", 0);

    private static CharacterUnderwearDyes? _current;

    public static CharacterUnderwearDyes Current => _current ??= FromExecutable(ExecutableImage.Current);

    public static CharacterUnderwearDyes FromExecutable(string executablePath) => FromExecutable(ExecutableImage.Open(executablePath));

    internal static CharacterUnderwearDyes FromExecutable(ExecutableImage executable)
    {
        ulong table = UnderwearDyeTable.Locate(executable) - TableBias;
        ulong typeCheck = ColorTypeCheck.Locate(executable);
        var colors = new Guid[RaceCount * SexCount * ColorsPerCharacter];
        for (int index = 0; index < colors.Length; index++)
            colors[index] = new Guid(executable.Bytes(table + (ulong)(index * GuidSize), GuidSize));
        return new CharacterUnderwearDyes(executable.Bytes(typeCheck + ColorContentTypeOffset, 1)[0], colors);
    }

    public IReadOnlyList<Guid> ColorsOf(int race, int sex) =>
        race is >= 0 and < RaceCount && sex is >= 0 and < SexCount
            ? Colors.Skip((race + RaceCount * sex) * ColorsPerCharacter).Take(ColorsPerCharacter).ToList()
            : [];
}
