namespace Gw2.Formats;

public sealed record ProfessionMechanicPaletteLayout(IReadOnlyDictionary<uint, Guid> PaletteConfigKeysByProfession, IReadOnlySet<uint> ToolbeltProfessions)
{
    private const int ConfigIdImmediateOffset = 6;
    private const int ProfessionSlotAfterPaletteSlot = 0x10;
    private const int ToolbeltFallbackSlotBeforePaletteSlot = 0x20;
    private const byte ReturnImmediateOpcode = 0xB8;
    private const byte ReturnOpcode = 0xC3;
    private const uint HighestProfession = 0xFF;

    private static readonly CodeAnchor PaletteConfigLookup = CodeAnchor.Site("BA ?? 00 00 00 B9 ?? ?? 00 00 E8", 0);

    private static ProfessionMechanicPaletteLayout? _current;

    public static ProfessionMechanicPaletteLayout Current => _current ??= FromExecutable(ExecutableImage.Current);

    public static ProfessionMechanicPaletteLayout FromExecutable(string executablePath) => FromExecutable(ExecutableImage.Open(executablePath));

    internal static ProfessionMechanicPaletteLayout FromExecutable(ExecutableImage executable)
    {
        uint paletteContentType = SkillPaletteContentLayout.FromExecutable(executable).PaletteContentType;
        var configIdByFunction = new Dictionary<ulong, uint>();
        foreach (ulong lookup in PaletteConfigLookup.LocateAll(executable))
        {
            if (executable.Dword(lookup + 1) != paletteContentType) continue;
            if (executable.FunctionStart(lookup) is { } function) configIdByFunction.TryAdd(function, executable.Dword(lookup + ConfigIdImmediateOffset));
        }
        var keys = new Dictionary<uint, Guid>();
        var toolbeltFallbackByProfession = new Dictionary<uint, ulong>();
        var (dataStart, dataSize) = executable.Section(".rdata");
        for (ulong slot = dataStart; slot + sizeof(ulong) <= dataStart + dataSize; slot += sizeof(ulong))
        {
            if (!configIdByFunction.TryGetValue(executable.Qword(slot), out uint configId)) continue;
            if (ProfessionReturnedBy(executable, executable.Qword(slot + ProfessionSlotAfterPaletteSlot)) is not { } profession) continue;
            keys.TryAdd(profession, LegendSkillPaletteLayout.ConfigKeyOf(executable, configId));
            toolbeltFallbackByProfession.TryAdd(profession, executable.Qword(slot - ToolbeltFallbackSlotBeforePaletteSlot));
        }
        ulong sharedFallback = toolbeltFallbackByProfession.Values.GroupBy(function => function).MaxBy(group => group.Count())?.Key ?? 0;
        var toolbeltProfessions = toolbeltFallbackByProfession.Where(entry => entry.Value != sharedFallback).Select(entry => entry.Key).ToHashSet();
        if (keys.Count == 0) throw new InvalidOperationException("No profession mechanic palette configs were found in the executable.");
        return new ProfessionMechanicPaletteLayout(keys, toolbeltProfessions);
    }

    private static uint? ProfessionReturnedBy(ExecutableImage executable, ulong function)
    {
        if (!executable.InCode(function)) return null;
        var code = executable.Bytes(function, 6);
        if (code.Length < 6 || code[0] != ReturnImmediateOpcode || code[5] != ReturnOpcode) return null;
        uint profession = executable.Dword(function + 1);
        return profession is > 0 and <= HighestProfession ? profession : null;
    }
}
