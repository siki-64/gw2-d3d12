using Gw2.Dat.Archive;
using Gw2.Formats;

namespace Gw2.Dat.Content;

public readonly record struct Gw2MapLayoutRect(float MinimumX, float MinimumY, float MaximumX, float MaximumY);

public readonly record struct Gw2DataFloorPages(uint PagedImageFileId, bool IsOverlay, int DownscaleShift);

public sealed record Gw2DataFloor(int FloorId, int TextureWidth, int TextureHeight, uint? FallbackTextureFileId, IReadOnlyList<Gw2DataFloorPages> Pages);

public sealed record Gw2MapLayoutPlacement(Gw2MapLayoutRect ContinentRect, Gw2MapLayoutRect MapRect, Gw2DataFloor DataFloor);

public readonly record struct Gw2Waypoint(uint PointOfInterestDataId, float X, float Y, float Z);

public sealed class Gw2MapLayouts
{
    public const uint ContinentContentType = 0x118;
    public const uint DataFloorContentType = 0x113;
    public const uint PointOfInterestContentType = 0x34;
    private const int ArrayCountOffset = 8;
    private const int RectFieldSize = 4;

    private readonly Gw2ContentIndex _index;
    private readonly MapLayoutContentLayout _layout;

    public Gw2MapLayouts(Gw2ContentIndex index, MapLayoutContentLayout layout)
    {
        _index = index;
        _layout = layout;
    }

    public Gw2MapLayoutPlacement? FindPlacement(uint mapId)
    {
        if (_index.FindTyped(Gw2MapDefinitions.MapContentType, mapId) is not { } map) return null;
        int defaultFloorId = _index.ReadInt32(map, _layout.MapDefaultFloorIdOffset);
        Gw2MapLayoutPlacement? firstPlacement = null;
        foreach (var continent in _index.All(ContinentContentType))
            foreach (var floor in Elements(continent, _layout.ContinentFloorsOffset))
            {
                if (_index.ReadPointer(floor, _layout.FloorDataFloorOffset) is not { } dataFloor) continue;
                foreach (var region in Elements(floor, _layout.FloorRegionsOffset))
                    foreach (var mapLayout in Elements(region, _layout.RegionMapsOffset))
                    {
                        if (_index.ReadPointer(mapLayout, _layout.MapLayoutDataOffset) is not { } data) continue;
                        if (_index.ReadPointer(data, _layout.MapLayoutDataMapOffset) != map) continue;
                        var placement = new Gw2MapLayoutPlacement(
                            ReadRect(mapLayout, _layout.MapLayoutContinentRectOffset),
                            ReadRect(mapLayout, _layout.MapLayoutMapRectOffset),
                            ReadDataFloor(dataFloor));
                        if (placement.DataFloor.FloorId == defaultFloorId) return placement;
                        firstPlacement ??= placement;
                    }
            }
        return firstPlacement;
    }

    public IReadOnlyList<Gw2Waypoint> FindWaypoints(uint mapId, PointOfInterestContentLayout pointLayout)
    {
        var waypoints = new List<Gw2Waypoint>();
        var seen = new HashSet<uint>();
        if (_index.FindTyped(Gw2MapDefinitions.MapContentType, mapId) is not { } map) return waypoints;
        foreach (var continent in _index.All(ContinentContentType))
            foreach (var floor in Elements(continent, _layout.ContinentFloorsOffset))
                foreach (var region in Elements(floor, _layout.FloorRegionsOffset))
                    foreach (var mapLayout in Elements(region, _layout.RegionMapsOffset))
                    {
                        if (_index.ReadPointer(mapLayout, _layout.MapLayoutDataOffset) is not { } data) continue;
                        if (_index.ReadPointer(data, _layout.MapLayoutDataMapOffset) != map) continue;
                        foreach (var point in Elements(mapLayout, pointLayout.MapLayoutPointsOfInterestOffset))
                        {
                            if (_index.ReadPointer(point, pointLayout.PointOfInterestDefinitionOffset) is not { } definition) continue;
                            if (_index.ReadUInt32(definition, pointLayout.DefinitionTypeOffset) != PointOfInterestContentLayout.WaypointType) continue;
                            uint dataId = _index.DataIdOf(PointOfInterestContentType, definition);
                            if (!seen.Add(dataId)) continue;
                            int position = pointLayout.PointOfInterestPositionOffset;
                            waypoints.Add(new Gw2Waypoint(dataId,
                                _index.ReadSingle(point, position),
                                _index.ReadSingle(point, position + RectFieldSize),
                                _index.ReadSingle(point, position + 2 * RectFieldSize)));
                        }
                    }
        return waypoints;
    }

    public Gw2DataFloor? FindDataFloor(uint uid) =>
        _index.Find(DataFloorContentType, uid) is { } floor ? ReadDataFloor(floor) : null;

    private IEnumerable<Gw2ContentLocation> Elements(Gw2ContentLocation owner, int arrayOffset)
    {
        if (_index.ReadPointer(owner, arrayOffset) is not { } array) yield break;
        uint count = _index.ReadUInt32(owner, arrayOffset + ArrayCountOffset);
        for (int index = 0; index < count; index++)
            if (_index.ReadPointerArrayElement(array, index) is { } element) yield return element;
    }

    private Gw2MapLayoutRect ReadRect(Gw2ContentLocation owner, int offset) => new(
        _index.ReadSingle(owner, offset),
        _index.ReadSingle(owner, offset + RectFieldSize),
        _index.ReadSingle(owner, offset + 2 * RectFieldSize),
        _index.ReadSingle(owner, offset + 3 * RectFieldSize));

    private Gw2DataFloor ReadDataFloor(Gw2ContentLocation floor)
    {
        var offsets = _layout.DataFloorPages;
        var pages = new List<Gw2DataFloorPages>();
        AddPages(pages, floor, offsets.OverlayShift0, true, 0);
        AddPages(pages, floor, offsets.TerrainShift1, false, 1);
        AddPages(pages, floor, offsets.OverlayShift1, true, 1);
        AddPages(pages, floor, offsets.TerrainShift2, false, 2);
        AddPages(pages, floor, offsets.OverlayShift2, true, 2);
        AddPages(pages, floor, offsets.TerrainShift3, false, 3);
        AddPages(pages, floor, offsets.OverlayShift3, true, 3);
        AddPages(pages, floor, offsets.TerrainShift5, false, 5);
        AddPages(pages, floor, offsets.OverlayShift5, true, 5);
        return new Gw2DataFloor(
            _index.ReadInt32(floor, _layout.DataFloorIdOffset),
            _index.ReadInt32(floor, _layout.DataFloorTextureWidthOffset),
            _index.ReadInt32(floor, _layout.DataFloorTextureHeightOffset),
            FileIdAt(floor, _layout.DataFloorFallbackTextureOffset),
            pages);
    }

    private void AddPages(List<Gw2DataFloorPages> pages, Gw2ContentLocation floor, int offset, bool isOverlay, int downscaleShift)
    {
        if (FileIdAt(floor, offset) is { } fileId) pages.Add(new(fileId, isOverlay, downscaleShift));
    }

    private uint? FileIdAt(Gw2ContentLocation owner, int offset) => _index.ReadFileId(owner, offset) is { } fileId and not 0 ? fileId : null;
}
