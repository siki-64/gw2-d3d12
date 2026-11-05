namespace Gw2.Formats;

public sealed record MapLayoutFloorPagesOffsets(
    int OverlayShift0,
    int TerrainShift1,
    int OverlayShift1,
    int TerrainShift2,
    int OverlayShift2,
    int TerrainShift3,
    int OverlayShift3,
    int TerrainShift5,
    int OverlayShift5);

public sealed record MapLayoutContentLayout(
    int ContinentFloorsOffset,
    int FloorDataFloorOffset,
    int FloorRegionsOffset,
    int RegionMapsOffset,
    int MapLayoutDataOffset,
    int MapLayoutDataMapOffset,
    int MapLayoutContinentRectOffset,
    int MapLayoutMapRectOffset,
    int DataFloorIdOffset,
    int DataFloorTextureWidthOffset,
    int DataFloorTextureHeightOffset,
    int DataFloorFallbackTextureOffset,
    MapLayoutFloorPagesOffsets DataFloorPages,
    int MapDefaultFloorIdOffset)
{
    private static readonly CodeAnchor ContinentFloors = CodeAnchor.Site("48 8B 47 ?? 48 8B 3C 03 48 89 7C 24 58 48 85 FF 75 ?? 48 8D 15", 0);
    private static readonly CodeAnchor FloorRegions = CodeAnchor.Site("48 8B 47 ?? 48 8B 3C 03 48 89 7C 24 30 48 85 FF 75 ?? 48 8D 15", 0);
    private static readonly CodeAnchor RegionMaps = CodeAnchor.Site("48 8B 47 ?? 48 8B 2C 03 48 85 ED 74 ?? 48 83 7D ?? 00 75 ?? 48 8D 15", 0);
    private static readonly CodeAnchor MapLayoutDataMap = CodeAnchor.Site("48 8B 45 ?? 4C 8B 78 ?? 4D 85 FF 0F 84", 0);
    private static readonly CodeAnchor MapLayoutRects = CodeAnchor.Site("8B 8B ?? ?? ?? ?? 89 0E F3 0F 11 46 04 F3 0F 11 4E 08 F3 0F 11 56 0C 33 F6 F3 0F 10 83 ?? ?? ?? ?? 8B C6", 0);
    private static readonly CodeAnchor DefaultFloor = CodeAnchor.Site("8B 4B 50 4C 8D 0D ?? ?? ?? ?? 8B 87 ?? ?? ?? ?? 4C 8D 44 24 48 89 43 50", 0);
    private static readonly CodeAnchor DataFloorFields = CodeAnchor.Site(
        "48 8B 41 ?? 4C 8B E9 48 8B FA 49 8B D8 44 8B 50 ?? 8B 50 ?? 44 89 55 60 89 55 64 48 8B 45 60 48 89 47 10 " +
        "48 8B 41 28 8B 48 ?? 89 4F 60 49 8B 45 28 48 8B 88 ?? ?? ?? ?? 48 89 4F 48 49 8B 45 28", 0);
    private static readonly CodeAnchor DataFloorPageFields = CodeAnchor.Site(
        "49 8B 4D 28 4C 8B C0 48 8B 51 ?? 48 89 50 10 49 8B 4D 28 48 8B 51 ?? 48 89 10 BA 80 00 00 00 8B 4F 10 C1 E9 05 89 4D 60 " +
        "8B 4F 14 C1 E9 05 89 4D 64 48 8B CB 48 8B 45 60 49 89 40 18 8B 47 10 C1 E8 05 89 45 60 8B 47 14 C1 E8 05 89 45 64 " +
        "48 8B 45 60 49 89 40 08 4C 89 47 40 E8 ?? ?? ?? ?? 49 8B 4D 28 4C 8B C0 48 8B 91 ?? ?? ?? ?? 48 89 50 10 49 8B 4D 28 " +
        "48 8B 91 ?? ?? ?? ?? 48 89 10 8B 4F 10 D1 E9 89 4D 60 8B 4F 14 D1 E9 89 4D 64 48 8B 4D 60 48 89 48 18 48 8B 47 10 " +
        "49 89 40 08 49 8B 45 28 48 8B 88 ?? ?? ?? ?? 49 89 48 30 49 8B 45 28 48 8B 88 ?? ?? ?? ?? 49 89 48 20 8B 47 10 D1 E8 " +
        "89 45 60 8B 47 14 D1 E8 89 45 64 48 8B 45 60 49 89 40 38 8B 47 10 D1 E8 89 45 60 8B 47 14 D1 E8 89 45 64 48 8B 45 60 " +
        "49 89 40 28 49 8B 45 28 48 8B 88 ?? ?? ?? ?? 49 89 48 50 49 8B 45 28 48 8B 88 ?? ?? ?? ?? 49 89 48 40 8B 47 10 C1 E8 02 " +
        "89 45 60 8B 47 14 C1 E8 02 89 45 64 48 8B 45 60 49 89 40 58 8B 47 10 C1 E8 02 89 45 60 8B 47 14 C1 E8 02 89 45 64 " +
        "48 8B 45 60 49 89 40 48 49 8B 45 28 48 8B 88 ?? ?? ?? ?? 49 89 48 70 49 8B 45 28 48 8B 88 ?? ?? ?? ?? 49 89 48 60", 0);

    private static MapLayoutContentLayout? _current;

    public static MapLayoutContentLayout Current => _current ??= FromExecutable(ExecutableImage.Current);

    public static MapLayoutContentLayout FromExecutable(string executablePath) => FromExecutable(ExecutableImage.Open(executablePath));

    internal static MapLayoutContentLayout FromExecutable(ExecutableImage executable)
    {
        ulong regionMaps = RegionMaps.Locate(executable);
        ulong fields = DataFloorFields.Locate(executable);
        ulong pages = DataFloorPageFields.Locate(executable);
        ulong rects = MapLayoutRects.Locate(executable);
        return new MapLayoutContentLayout(
            ContinentFloorsOffset: Byte(executable, ContinentFloors.Locate(executable) + 3),
            FloorDataFloorOffset: Byte(executable, fields + 3),
            FloorRegionsOffset: Byte(executable, FloorRegions.Locate(executable) + 3),
            RegionMapsOffset: Byte(executable, regionMaps + 3),
            MapLayoutDataOffset: Byte(executable, regionMaps + 16),
            MapLayoutDataMapOffset: Byte(executable, MapLayoutDataMap.Locate(executable) + 7),
            MapLayoutContinentRectOffset: Dword(executable, rects + 29),
            MapLayoutMapRectOffset: Dword(executable, rects + 2),
            DataFloorIdOffset: Byte(executable, fields + 41),
            DataFloorTextureWidthOffset: Byte(executable, fields + 16),
            DataFloorTextureHeightOffset: Byte(executable, fields + 19),
            DataFloorFallbackTextureOffset: Dword(executable, fields + 52),
            DataFloorPages: new MapLayoutFloorPagesOffsets(
                OverlayShift0: Dword(executable, pages + 120),
                TerrainShift1: Dword(executable, pages + 166),
                OverlayShift1: Dword(executable, pages + 181),
                TerrainShift2: Dword(executable, pages + 244),
                OverlayShift2: Dword(executable, pages + 259),
                TerrainShift3: Dword(executable, pages + 326),
                OverlayShift3: Dword(executable, pages + 341),
                TerrainShift5: Byte(executable, pages + 10),
                OverlayShift5: Byte(executable, pages + 22)),
            MapDefaultFloorIdOffset: Dword(executable, DefaultFloor.Locate(executable) + 12));
    }

    private static byte Byte(ExecutableImage executable, ulong address) => executable.Bytes(address, 1)[0];

    private static int Dword(ExecutableImage executable, ulong address) => (int)executable.Dword(address);
}
