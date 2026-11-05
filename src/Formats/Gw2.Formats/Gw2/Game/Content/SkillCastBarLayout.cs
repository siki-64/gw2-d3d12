namespace Gw2.Formats;

public sealed record SkillCastBarLayout(int AbilitySequenceTokenOffset, int AbilityVariantTokenOffset, int AbilityFlagsOffset, uint HidesCastBarAbilityFlag, int AbilityLoopStepCountsOffset)
{
    private const int AbilitySequenceTokenReadHasNoDisplacement = 0;

    private static readonly CodeAnchor ActivationStartAbilityFlagsTest = CodeAnchor.Site(
        "33 DB 8B CB 39 48 ?? 48 0F 44 48 ?? F7 41 ?? 00 00 00 10 0F 85", 12);

    private static readonly CodeAnchor SkillTimingSequenceRead = CodeAnchor.Site(
        "4C 8B 43 ?? 85 ED 74 ?? 48 8B 53 ?? 48 85 D2 75 ?? 48 8B 13 48 8B C8 41 FF D1", 0);

    private static readonly CodeAnchor SkillTimingLoopStepCountsRead = CodeAnchor.Site(
        "74 09 48 8B 9B ?? 00 00 00 EB 07 48 8B 9B ?? 00 00 00 48 8B 57 ??", 11);

    private static SkillCastBarLayout? _current;

    public static SkillCastBarLayout Current => _current ??= FromExecutable(ExecutableImage.Current);

    public static SkillCastBarLayout FromExecutable(string executablePath) => FromExecutable(ExecutableImage.Open(executablePath));

    internal static SkillCastBarLayout FromExecutable(ExecutableImage executable)
    {
        ulong flagsTest = ActivationStartAbilityFlagsTest.Locate(executable);
        ulong sequenceRead = SkillTimingSequenceRead.Locate(executable);
        ulong loopStepCountsRead = SkillTimingLoopStepCountsRead.Locate(executable);
        return new SkillCastBarLayout(
            AbilitySequenceTokenOffset: AbilitySequenceTokenReadHasNoDisplacement,
            AbilityVariantTokenOffset: executable.Bytes(sequenceRead + 3, 1)[0],
            AbilityFlagsOffset: executable.Bytes(flagsTest + 2, 1)[0],
            HidesCastBarAbilityFlag: executable.Dword(flagsTest + 3),
            AbilityLoopStepCountsOffset: (int)executable.Dword(loopStepCountsRead + 3));
    }
}
