using System;
using System.Collections.Generic;
using System.Linq;
using Gw2.Dat.Content;

namespace Gw2.Client.Scene;

public readonly record struct Gw2CompositePass(Gw2RgbaImage? Mask, Gw2ColorShift? Shift);

public readonly record struct Gw2AtlasRect(int X0, int Y0, int X1, int Y1)
{
    public int Width => X1 - X0;

    public int Height => Y1 - Y0;
}

public static class Gw2CompositeAtlas
{
    private const int RegionCount = 16;
    private const uint PartHidesChestOverlap = 0x4;
    private const uint PartHidesLegsOverlap = 0x20;
    private const uint PartHidesHairEars = 0x10000;

    private static readonly int[] HideFlagBitOfRegion = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 16, 11, 12, 13, 14];

    public static bool HidesRegion(uint fileDataHideFlags, int region) =>
        (fileDataHideFlags & (1u << HideFlagBitOfRegion[region])) != 0;

    public static IReadOnlyList<int> Regions(uint partType, uint partHideFlags, uint fileDataHideFlags, bool hasMeshOverlap)
    {
        var regions = new List<int>();
        switch (partType)
        {
            case 0: regions.Add(12); break;
            case 1: regions.Add(13); break;
            case 2: regions.Add(14); break;
            case 3: regions.Add(15); break;
            case 4: regions.Add(11); regions.Add(9); break;
            case 5: regions.Add(9); break;
            case 6:
                regions.Add(10);
                regions.Add(9);
                if ((partHideFlags & PartHidesHairEars) == 0) regions.Add(11);
                break;
            case 7: regions.Add(11); break;
            case 8:
                regions.Add(3);
                if ((partHideFlags & PartHidesChestOverlap) == 0 && hasMeshOverlap) regions.Add(2);
                break;
            case 9: regions.Add(0); break;
            case 10:
                regions.Add(4);
                if ((partHideFlags & PartHidesLegsOverlap) == 0 && hasMeshOverlap) regions.Add(5);
                break;
            case 11: regions.Add(6); break;
            case 12: regions.Add(1); break;
            case 13: regions.Add(7); break;
            case 14: regions.Add(8); break;
            default: return regions;
        }

        for (var region = 0; region < RegionCount; region++)
            if ((fileDataHideFlags & (1u << HideFlagBitOfRegion[region])) != 0) regions.Add(region);
        return regions;
    }

    public static IReadOnlyList<Gw2AtlasRect> Rects(Gw2CompositeBlitRectSet set, int region, bool halfResolution)
    {
        ArgumentNullException.ThrowIfNull(set);
        var shift = halfResolution ? 1 : 0;
        var rects = new List<Gw2AtlasRect>();
        for (int rect = set.RectIndices[region]; rect < set.RectIndices[region + 1]; rect++)
        {
            var values = set.Rects[rect];
            rects.Add(new((int)values[0] >> shift, (int)values[1] >> shift, (int)values[2] >> shift, (int)values[3] >> shift));
        }

        return rects;
    }

    public static IReadOnlyList<Gw2AtlasRect> PartRects(Gw2CompositeBlitRectSet set, Gw2CompositeFileData part, uint partHideFlags, bool halfResolution)
    {
        ArgumentNullException.ThrowIfNull(part);
        var hasMeshOverlap = part.MeshOverlap.TryGetFileId(out var overlapFileId) && overlapFileId != 0;
        return Regions(part.Type, partHideFlags, part.HideFlags, hasMeshOverlap)
            .SelectMany(region => Rects(set, region, halfResolution))
            .ToList();
    }

    public static void Blit(Gw2RgbaImage atlas, IReadOnlyList<Gw2AtlasRect> rects, Gw2RgbaImage source) =>
        Blit(atlas, rects, source, null);

    public static void Blit(Gw2RgbaImage atlas, IReadOnlyList<Gw2AtlasRect> rects, Gw2RgbaImage source, Gw2ColorShift? colorShift)
    {
        ArgumentNullException.ThrowIfNull(atlas);
        ArgumentNullException.ThrowIfNull(rects);
        ArgumentNullException.ThrowIfNull(source);
        if (rects.Count == 0) return;

        var boundsX = rects.Min(rect => rect.X0);
        var boundsY = rects.Min(rect => rect.Y0);
        var shiftMatrix = colorShift?.ToMatrix();
        foreach (var rect in rects)
            for (var y = Math.Max(rect.Y0, 0); y < Math.Min(rect.Y1, atlas.Height); y++)
            for (var x = Math.Max(rect.X0, 0); x < Math.Min(rect.X1, atlas.Width); x++)
            {
                var sourceX = x - boundsX;
                var sourceY = y - boundsY;
                if (sourceX < source.Width && sourceY < source.Height) atlas.SetPixel(x, y, Shift(source.GetPixel(sourceX, sourceY), shiftMatrix));
            }
    }

    public static void Compose(Gw2RgbaImage atlas, IReadOnlyList<Gw2AtlasRect> rects, Gw2RgbaImage source, IReadOnlyList<Gw2CompositePass> passes)
    {
        ArgumentNullException.ThrowIfNull(atlas);
        ArgumentNullException.ThrowIfNull(rects);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(passes);
        if (rects.Count == 0 || passes.Count == 0) return;

        var boundsX = rects.Min(rect => rect.X0);
        var boundsY = rects.Min(rect => rect.Y0);
        var shiftMatrices = passes.Select(pass => pass.Shift?.ToMatrix()).ToArray();
        foreach (var rect in rects)
            for (var y = Math.Max(rect.Y0, 0); y < Math.Min(rect.Y1, atlas.Height); y++)
            for (var x = Math.Max(rect.X0, 0); x < Math.Min(rect.X1, atlas.Width); x++)
            {
                var sourceX = x - boundsX;
                var sourceY = y - boundsY;
                if (sourceX >= source.Width || sourceY >= source.Height) continue;

                var sourceTexel = source.GetPixel(sourceX, sourceY);
                var composed = Shift(sourceTexel, shiftMatrices[0]);
                for (var passIndex = 1; passIndex < passes.Count; passIndex++)
                {
                    if (passes[passIndex].Mask is not { } mask) continue;
                    var coverage = MaskCoverage(mask, source, sourceX, sourceY);
                    if (coverage == 0) continue;
                    composed = Lerp(composed, Shift(sourceTexel, shiftMatrices[passIndex]), coverage);
                }

                atlas.SetPixel(x, y, (composed & 0x00FFFFFF) | (sourceTexel & 0xFF000000));
            }
    }

    public static void ComposeOver(Gw2RgbaImage atlas, IReadOnlyList<Gw2AtlasRect> rects, Gw2RgbaImage source, IReadOnlyList<Gw2CompositePass> passes)
    {
        ArgumentNullException.ThrowIfNull(atlas);
        ArgumentNullException.ThrowIfNull(rects);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(passes);
        if (rects.Count == 0 || passes.Count == 0) return;

        var boundsX = rects.Min(rect => rect.X0);
        var boundsY = rects.Min(rect => rect.Y0);
        var shiftMatrices = passes.Select(pass => pass.Shift?.ToMatrix()).ToArray();
        foreach (var rect in rects)
            for (var y = Math.Max(rect.Y0, 0); y < Math.Min(rect.Y1, atlas.Height); y++)
            for (var x = Math.Max(rect.X0, 0); x < Math.Min(rect.X1, atlas.Width); x++)
            {
                var sourceX = x - boundsX;
                var sourceY = y - boundsY;
                if (sourceX >= source.Width || sourceY >= source.Height) continue;

                var sourceTexel = source.GetPixel(sourceX, sourceY);
                var underlying = atlas.GetPixel(x, y);
                var composed = underlying;
                for (var passIndex = 0; passIndex < passes.Count; passIndex++)
                {
                    var coverage = passes[passIndex].Mask is { } mask ? MaskCoverage(mask, source, sourceX, sourceY) : 0;
                    if (coverage == 0) continue;
                    composed = Lerp(composed, Shift(sourceTexel, shiftMatrices[passIndex]), coverage);
                }

                atlas.SetPixel(x, y, (composed & 0x00FFFFFF) | Math.Max(underlying & 0xFF000000, sourceTexel & 0xFF000000));
            }
    }

    private static int MaskCoverage(Gw2RgbaImage mask, Gw2RgbaImage source, int sourceX, int sourceY)
    {
        var maskX = sourceX * mask.Width / source.Width;
        var maskY = sourceY * mask.Height / source.Height;
        return (int)(mask.GetPixel(maskX, maskY) & 0xFF);
    }

    private static uint Lerp(uint from, uint to, int coverage)
    {
        var result = 0u;
        for (var shift = 0; shift < 24; shift += 8)
        {
            var fromChannel = (int)((from >> shift) & 0xFF);
            var toChannel = (int)((to >> shift) & 0xFF);
            result |= (uint)((fromChannel * (255 - coverage) + toChannel * coverage + 127) / 255) << shift;
        }

        return result;
    }

    private static uint Shift(uint rgba, System.Numerics.Matrix4x4? shiftMatrix) =>
        shiftMatrix is { } matrix ? Gw2ColorShift.Apply(matrix, rgba) : rgba;
}
