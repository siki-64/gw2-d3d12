namespace Gw2.Formats;

public sealed record MapContentLayout(
    int MapFileOffset,
    int FlagsOffset,
    uint FreezesTimeOfDayFlag,
    int DayNightCycleOffset,
    uint DayNightCycleContentType,
    Guid DefaultDayNightCycleContentKey,
    int NightEndHourOffset,
    int DayStartHourOffset,
    int DayEndHourOffset,
    int NightStartHourOffset,
    int LoadScreenImageOffset)
{
    private static readonly CodeAnchor PickCycle = CodeAnchor.Site(
        "48 8B F0 48 8B 98 ?? ?? ?? ?? 48 85 DB 75 ?? E8 ?? ?? ?? ?? 48 8B 88 E0 00 00 00 48 8B 11 " +
        "C7 44 24 20 ?? ?? ?? ?? C7 44 24 24 ?? ?? ?? ?? C7 44 24 28 ?? ?? ?? ?? 4C 8B 42 68 48 8D 54 24 20 " +
        "C7 44 24 2C ?? ?? ?? ?? 41 FF D0 48 8B D8 81 78 10 ?? ?? ?? ?? 74 ?? 41 B8 ?? ?? ?? ?? 48 8D 15 ?? ?? ?? ?? " +
        "48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 89 5F 20 F6 86 ?? ?? ?? ?? ?? 74 04 83 4F 10 01", 0);
    private static readonly CodeAnchor CycleHours = CodeAnchor.Site(
        "8B 43 ?? 48 8D 4D C7 44 8B 63 ?? 44 8B 6B ?? 44 8B 7B ?? C1 E0 10", 0);
    private static readonly CodeAnchor MapFileRead = CodeAnchor.Site(
        "FF 90 A8 01 00 00 48 85 C0 74 ?? F7 80 ?? ?? ?? ?? 00 04 00 00 75 ?? 48 8B 48 ?? 33 DB 48 85 C9 74 ?? 33 D2 E8", 0);

    private static readonly CodeAnchor LoadScreenImage = CodeAnchor.Site(
        "83 B9 ?? ?? ?? ?? 00 48 8B F2 48 8B E9 75 ?? 48 8B 49 ?? 48 89 0A BA 01 00 00 00 E8", 0);

    private static MapContentLayout? _current;

    public static MapContentLayout Current => _current ??= FromExecutable(ExecutableImage.Current);

    public static MapContentLayout FromExecutable(string executablePath) => FromExecutable(ExecutableImage.Open(executablePath));

    internal static MapContentLayout FromExecutable(ExecutableImage executable)
    {
        ulong pickCycle = PickCycle.Locate(executable);
        ulong hours = CycleHours.Locate(executable);
        ulong mapFile = MapFileRead.Locate(executable);
        ulong loadScreenImage = LoadScreenImage.Locate(executable);
        Span<byte> contentKey = stackalloc byte[16];
        for (int part = 0; part < 4; part++)
            BitConverter.TryWriteBytes(contentKey[(part * 4)..], executable.Dword(pickCycle + (ulong)(part < 3 ? 34 + part * 8 : 67)));
        return new MapContentLayout(
            MapFileOffset: Byte(executable, mapFile + 26),
            FlagsOffset: (int)executable.Dword(pickCycle + 117),
            FreezesTimeOfDayFlag: Byte(executable, pickCycle + 121),
            DayNightCycleOffset: (int)executable.Dword(pickCycle + 6),
            DayNightCycleContentType: executable.Dword(pickCycle + 80),
            DefaultDayNightCycleContentKey: new Guid(contentKey),
            NightEndHourOffset: Byte(executable, hours + 18),
            DayStartHourOffset: Byte(executable, hours + 10),
            DayEndHourOffset: Byte(executable, hours + 2),
            NightStartHourOffset: Byte(executable, hours + 14),
            LoadScreenImageOffset: Byte(executable, loadScreenImage + 18));
    }

    private static byte Byte(ExecutableImage executable, ulong address) => executable.Bytes(address, 1)[0];
}
