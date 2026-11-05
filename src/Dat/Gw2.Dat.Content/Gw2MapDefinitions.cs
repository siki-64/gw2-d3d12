using Gw2.Dat.Archive;
using Gw2.Formats;

namespace Gw2.Dat.Content;

public readonly record struct Gw2MapDefinition(uint MapId, uint MapFileBaseId, bool FreezesTimeOfDay, Gw2DayNightCycle? DayNightCycle, uint? LoadScreenImageFileId);

public readonly record struct Gw2DayNightCycle(uint NightEndHour, uint DayStartHour, uint DayEndHour, uint NightStartHour);

public sealed class Gw2MapDefinitions
{
    public const uint MapContentType = 0x2D;

    private readonly Gw2ContentIndex _index;
    private readonly MapContentLayout _layout;
    private readonly int _mapIdOffset;

    public Gw2MapDefinitions(Gw2ContentIndex index, MapContentLayout layout)
    {
        _index = index;
        _layout = layout;
        _mapIdOffset = index.FieldOffsetsOf(MapContentType).DataIdOffset;
    }

    public Gw2MapDefinition? Find(uint mapId) =>
        _index.FindTyped(MapContentType, mapId) is { } map ? Read(map) : null;

    public IEnumerable<Gw2MapDefinition> OfMapFile(uint mapFileBaseId)
    {
        foreach (var map in _index.All(MapContentType))
            if (MapFileBaseIdOf(map) == mapFileBaseId) yield return Read(map);
    }

    public Gw2DayNightCycle? DefaultDayNightCycle =>
        _index.FindByContentKey(_layout.DayNightCycleContentType, _layout.DefaultDayNightCycleContentKey) is { } cycle ? ReadCycle(cycle) : null;

    public (Gw2DayNightCycle? Cycle, bool FreezesClock) PickDayNightCycle(uint mapId)
    {
        var map = Find(mapId);
        return (map?.DayNightCycle ?? DefaultDayNightCycle, map?.FreezesTimeOfDay ?? false);
    }

    private Gw2MapDefinition Read(Gw2ContentLocation map) => new(
        _index.ReadUInt32(map, _mapIdOffset),
        MapFileBaseIdOf(map),
        (_index.ReadUInt32(map, _layout.FlagsOffset) & _layout.FreezesTimeOfDayFlag) != 0,
        _index.ReadPointer(map, _layout.DayNightCycleOffset) is { } cycle ? ReadCycle(cycle) : null,
        _index.ReadFileId(map, _layout.LoadScreenImageOffset));

    private uint MapFileBaseIdOf(Gw2ContentLocation map) => _index.ReadFileId(map, _layout.MapFileOffset) ?? 0;

    private Gw2DayNightCycle ReadCycle(Gw2ContentLocation cycle) => new(
        _index.ReadUInt32(cycle, _layout.NightEndHourOffset),
        _index.ReadUInt32(cycle, _layout.DayStartHourOffset),
        _index.ReadUInt32(cycle, _layout.DayEndHourOffset),
        _index.ReadUInt32(cycle, _layout.NightStartHourOffset));
}
