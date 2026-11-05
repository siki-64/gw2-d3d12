namespace Gw2.Formats;

internal sealed class CodeAnchor
{
    private const string Wildcard = "??";

    private readonly byte?[] _pattern;
    private readonly int _offset;
    private readonly int _instructionEnd;
    private readonly (int DisplacementOffset, int InstructionEnd)? _followedReference;
    private readonly long _shift;

    private CodeAnchor(byte?[] pattern, int offset, int instructionEnd, (int, int)? followedReference = null, long shift = 0)
    {
        _pattern = pattern;
        _offset = offset;
        _instructionEnd = instructionEnd;
        _followedReference = followedReference;
        _shift = shift;
    }

    internal static CodeAnchor Site(string pattern, int offset) => new(Parse(pattern), offset, 0);

    internal static CodeAnchor Reference(string pattern, int displacementOffset, int instructionEnd) =>
        new(Parse(pattern), displacementOffset, instructionEnd);

    internal CodeAnchor ThenReference(int displacementOffset, int instructionEnd) =>
        new(_pattern, _offset, _instructionEnd, (displacementOffset, instructionEnd), _shift);

    internal CodeAnchor Shifted(long shift) => new(_pattern, _offset, _instructionEnd, _followedReference, shift);

    internal ulong Locate(ExecutableImage executable)
    {
        var (start, _) = executable.Section(".text");
        var code = executable.SectionBytes(".text");
        int match = FindUnique(code);
        ulong address = start + (ulong)match + (ulong)_offset;
        if (_instructionEnd != 0) address = RelativeTarget(executable, address, start + (ulong)match + (ulong)_instructionEnd);
        if (_followedReference is var (displacementOffset, instructionEnd))
            address = RelativeTarget(executable, address + (ulong)displacementOffset, address + (ulong)instructionEnd);
        return address + (ulong)_shift;
    }

    internal ulong LocateWithin(ExecutableImage executable, ulong start, ulong end)
    {
        var code = executable.Bytes(start, checked((int)(end - start)));
        return start + (ulong)FindUnique(code) + (ulong)_offset;
    }

    internal ulong? TryLocateWithin(ExecutableImage executable, ulong start, ulong end)
    {
        var code = executable.Bytes(start, checked((int)(end - start)));
        return FindAll(code) is [var match] ? start + (ulong)match + (ulong)_offset : null;
    }

    internal List<ulong> LocateAll(ExecutableImage executable)
    {
        var (start, _) = executable.Section(".text");
        return FindAll(executable.SectionBytes(".text")).Select(match => start + (ulong)match + (ulong)_offset).ToList();
    }

    private List<int> FindAll(ReadOnlySpan<byte> code)
    {
        var matches = new List<int>();
        for (int candidate = 0; candidate + _pattern.Length <= code.Length; candidate++)
            if (Matches(code[candidate..])) matches.Add(candidate);
        return matches;
    }

    private static ulong RelativeTarget(ExecutableImage executable, ulong displacement, ulong instructionEnd) =>
        instructionEnd + (ulong)(long)(int)executable.Dword(displacement);

    private int FindUnique(ReadOnlySpan<byte> code)
    {
        var (runStart, runLength) = LongestLiteralRun();
        var run = new byte[runLength];
        for (int index = 0; index < runLength; index++) run[index] = _pattern[runStart + index]!.Value;

        int found = -1;
        for (int searched = 0; ;)
        {
            int next = code[searched..].IndexOf(run);
            if (next < 0) break;
            int candidate = searched + next - runStart;
            searched += next + 1;
            if (candidate < 0 || candidate + _pattern.Length > code.Length || !Matches(code[candidate..])) continue;
            if (found >= 0) throw new InvalidOperationException($"Code anchor {Describe()} matches more than once.");
            found = candidate;
        }
        return found >= 0 ? found : throw new InvalidOperationException($"Code anchor {Describe()} does not match.");
    }

    private (int Start, int Length) LongestLiteralRun()
    {
        (int Start, int Length) longest = (0, 0);
        for (int index = 0, start = 0; index <= _pattern.Length; index++)
        {
            if (index < _pattern.Length && _pattern[index] is not null) continue;
            if (index - start > longest.Length) longest = (start, index - start);
            start = index + 1;
        }
        return longest;
    }

    private bool Matches(ReadOnlySpan<byte> bytes)
    {
        for (int index = 0; index < _pattern.Length; index++)
            if (_pattern[index] is { } expected && bytes[index] != expected) return false;
        return true;
    }

    private string Describe() => string.Join(' ', _pattern.Select(value => value?.ToString("X2") ?? Wildcard));

    private static byte?[] Parse(string pattern) =>
        [.. pattern.Split(' ').Select(token => token == Wildcard ? (byte?)null : Convert.ToByte(token, 16))];
}
