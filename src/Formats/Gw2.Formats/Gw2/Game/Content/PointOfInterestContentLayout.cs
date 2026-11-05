namespace Gw2.Formats;

public sealed record PointOfInterestContentLayout(
    int MapLayoutPointsOfInterestOffset,
    int MapLayoutPointsOfInterestCountOffset,
    int PointOfInterestDefinitionOffset,
    int PointOfInterestPositionOffset,
    int DefinitionTypeOffset)
{
    public const uint WaypointType = 1;

    private static readonly CodeAnchor PointsOfInterestLoop = CodeAnchor.Site("44 8B 76 ?? 45 85 F6 74 ?? 48 8B DF 0F 1F 80 00 00 00 00 3B 7E ?? 72", 0);
    private static readonly CodeAnchor DefinitionType = CodeAnchor.Site("49 8B 6D ?? 83 7D ?? 00 0F 85 ?? ?? ?? ?? 48 85 ED 75 ?? 41 B8", 0);
    private static readonly CodeAnchor Position = CodeAnchor.Site("F2 0F 10 4B ?? 48 8D 54 24 ?? 44 8B 46 ?? 48 8D 4C 24 ?? 0F 28 C1", 0);

    private static PointOfInterestContentLayout? _current;

    public static PointOfInterestContentLayout Current => _current ??= FromExecutable(ExecutableImage.Current);

    public static PointOfInterestContentLayout FromExecutable(string executablePath) => FromExecutable(ExecutableImage.Open(executablePath));

    internal static PointOfInterestContentLayout FromExecutable(ExecutableImage executable)
    {
        ulong loop = PointsOfInterestLoop.Locate(executable);
        ulong definitionType = DefinitionType.Locate(executable);
        return new PointOfInterestContentLayout(
            MapLayoutPointsOfInterestOffset: Byte(executable, loop + 0x34),
            MapLayoutPointsOfInterestCountOffset: Byte(executable, loop + 3),
            PointOfInterestDefinitionOffset: Byte(executable, definitionType + 3),
            PointOfInterestPositionOffset: Byte(executable, Position.Locate(executable) + 4),
            DefinitionTypeOffset: Byte(executable, definitionType + 6));
    }

    private static byte Byte(ExecutableImage executable, ulong address) => executable.Bytes(address, 1)[0];
}
