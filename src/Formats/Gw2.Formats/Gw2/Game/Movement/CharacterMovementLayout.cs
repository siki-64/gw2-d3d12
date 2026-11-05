namespace Gw2.Formats;

public sealed record CharacterMovementLayout(float DefaultRunSpeedInGameUnits)
{
    private static readonly CodeAnchor RunSpeedTunable = CodeAnchor.Reference(
        "E8 ?? ?? ?? ?? F3 0F 10 0D ?? ?? ?? ?? 48 8D 0D ?? ?? ?? ?? C7 05 ?? ?? ?? ?? 32 01 00 00 C7 05 ?? ?? ?? ?? B5 01 00 00", 9, 13);

    private static CharacterMovementLayout? _current;

    public static CharacterMovementLayout Current => _current ??= FromExecutable(ExecutableImage.Current);

    public static CharacterMovementLayout FromExecutable(string executablePath) => FromExecutable(ExecutableImage.Open(executablePath));

    internal static CharacterMovementLayout FromExecutable(ExecutableImage executable) =>
        new(executable.Single(RunSpeedTunable.Locate(executable)));
}
