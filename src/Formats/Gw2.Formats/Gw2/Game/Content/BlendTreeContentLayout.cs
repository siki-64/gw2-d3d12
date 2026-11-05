namespace Gw2.Formats;

public sealed record BlendTreeWeightedOptionsLayout(int ArrayOffset, int CountOffset);

public sealed record BlendTreeComparisonLayout(
    int ParameterOffset,
    int ComparisonOffset,
    int ThresholdPercentOffset,
    int BlendTimeMillisecondsOffset,
    int FlagsOffset,
    int RangeMinimumOffset,
    int RangeMaximumOffset);

public sealed record BlendTreeDrivenLayout(int ParameterOffset, int FlagsOffset, int RangeMinimumOffset, int RangeMaximumOffset);

public sealed class BlendTreeContentLayout
{
    public const int PriorityClipType = 0;
    public const int LastClipType = 3;
    public const int DirectionSelectType = 5;
    public const int WeightedRandomType = 6;
    public const int WeightedRandomOptionsFirstType = 7;
    public const int DrivenType = 11;
    public const int SelectorType = 14;
    public const int ComparisonType = 20;

    private const byte CallOpcode = 0xE8;
    private const byte LoadOpcode = 0x8B;
    private const int CallLength = 5;
    private const int LoadLookBehind = 24;
    private const int SibRm = 4;
    private const int RipRm = 5;

    private static readonly CodeAnchor TreeLookup = CodeAnchor.Site(
        "44 8B C5 BA ?? ?? ?? ?? 48 8B 88 E0 00 00 00 48 8B 01 FF 50 58 48 8B D8 E8 ?? ?? ?? ?? 45 33 FF 41 BC 01 00 00 00", 0);
    private static readonly CodeAnchor TreeSetup = CodeAnchor.Site(
        "48 8B 43 ?? 48 8B D3 48 8B 48 ?? 48 89 4F 08 48 8B CF 8B 43 ?? 89 47 10 8B 43 ?? 89 47 14 66 0F 6E 43 ?? 0F 5B C0 " +
        "F3 0F 59 05 ?? ?? ?? ?? F3 0F 11 47 18 8B 43 ?? 89 47 1C 8B 43 ?? 89 47 20 4C 8B 43 ?? E8", 0);
    private static readonly CodeAnchor OpCount = CodeAnchor.Site("48 81 EC ?? ?? ?? ?? 8B 7A ?? 48 8D 59 ?? 48 8B F2 4C 8B E1", 0);
    private static readonly CodeAnchor OpSwitch = CodeAnchor.Site(
        "49 C1 E6 ?? 4C 03 76 ?? 41 8B DD 48 89 9D ?? ?? ?? ?? 4C 89 75 ?? 49 63 46 ?? 83 F8 ?? 0F 87 ?? ?? ?? ?? " +
        "8B 8C 82 ?? ?? ?? ?? 48 03 CA FF E1", 0);
    private static readonly CodeAnchor OpData = CodeAnchor.Site("49 8B 4E ??", 0);
    private static readonly CodeAnchor OptionsCount = CodeAnchor.Site("49 8B 56 ?? 48 8D 4D ?? 8B 52 ?? E8", 0);
    private static readonly CodeAnchor OptionsArray = CodeAnchor.Site("49 8B CC 48 8B 95 ?? ?? ?? ?? 8B FB", 0);
    private static readonly CodeAnchor OptionChild = CodeAnchor.Site("4C 8D 3C 7F 4F 8B 04 F8 E8", 0);
    private static readonly CodeAnchor OptionCooldown = CodeAnchor.Site("66 42 0F 6E 44 F8 ?? 48 8B 45 ?? 0F 5B C0", 0);
    private static readonly CodeAnchor OptionWeight = CodeAnchor.Site("42 8B 4C F8 ?? 48 8B 45 ?? 89 4C F8", 0);
    private static readonly CodeAnchor Comparison = CodeAnchor.Site(
        "41 8B 48 ?? 66 41 0F 6E 60 ?? 66 41 0F 6E 48 ?? F3 41 0F 10 50 ?? F3 41 0F 10 40 ?? F3 41 0F 10 58 ?? 45 8B 48 ?? 45 8B 00", 0);
    private static readonly CodeAnchor Driven = CodeAnchor.Site("F3 41 0F 10 41 ?? F3 41 0F 10 49 ?? 45 8B 01 45 8B 49 ??", 0);
    private static readonly CodeAnchor Selector = CodeAnchor.Site("F3 0F 10 59 ?? 44 8B 01", 0);
    private static readonly CodeAnchor DirectionSelect = CodeAnchor.Site("44 8B 49 ?? 44 8B 01", 0);

    private static BlendTreeContentLayout? _current;

    private BlendTreeContentLayout() { }

