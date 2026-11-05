using System;

namespace Gw2.Formats;

public sealed record CharacterFixedEffects(Guid FixedEffectEventEffect)
{
    private static readonly CodeAnchor FixedEffectEventLookup = CodeAnchor.Site(
        "C7 84 24 ?? ?? ?? ?? ?? ?? ?? ?? C7 84 24 ?? ?? ?? ?? ?? ?? ?? ?? C7 84 24 ?? ?? ?? ?? ?? ?? ?? ?? " +
        "C7 84 24 ?? ?? ?? ?? ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 8B 88 E0 00 00 00 48 8B 11 4C 8B 42 68 " +
        "48 8D 94 24 ?? ?? ?? ?? 41 FF D0 45 33 FF 48 8B E8 48 85 C0 0F 84 ?? ?? ?? ?? 81 78 10 B7 00 00 00", 0);
    private const int GuidDwordCount = 4;
    private const int StoreLength = 11;
    private const int StoreImmediateOffset = 7;
    private const int GuidLength = 16;

    private static CharacterFixedEffects? _current;

    public static CharacterFixedEffects Current => _current ??= FromExecutable(ExecutableImage.Current);

    public static CharacterFixedEffects FromExecutable(string executablePath) => FromExecutable(ExecutableImage.Open(executablePath));

    internal static CharacterFixedEffects FromExecutable(ExecutableImage executable)
    {
        ulong lookup = FixedEffectEventLookup.Locate(executable);
        var guidBytes = new byte[GuidLength];
        for (var dword = 0; dword < GuidDwordCount; dword++)
            BitConverter.TryWriteBytes(guidBytes.AsSpan(dword * sizeof(uint)), executable.Dword(lookup + (ulong)(dword * StoreLength + StoreImmediateOffset)));
        return new CharacterFixedEffects(new Guid(guidBytes));
    }
}
