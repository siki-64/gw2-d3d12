namespace Gw2.Formats;

public sealed record LegendSkillPaletteLayout(
    uint FirstLegendProfessionState,
    IReadOnlyList<Guid> PaletteConfigKeys,
    uint ConfigContentType,
    int ConfigValueOffset)
{
    public const int ConfigValueTypeOffset = 0x00;
    public const int ConfigValueObjectOffset = 0x08;
    private const ushort StoreOpcode = 0x05C7;
    private const int StoreDisplacementOffset = 2;
    private const int StoreImmediateOffset = 6;
    private const int StoreInstructionSize = 10;
    private const int ConfigContentTypeOffset = 4;
    private const int ConfigKeyOffset = 8;
    private const int ConfigKeyDwordCount = 4;
    private const int ConfigEntryCodeSpan = 0x80;

    private static readonly CodeAnchor LegendPaletteConfigLookup = CodeAnchor.Site(
        "83 C3 ?? 83 FB ?? 72 ?? 41 B8 ?? ?? 00 00 48 8D 15 ?? ?? ?? ?? 48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 8D 0D ?? ?? ?? ?? 8B 04 99", 0);
    private const int LegendPaletteConfigTableDisplacement = 36;
    private const int LegendPaletteConfigTableInstructionEnd = 40;

    private static readonly CodeAnchor ConfigDefinitionTypeTest = CodeAnchor.Site(
        "3D ?? 00 00 00 75 ?? 33 C9 3D ?? 00 00 00 0F 94 C1 41 89 0E", 1);

    private static readonly CodeAnchor LegendPaletteConfigValueRead = CodeAnchor.Site(
        "83 7C 24 ?? 00 74 ?? 48 85 C0 75 ?? 33 C0 48 83 C4 20 5B C3 48 8D 48 ?? E8", 23);
    private const int LegendPaletteConfigValueSearchSpan = 0x30;

    private static LegendSkillPaletteLayout? _current;

    public static LegendSkillPaletteLayout Current => _current ??= FromExecutable(ExecutableImage.Current);

    public static LegendSkillPaletteLayout FromExecutable(string executablePath) => FromExecutable(ExecutableImage.Open(executablePath));

    internal static LegendSkillPaletteLayout FromExecutable(ExecutableImage executable)
    {
        ulong lookup = LegendPaletteConfigLookup.Locate(executable);
        uint firstState = (uint)-(sbyte)Byte(executable, lookup + 2);
        int legendCount = Byte(executable, lookup + 5);
        ulong table = lookup + LegendPaletteConfigTableInstructionEnd + (ulong)(long)(int)executable.Dword(lookup + LegendPaletteConfigTableDisplacement);
        var keys = new Guid[legendCount];
        for (int legend = 0; legend < legendCount; legend++)
            keys[legend] = ConfigKeyOf(executable, executable.Dword(table + (ulong)(legend * sizeof(uint))));
        return new LegendSkillPaletteLayout(
            FirstLegendProfessionState: firstState,
            PaletteConfigKeys: keys,
            ConfigContentType: executable.Dword(ConfigDefinitionTypeTest.Locate(executable)),
            ConfigValueOffset: ConfigValueOffsetOf(executable, lookup));
    }

    private static int ConfigValueOffsetOf(ExecutableImage executable, ulong lookup)
    {
        ulong lookupFunction = executable.FunctionStart(lookup) ?? throw new InvalidOperationException("The legend palette config lookup is outside any function.");
        foreach (ulong call in executable.CallSites([lookupFunction])[lookupFunction])
            if (LegendPaletteConfigValueRead.TryLocateWithin(executable, call, call + LegendPaletteConfigValueSearchSpan) is { } read)
                return Byte(executable, read);
        throw new InvalidOperationException("No caller of the legend palette config lookup reads the config value.");
    }

    internal static Guid ConfigKeyOf(ExecutableImage executable, uint configId)
    {
        var (codeStart, _) = executable.Section(".text");
        var code = executable.SectionBytes(".text");
        Span<byte> idStore = stackalloc byte[sizeof(uint)];
        BitConverter.TryWriteBytes(idStore, configId);
        for (int index = StoreImmediateOffset; index + sizeof(uint) <= code.Length; index++)
        {
            int found = code[index..].IndexOf(idStore);
            if (found < 0) break;
            index += found;
            ulong store = codeStart + (ulong)(index - StoreImmediateOffset);
            if (BitConverter.ToUInt16(code[(index - StoreImmediateOffset)..]) == StoreOpcode && ConfigKeyAt(executable, store) is { } key) return key;
        }
        throw new InvalidOperationException($"Config {configId} has no content key in the executable.");
    }

    private static Guid? ConfigKeyAt(ExecutableImage executable, ulong idStore)
    {
        ulong idAddress = StoreTarget(executable, idStore);
        var key = new byte[ConfigKeyDwordCount * sizeof(uint)];
        var found = new bool[ConfigKeyDwordCount];
        bool hasContentType = false;
        for (ulong store = idStore - ConfigEntryCodeSpan; store < idStore + ConfigEntryCodeSpan; store++)
        {
            if ((ushort)executable.Dword(store) != StoreOpcode) continue;
            long fieldOffset = (long)(StoreTarget(executable, store) - idAddress);
            if (fieldOffset == ConfigContentTypeOffset) hasContentType = true;
            long keyOffset = fieldOffset - ConfigKeyOffset;
            if (keyOffset < 0 || keyOffset % sizeof(uint) != 0 || keyOffset / sizeof(uint) >= ConfigKeyDwordCount) continue;
            int part = (int)(keyOffset / sizeof(uint));
            BitConverter.GetBytes(executable.Dword(store + StoreImmediateOffset)).CopyTo(key, part * sizeof(uint));
            found[part] = true;
        }
        return hasContentType && !found.Contains(false) ? new Guid(key) : null;
    }

    private static ulong StoreTarget(ExecutableImage executable, ulong store) =>
        store + StoreInstructionSize + (ulong)(long)(int)executable.Dword(store + StoreDisplacementOffset);

    private static byte Byte(ExecutableImage executable, ulong address) => executable.Bytes(address, 1)[0];
}
