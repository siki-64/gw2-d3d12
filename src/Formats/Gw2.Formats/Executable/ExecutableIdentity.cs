namespace Gw2.Formats;

internal static class ExecutableIdentity
{
    private const byte MoveEaxImmediateOpcode = 0xB8;
    private const byte ReturnOpcode = 0xC3;
    private const uint ProgramId = 101;

    private static readonly byte?[] RegisterIdentityCalls =
    [
        0xE8, null, null, null, null,
        0x8B, 0xD8,
        0xE8, null, null, null, null,
        0x45, 0x33, 0xC0,
        0x8B, 0xD3,
        0x8B, 0xC8,
        0xE8,
    ];

    internal static int Build(ExecutableImage executable)
    {
        var (start, _) = executable.Section(".text");
        var code = executable.SectionBytes(".text");
        int? build = null;
        for (int index = 0; index + RegisterIdentityCalls.Length <= code.Length; index++)
        {
            if (!Matches(code[index..], RegisterIdentityCalls)) continue;
            ulong programIdGetter = CallTarget(start + (ulong)index);
            ulong buildGetter = CallTarget(start + (ulong)index + 7);
            if (ConstantGetter(executable, programIdGetter) != ProgramId || ConstantGetter(executable, buildGetter) is not { } value) continue;
            if (build is not null) throw new InvalidOperationException("The executable has more than one identity registration.");
            build = (int)value;
        }
        return build ?? throw new InvalidOperationException("The executable has no identity registration.");

        ulong CallTarget(ulong call) => call + 5 + (ulong)(long)(int)executable.Dword(call + 1);
    }

    private static uint? ConstantGetter(ExecutableImage executable, ulong address) =>
        executable.Bytes(address, 6) is { Length: 6 } bytes && bytes[0] == MoveEaxImmediateOpcode && bytes[5] == ReturnOpcode
            ? executable.Dword(address + 1) : null;

    private static bool Matches(ReadOnlySpan<byte> bytes, byte?[] pattern)
    {
        for (int index = 0; index < pattern.Length; index++)
            if (pattern[index] is { } expected && bytes[index] != expected) return false;
        return true;
    }
}