    public uint TreeContentType { get; private init; }
    public int OpsOffset { get; private init; }
    public int OpCountOffset { get; private init; }
    public int OpSize { get; private init; }
    public int OpTypeOffset { get; private init; }
    public int OpDataOffset { get; private init; }
    public int RootOffset { get; private init; }
    public int BlendTimeMillisecondsOffset { get; private init; }
    public IReadOnlyList<IReadOnlyList<int>> ChildOffsetsByOpType { get; private init; } = [];
    public IReadOnlyList<int?> ClipTokenOffsetByOpType { get; private init; } = [];
    public IReadOnlyDictionary<int, BlendTreeWeightedOptionsLayout> WeightedOptionsByOpType { get; private init; } = new Dictionary<int, BlendTreeWeightedOptionsLayout>();
    public int OptionSize { get; private init; }
    public int OptionChildOffset { get; private init; }
    public int OptionWeightOffset { get; private init; }
    public int OptionCooldownMillisecondsOffset { get; private init; }
    public BlendTreeComparisonLayout ComparisonFields { get; private init; } = null!;
    public BlendTreeDrivenLayout DrivenFields { get; private init; } = null!;
    public int SelectorParameterOffset { get; private init; }
    public int SelectorLengthThresholdOffset { get; private init; }
    public int DirectionMeasuredVectorKindOffset { get; private init; }
    public int DirectionReferenceVectorKindOffset { get; private init; }

    public int OpTypeCount => ChildOffsetsByOpType.Count;

    public static BlendTreeContentLayout Current => _current ??= FromExecutable(ExecutableImage.Current);

    public static BlendTreeContentLayout FromExecutable(string executablePath) => FromExecutable(ExecutableImage.Open(executablePath));

    internal static BlendTreeContentLayout FromExecutable(ExecutableImage executable)
    {
        ulong lookup = TreeLookup.Locate(executable);
        ulong setup = TreeSetup.Locate(executable);
        ulong resolveCall = setup + 67;
        ulong resolve = resolveCall + CallLength + (ulong)(long)(int)executable.Dword(resolveCall + 1);
        ulong count = OpCount.Locate(executable);
        ulong dispatch = OpSwitch.Locate(executable);
        var cases = CaseRanges(executable, dispatch);

        var (sixStart, sixEnd) = cases[WeightedRandomType];
        ulong optionChild = OptionChild.LocateWithin(executable, sixStart, sixEnd);
        ulong comparison = Comparison.LocateWithin(executable, cases[ComparisonType].Start, cases[ComparisonType].End);
        ulong driven = Driven.LocateWithin(executable, cases[DrivenType].Start, cases[DrivenType].End);
        ulong selector = Selector.LocateWithin(executable, cases[SelectorType].Start, cases[SelectorType].End);
        ulong direction = DirectionSelect.LocateWithin(executable, cases[DirectionSelectType].Start, cases[DirectionSelectType].End);

        return new BlendTreeContentLayout
        {
            TreeContentType = executable.Dword(lookup + 4),
            OpsOffset = Byte(executable, dispatch + 7),
            OpCountOffset = Byte(executable, count + 9),
            OpSize = 1 << Byte(executable, dispatch + 3),
            OpTypeOffset = Byte(executable, dispatch + 25),
            OpDataOffset = Byte(executable, OpData.LocateWithin(executable, cases[PriorityClipType].Start, cases[PriorityClipType].Start + 4) + 3),
            RootOffset = Byte(executable, setup + 66),
            BlendTimeMillisecondsOffset = Byte(executable, setup + 34),
            ChildOffsetsByOpType = [.. cases.Select(range => (IReadOnlyList<int>)ChildOffsets(executable, range, resolve))],
            ClipTokenOffsetByOpType = [.. cases.Select((range, type) => type <= LastClipType ? LastTokenLoad(executable, range) : null)],
            WeightedOptionsByOpType = new Dictionary<int, BlendTreeWeightedOptionsLayout>
            {
                [WeightedRandomType] = WeightedOptions(executable, cases[WeightedRandomType]),
                [WeightedRandomOptionsFirstType] = WeightedOptions(executable, cases[WeightedRandomOptionsFirstType]),
            },
            OptionSize = 3 * (1 << (Byte(executable, optionChild + 7) >> 6)),
            OptionChildOffset = LoadDisplacement(executable, optionChild + 4) ?? throw Unexpected("option child"),
            OptionWeightOffset = Byte(executable, OptionWeight.LocateWithin(executable, sixStart, sixEnd) + 4),
            OptionCooldownMillisecondsOffset = Byte(executable, OptionCooldown.LocateWithin(executable, sixStart, sixEnd) + 6),
            ComparisonFields = new BlendTreeComparisonLayout(
                ParameterOffset: LoadDisplacement(executable, comparison + 38) ?? throw Unexpected("comparison parameter"),
                ComparisonOffset: Byte(executable, comparison + 37),
                ThresholdPercentOffset: Byte(executable, comparison + 27),
                BlendTimeMillisecondsOffset: Byte(executable, comparison + 15),
                FlagsOffset: Byte(executable, comparison + 3),
                RangeMinimumOffset: Byte(executable, comparison + 33),
                RangeMaximumOffset: Byte(executable, comparison + 21)),
            DrivenFields = new BlendTreeDrivenLayout(
                ParameterOffset: LoadDisplacement(executable, driven + 12) ?? throw Unexpected("driven parameter"),
                FlagsOffset: Byte(executable, driven + 18),
                RangeMinimumOffset: Byte(executable, driven + 11),
                RangeMaximumOffset: Byte(executable, driven + 5)),
            SelectorParameterOffset = LoadDisplacement(executable, selector + 5) ?? throw Unexpected("selector parameter"),
            SelectorLengthThresholdOffset = Byte(executable, selector + 4),
            DirectionMeasuredVectorKindOffset = LoadDisplacement(executable, direction + 4) ?? throw Unexpected("measured vector kind"),
            DirectionReferenceVectorKindOffset = Byte(executable, direction + 3),
        };
    }

