using System.Linq;
using System.Numerics;
using Gw2.Client.Model;
using Gw2.Dat.Content;

namespace Gw2.Client.World;

internal static class Gw2CompassPlacementView
{
    internal static Gw2CompassMapPlacement? Find(Gw2MapLayouts layouts, uint mapId) =>
        layouts.FindPlacement(mapId) is { } placement
            ? new Gw2CompassMapPlacement(RectOf(placement.ContinentRect), RectOf(placement.MapRect), FloorOf(placement.DataFloor))
            : null;

    internal static Gw2CompassMapFloor? FindDataFloor(Gw2MapLayouts layouts, uint uid) =>
        layouts.FindDataFloor(uid) is { } floor ? FloorOf(floor) : null;

    internal static Gw2CompassMapFloor FloorOf(Gw2DataFloor floor)
    {
        var pages = floor.Pages
            .Select(pages => new Gw2MapFloorPages(pages.PagedImageFileId, pages.IsOverlay ? Gw2MapFloorPagesKind.Overlay : Gw2MapFloorPagesKind.Terrain,
                pages.DownscaleShift, floor.TextureWidth >> pages.DownscaleShift, floor.TextureHeight >> pages.DownscaleShift))
            .ToArray();
        return new Gw2CompassMapFloor(floor.FloorId, floor.TextureWidth, floor.TextureHeight, floor.FallbackTextureFileId, pages);
    }

    private static Gw2ContinentRect RectOf(Gw2MapLayoutRect rect) =>
        new(new Vector2(rect.MinimumX, rect.MinimumY), new Vector2(rect.MaximumX, rect.MaximumY));
}
