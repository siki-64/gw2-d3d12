using System.Numerics;

namespace Gw2.Client.Model;

public readonly record struct Gw2ContinentRect(Vector2 Minimum, Vector2 Maximum)
{
    public Vector2 Size => Maximum - Minimum;
    public Vector2 Center => (Minimum + Maximum) * 0.5f;
}

public static class Gw2CompassView
{
    public const float MinimumCompassZoom = 0f;
    public const float MaximumCompassZoom = 3f;
    public const float MapZoomPerCompassZoom = 0.375f;
    public const float MaximumMapZoom = 1.125f;
    public const float FullyZoomedInMapZoom = -0.25f;
    private const float MaximumZoom = 3f;
    public const float LargestScaledViewport = 32768f;
    public static readonly float[] ContinentPixelsPerViewportPixel = [1f, 3f, 12f, 20f];

    public static float MapZoom(float compassZoom, bool fullyZoomedInWhenAtMinimum)
    {
        var clampedCompassZoom = Math.Clamp(compassZoom, MinimumCompassZoom, MaximumCompassZoom);
        if (clampedCompassZoom == MinimumCompassZoom && fullyZoomedInWhenAtMinimum) return FullyZoomedInMapZoom;
        return Math.Clamp(clampedCompassZoom * MapZoomPerCompassZoom, 0f, MaximumMapZoom);
    }

    public static float ContinentPixelsPerViewportPixelAt(float mapZoom, (int Width, int Height) viewport) =>
        HalfWidthInContinentPixels(mapZoom, viewport) / (viewport.Width * 0.5f);

    public static float ScaleAtZoomLevel(int zoomLevel, (int Width, int Height) viewport)
    {
        var scale = ContinentPixelsPerViewportPixel[zoomLevel];
        var largest = ContinentPixelsPerViewportPixel[^1];
        var scaledViewport = Math.Max(viewport.Width, viewport.Height) * largest;
        if (scaledViewport <= LargestScaledViewport) return scale;
        return scale * (LargestScaledViewport / scaledViewport);
    }

    public static float HalfWidthInContinentPixels(float zoom, (int Width, int Height) viewport)
    {
        int lower, upper;
        float fraction;
        if (zoom < 0f)
        {
            (lower, upper, fraction) = (0, 1, zoom);
        }
        else if (zoom < MaximumZoom)
        {
            lower = (int)MathF.Floor(zoom);
            upper = Math.Min(lower + 1, 3);
            fraction = zoom - MathF.Floor(zoom);
        }
        else
        {
            (lower, upper, fraction) = (2, 3, zoom - 2f);
        }

        var lowerScale = ScaleAtZoomLevel(lower, viewport);
        var upperScale = ScaleAtZoomLevel(upper, viewport);
        return Math.Max(0f, viewport.Width * ((upperScale - lowerScale) * fraction + lowerScale) * 0.5f);
    }

    public static Gw2ContinentRect VisibleRect(Vector2 center, float zoom, (int Width, int Height) viewport)
    {
        var aspect = viewport.Height != 0 ? (float)viewport.Width / viewport.Height : 1f;
        var halfWidth = HalfWidthInContinentPixels(zoom, viewport);
        var halfExtent = viewport.Width >= viewport.Height
            ? new Vector2(halfWidth, halfWidth / aspect)
            : new Vector2(halfWidth * aspect, halfWidth);
        return new(center - halfExtent, center + halfExtent);
    }
}