    private static List<(ulong Start, ulong End)> CaseRanges(ExecutableImage executable, ulong dispatch)
    {
        int caseCount = Byte(executable, dispatch + 28) + 1;
        ulong table = executable.ImageBase + executable.Dword(dispatch + 38);
        var starts = new ulong[caseCount];
        for (int index = 0; index < caseCount; index++) starts[index] = executable.ImageBase + executable.Dword(table + (ulong)(index * sizeof(uint)));
        var ordered = starts.Distinct().Order().ToList();
        if (ordered[^1] >= table) throw Unexpected("jump table after the blend op cases");
        return [.. starts.Select(start => (start, ordered.FirstOrDefault(other => other > start, table)))];
    }

    private static BlendTreeWeightedOptionsLayout WeightedOptions(ExecutableImage executable, (ulong Start, ulong End) range)
    {
        ulong countLoad = OptionsCount.LocateWithin(executable, range.Start, range.End);
        ulong arrayUse = OptionsArray.LocateWithin(executable, range.Start, range.End);
        int arrayOffset = LoadEndingAt(executable, arrayUse) ?? throw Unexpected("options array");
        return new BlendTreeWeightedOptionsLayout(arrayOffset, Byte(executable, countLoad + 10));
    }

    private static List<int> ChildOffsets(ExecutableImage executable, (ulong Start, ulong End) range, ulong resolve)
    {
        var offsets = new List<int>();
        var code = executable.Bytes(range.Start, checked((int)(range.End - range.Start)));
        for (int index = 0; index + CallLength <= code.Length; index++)
        {
            if (code[index] != CallOpcode) continue;
            ulong call = range.Start + (ulong)index;
            if (call + CallLength + (ulong)(long)(int)executable.Dword(call + 1) != resolve) continue;
            if (LoadEndingAt(executable, call) is { } offset) offsets.Add(offset);
        }
        offsets.Sort();
        return offsets;
    }

    private static int? LastTokenLoad(ExecutableImage executable, (ulong Start, ulong End) range)
    {
        int? last = null;
        for (ulong address = range.Start; address + 3 <= range.End; address++)
            if (Byte(executable, address) == 0x4D && Byte(executable, address + 1) == LoadOpcode && (Byte(executable, address + 2) & 0x3F) == 0
                && LoadDisplacement(executable, address) is { } offset)
                last = offset;
        return last;
    }

    private static int? LoadEndingAt(ExecutableImage executable, ulong end)
    {
        for (ulong start = end - 1; start >= end - LoadLookBehind; start--)
        {
            if (R8LoadLength(executable, start) is not { } length || start + (ulong)length > end) continue;
            return (Byte(executable, start + 2) & 7) == SibRm ? null : LoadDisplacement(executable, start);
        }
        return null;
    }

    private static int? R8LoadLength(ExecutableImage executable, ulong start)
    {
        if ((Byte(executable, start) & 0xFC) != 0x4C || Byte(executable, start + 1) != LoadOpcode) return null;
        byte modrm = Byte(executable, start + 2);
        int mod = modrm >> 6, register = (modrm >> 3) & 7, rm = modrm & 7;
        if (register != 0 || mod == 3 || (mod == 0 && rm == RipRm)) return null;
        return 3 + (rm == SibRm ? 1 : 0) + mod switch { 1 => 1, 2 => 4, _ => 0 };
    }

    private static int? LoadDisplacement(ExecutableImage executable, ulong start)
    {
        byte modrm = Byte(executable, start + 2);
        int mod = modrm >> 6, rm = modrm & 7;
        if (mod == 3 || (mod == 0 && rm == RipRm)) return null;
        int displacementAt = rm == SibRm ? 4 : 3;
        return mod switch
        {
            0 => 0,
            1 => (sbyte)Byte(executable, start + (ulong)displacementAt),
            _ => (int)executable.Dword(start + (ulong)displacementAt),
        };
    }

    private static InvalidOperationException Unexpected(string what) => new($"The blend tree layout has no {what} load where expected.");

    private static byte Byte(ExecutableImage executable, ulong address) => executable.Bytes(address, 1)[0];
}
