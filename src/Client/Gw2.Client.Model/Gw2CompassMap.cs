using System.Numerics;

namespace Gw2.Client.Model;

public enum Gw2MapFloorPagesKind
{
    Terrain,
    Overlay,
}

public sealed record Gw2MapFloorPages(uint PagedImageFileId, Gw2MapFloorPagesKind Kind, int DownscaleShift, int Width, int Height);

public sealed record Gw2CompassMapFloor(int FloorId, int TextureWidth, int TextureHeight, uint? FallbackTextureFileId, IReadOnlyList<Gw2MapFloorPages> Pages)
{
    public const int ChunkTextureResolution = 512;

    public Gw2MapFloorPages? PagesAt(Gw2MapFloorPagesKind kind, int downscaleShift) =>
        Pages.FirstOrDefault(pages => pages.Kind == kind && pages.DownscaleShift == downscaleShift);
}

public sealed record Gw2CompassMapChunk(uint PagedImageFileId, Gw2MapFloorPagesKind Kind, int DownscaleShift, int Column, int Row, Gw2ContinentRect ContinentArea);

public sealed record Gw2CompassMapCell(Gw2ContinentRect ContinentArea, Gw2CompassMapChunk? Terrain, Gw2CompassMapChunk? Overlay);

public sealed record Gw2CompassMapPlacement(Gw2ContinentRect ContinentRect, Gw2ContinentRect MapRect, Gw2CompassMapFloor Floor)
{
    public Vector2 ContinentPosition(Vector2 mapPosition)
    {
        var fraction = (mapPosition - MapRect.Minimum) / MapRect.Size;
        return new(
            ContinentRect.Minimum.X + fraction.X * ContinentRect.Size.X,
            ContinentRect.Maximum.Y - fraction.Y * ContinentRect.Size.Y);
    }
}

public static class Gw2CompassMap
{
    public const float InchesPerMeter = 1f / 0.0254f;
    private const int BackgroundDownscaleShift = 5;
    private const int HighestZoomLevel = 3;
    private static readonly (int OverlayShift, int TerrainShift)[] DownscaleShiftsByZoomLevel = [(0, 1), (1, 1), (2, 2), (3, 3)];

    public static int ZoomLevel(float mapZoom) => mapZoom <= 0f ? 0 : Math.Min((int)MathF.Floor(mapZoom), HighestZoomLevel);

    public static IEnumerable<Gw2CompassMapCell> VisibleCells(Gw2CompassMapFloor floor, Gw2ContinentRect view, float mapZoom)
    {
        ArgumentNullException.ThrowIfNull(floor);
        var (overlayShift, terrainShift) = DownscaleShiftsByZoomLevel[ZoomLevel(mapZoom)];
        foreach (var cell in CellsOf(floor, BackgroundDownscaleShift, BackgroundDownscaleShift, view)) yield return cell;
        foreach (var cell in CellsOf(floor, terrainShift, overlayShift, view)) yield return cell;
    }

    private static IEnumerable<Gw2CompassMapCell> CellsOf(Gw2CompassMapFloor floor, int terrainShift, int overlayShift, Gw2ContinentRect view)
    {
        var terrain = floor.PagesAt(Gw2MapFloorPagesKind.Terrain, terrainShift);
        var overlay = floor.PagesAt(Gw2MapFloorPagesKind.Overlay, overlayShift);
        var grid = terrain is null || (overlay is not null && overlay.DownscaleShift < terrain.DownscaleShift) ? overlay : terrain;
        if (grid is null) yield break;
        foreach (var cell in ChunksOf(grid, view))
            yield return new Gw2CompassMapCell(cell.ContinentArea, ChunkCovering(terrain, cell.ContinentArea.Center), ChunkCovering(overlay, cell.ContinentArea.Center));
    }

    private static Gw2CompassMapChunk? ChunkCovering(Gw2MapFloorPages? pages, Vector2 continentPosition)
    {
        if (pages is null) return null;
        var chunkSize = (float)(Gw2CompassMapFloor.ChunkTextureResolution << pages.DownscaleShift);
        var column = (int)MathF.Floor(continentPosition.X / chunkSize);
        var row = (int)MathF.Floor(continentPosition.Y / chunkSize);
        if (column < 0 || row < 0 || column >= pages.Width / Gw2CompassMapFloor.ChunkTextureResolution || row >= pages.Height / Gw2CompassMapFloor.ChunkTextureResolution) return null;
        var minimum = new Vector2(column, row) * chunkSize;
        return new Gw2CompassMapChunk(pages.PagedImageFileId, pages.Kind, pages.DownscaleShift, column, row, new Gw2ContinentRect(minimum, minimum + new Vector2(chunkSize)));
    }

    private static IEnumerable<Gw2CompassMapChunk> ChunksOf(Gw2MapFloorPages pages, Gw2ContinentRect view)
    {
        var chunkSize = (float)(Gw2CompassMapFloor.ChunkTextureResolution << pages.DownscaleShift);
        var columns = pages.Width / Gw2CompassMapFloor.ChunkTextureResolution;
        var rows = pages.Height / Gw2CompassMapFloor.ChunkTextureResolution;
        var firstColumn = Math.Max(0, (int)MathF.Floor(view.Minimum.X / chunkSize));
        var lastColumn = Math.Min(columns - 1, (int)MathF.Ceiling(view.Maximum.X / chunkSize) - 1);
        var firstRow = Math.Max(0, (int)MathF.Floor(view.Minimum.Y / chunkSize));
        var lastRow = Math.Min(rows - 1, (int)MathF.Ceiling(view.Maximum.Y / chunkSize) - 1);
        for (var row = firstRow; row <= lastRow; row++)
            for (var column = firstColumn; column <= lastColumn; column++)
            {
                var minimum = new Vector2(column, row) * chunkSize;
                yield return new Gw2CompassMapChunk(pages.PagedImageFileId, pages.Kind, pages.DownscaleShift, column, row, new Gw2ContinentRect(minimum, minimum + new Vector2(chunkSize)));
            }
    }
}
