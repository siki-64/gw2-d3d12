using System.Numerics;
using Gw2.Dat.Content;

namespace Gw2.Client.Model;

[Flags]
public enum Gw2TextLayoutFlags : uint
{
    None = 0,
    WidenedSpacing = 0x1,
    Mnemonics = 0x2,
    Inverted = 0x4,
    Halo = 0x8,
    ShadowDownRight = 0x10,
    ShadowUpLeft = 0x20,
    Glow = 0x40,
    StrikeThrough = 0x80,
    Underline = 0x100
}

public sealed record Gw2TextImage(int Width, int Height, byte[] Rgba);

public sealed record Gw2TextRasterResult(int TextWidth, int BandWidth, int BandHeight, int SegmentCount, IReadOnlyList<Gw2TextImage> Images);

public static class Gw2TextRaster
{
    public const int PageSize = 256;
    private const int MinimumBandEdge = 4;
    private const int SegmentMargin = 1;
    private const int MaximumAspectLog = 3;
    private const char MnemonicMarker = '_';
    private const uint EffectMask = 0x7D;
    private const int PaletteBlockSize = 256;
    private const int EffectLevels = 16;
    private const int HaloDivisor = 2;
    private const int GlowRadius = 2;
    private const int GlowDivisor = 12;

    private static readonly uint[] RegisteredEffects = [0, 0x1, 0x4, 0x8, 0x10, 0x20, 0x40];
    private static readonly Gw2TextLayoutFlags[] ExclusiveEffectsByPriority = [Gw2TextLayoutFlags.WidenedSpacing, Gw2TextLayoutFlags.Halo, Gw2TextLayoutFlags.Glow, Gw2TextLayoutFlags.ShadowDownRight];
    private const Gw2TextLayoutFlags ExclusiveEffects = Gw2TextLayoutFlags.WidenedSpacing | Gw2TextLayoutFlags.Halo | Gw2TextLayoutFlags.ShadowDownRight | Gw2TextLayoutFlags.Glow;

    public static Gw2TextRasterResult Rasterize(Gw2FontFace face, string text, Gw2TextLayoutFlags flags, uint argb)
    {
        ArgumentNullException.ThrowIfNull(face);
        ArgumentNullException.ThrowIfNull(text);
        var textWidth = MeasureWidth(face, text, flags);
        var clipRight = textWidth;
        var clipBottom = face.CellHeight;
        var spanWidth = clipRight + 2;
        var bandWidth = Math.Max(MinimumBandEdge, (int)Math.Min(PageSize, BitOperations.RoundUpToPowerOf2((uint)spanWidth)));
        var bandHeight = Math.Max(MinimumBandEdge, (int)Math.Min(PageSize, BitOperations.RoundUpToPowerOf2((uint)(clipBottom + 1))));
        var segmentCount = (spanWidth + PageSize - 3) / (PageSize - 1);
        var bandsPerImage = PageSize / bandHeight;
        var imageCount = (bandsPerImage + segmentCount - 1) / bandsPerImage;
        var lastImageBands = (bandsPerImage + segmentCount - 1) % bandsPerImage + 1;

        var palette = BuildPalette(argb);
        var effect = (uint)flags & EffectMask;
        if (Array.IndexOf(RegisteredEffects, effect) < 0) effect = 0;

        var images = new List<Gw2TextImage>(imageCount);
        for (var imageIndex = 0; imageIndex < imageCount; imageIndex++)
        {
            var remainingSpan = spanWidth - (PageSize - 1) * (segmentCount - 1);
            var (imageWidth, imageHeight) = imageIndex + 1 < imageCount
                ? (PageSize, PageSize)
                : SingleImageSize(remainingSpan, bandWidth, bandHeight, lastImageBands);
            images.Add(new Gw2TextImage(imageWidth, imageHeight, new byte[imageWidth * imageHeight * 4]));
        }

        var coverage = new byte[bandWidth * bandHeight];
        for (var segment = 0; segment < segmentCount; segment++)
        {
            var segmentLeft = segment * (bandWidth - 1) - SegmentMargin;
            if (segmentLeft >= clipRight) break;
            var segmentWidth = bandWidth;
            if (segmentLeft + bandWidth - 1 > clipRight + 1)
                segmentWidth = Math.Max(MinimumBandEdge, (int)BitOperations.RoundUpToPowerOf2((uint)(clipRight + 1 - segmentLeft)));
            Array.Clear(coverage);
            RasterizeSegment(face, text, flags, coverage, segmentWidth, bandHeight, segmentLeft, clipBottom);
            var image = images[segment / bandsPerImage];
            var top = bandHeight * (segment % bandsPerImage);
            Convert(coverage, segmentWidth, bandHeight, effect, palette, image, top);
        }

        return new Gw2TextRasterResult(textWidth, bandWidth, bandHeight, segmentCount, images);
    }

    public static Gw2TextLayoutFlags KeepOneEffect(Gw2TextLayoutFlags flags)
    {
        foreach (var effect in ExclusiveEffectsByPriority)
            if ((flags & effect) != 0)
                return (flags & ~ExclusiveEffects) | effect;
        return flags;
    }

    public static int MeasureWidth(Gw2FontFace face, string text, Gw2TextLayoutFlags flags)
    {
        var pen = new Gw2FontPen(face.Metrics, (flags & Gw2TextLayoutFlags.WidenedSpacing) != 0);
        var mnemonicPending = false;
        foreach (var character in text)
        {
            if (character == '\0') break;
            if (character == MnemonicMarker && (flags & Gw2TextLayoutFlags.Mnemonics) != 0 && !mnemonicPending)
            {
                mnemonicPending = true;
                continue;
            }
            mnemonicPending = false;
            if (face.TryGetGlyph(character, out _, out var profile)) pen.Place(character, profile);
            else pen.AdvanceMissingCharacter();
        }
        return (int)pen.Extent;
    }

    private static (int Width, int Height) SingleImageSize(int spanWidth, int bandWidth, int bandHeight, int bands)
    {
        int width;
        int height;
        if (bands > 1)
        {
            width = PageSize;
            height = Math.Max(MinimumBandEdge, (int)BitOperations.RoundUpToPowerOf2((uint)(bandHeight * bands)));
        }
        else
        {
            width = Math.Max(bandHeight, (int)BitOperations.RoundUpToPowerOf2((uint)Math.Min(spanWidth, PageSize)));
            width = Math.Max(MinimumBandEdge, width);
            height = bandHeight;
        }
        while (BitOperations.Log2((uint)width) - BitOperations.Log2((uint)height) > MaximumAspectLog) height *= 2;
        return (width, height);
    }

    private static void RasterizeSegment(Gw2FontFace face, string text, Gw2TextLayoutFlags flags, byte[] coverage, int segmentWidth, int bandHeight, int segmentLeft, int clipBottom)
    {
        var pen = new Gw2FontPen(face.Metrics, (flags & Gw2TextLayoutFlags.WidenedSpacing) != 0);
        var rows = Math.Min(clipBottom, bandHeight);
        var mnemonicPending = false;
        var underlineWidth = 0;
        var underlineX = 0;
        foreach (var character in text)
        {
            if (character == '\0') break;
            if (character == MnemonicMarker && (flags & Gw2TextLayoutFlags.Mnemonics) != 0)
            {
                if (!mnemonicPending)
                {
                    mnemonicPending = true;
                    continue;
                }
                mnemonicPending = false;
            }
            if (!face.TryGetGlyph(character, out var glyph, out var profile))
            {
                pen.AdvanceMissingCharacter();
                mnemonicPending = false;
                continue;
            }
            var x = (int)pen.Place(character, profile);
            if (x + glyph.Width > segmentLeft && x < segmentLeft + segmentWidth)
            {
                Blit(glyph, x - segmentLeft, coverage, segmentWidth, rows);
                if (mnemonicPending && underlineWidth == 0)
                {
                    underlineX = Math.Max(x, segmentLeft) - segmentLeft;
                    underlineWidth = glyph.Width;
                }
            }
            mnemonicPending = false;
        }

        if (underlineWidth != 0 && (flags & Gw2TextLayoutFlags.Underline) == 0 && rows != 0)
            FillRow(coverage, segmentWidth, rows - 1, underlineX, Math.Min(underlineWidth, segmentWidth - underlineX));
        else if ((flags & Gw2TextLayoutFlags.Underline) != 0 && rows != 0)
            FillRow(coverage, segmentWidth, rows - 1, 0, segmentWidth);
        if ((flags & Gw2TextLayoutFlags.StrikeThrough) != 0)
            FillRow(coverage, segmentWidth, rows / 2, 0, segmentWidth);
    }

    private static void Blit(Gw2FontGlyph glyph, int destinationX, byte[] coverage, int stride, int rows)
    {
        var firstColumn = Math.Max(0, -destinationX);
        var lastColumn = Math.Min(glyph.Width, stride - destinationX);
        if (firstColumn >= lastColumn) return;
        var lastRow = Math.Min(glyph.Height, rows - glyph.TopOffset);
        for (var row = 0; row < lastRow; row++)
        {
            var target = (glyph.TopOffset + row) * stride + destinationX;
            var source = row * glyph.Width;
            for (var column = firstColumn; column < lastColumn; column++)
                coverage[target + column] = Math.Max(coverage[target + column], glyph.Coverage[source + column]);
        }
    }

    private static void FillRow(byte[] coverage, int stride, int row, int start, int length)
    {
        if (length <= 0) return;
        Array.Fill(coverage, byte.MaxValue, row * stride + start, length);
    }

    private static void Convert(byte[] coverage, int width, int height, uint effect, uint[] palette, Gw2TextImage image, int top)
    {
        for (var y = 0; y < height && top + y < image.Height; y++)
        for (var x = 0; x < width && x < image.Width; x++)
        {
            var color = palette[PaletteIndex(coverage, width, height, x, y, effect)];
            var offset = ((top + y) * image.Width + x) * 4;
            image.Rgba[offset + 0] = (byte)(color >> 16);
            image.Rgba[offset + 1] = (byte)(color >> 8);
            image.Rgba[offset + 2] = (byte)color;
            image.Rgba[offset + 3] = (byte)(color >> 24);
        }
    }

    private static int PaletteIndex(byte[] coverage, int width, int height, int x, int y, uint effect)
    {
        int At(int column, int row) => coverage[row * width + column];
        var value = At(x, y);
        switch (effect)
        {
            case 0x1:
                return x != 0 && At(x - 1, y) != 0 ? Math.Max(value, At(x - 1, y)) : value;
            case 0x4:
                return byte.MaxValue - value;
            case 0x8:
            {
                var sum = 0;
                for (var row = Math.Max(0, y - 1); row <= Math.Min(height - 1, y + 1); row++)
                for (var column = Math.Max(0, x - 1); column <= Math.Min(width - 1, x + 1); column++)
                    sum += At(column, row);
                return EffectIndex(value, sum / HaloDivisor);
            }
            case 0x10:
                return x != 0 && y != 0 ? EffectIndex(value, At(x - 1, y - 1)) : value;
            case 0x20:
                return x != width - 1 && y != height - 1 ? EffectIndex(value, At(x + 1, y + 1)) : value;
            case 0x40:
            {
                var sum = 0;
                for (var row = Math.Max(0, y - GlowRadius); row < Math.Min(height, y + GlowRadius + 1); row++)
                for (var column = Math.Max(0, x - GlowRadius); column < Math.Min(width, x + GlowRadius + 1); column++)
                    sum += At(column, row);
                return EffectIndex(value, sum / GlowDivisor);
            }
            default:
                return value;
        }
    }

    private static int EffectIndex(int value, int effectCoverage)
    {
        if (effectCoverage == 0) return value;
        effectCoverage = Math.Min(effectCoverage, byte.MaxValue);
        return value + ((effectCoverage >> 4) + 1) * PaletteBlockSize;
    }

    public static uint[] BuildPalette(uint argb)
    {
        var alpha = (int)(argb >> 24);
        var red = (int)(argb >> 16) & 0xFF;
        var green = (int)(argb >> 8) & 0xFF;
        var blue = (int)argb & 0xFF;
        var ramp = new int[PaletteBlockSize];
        for (var coverage = 0; coverage < PaletteBlockSize; coverage++)
            ramp[coverage] = coverage == 0 ? 0 : coverage < byte.MaxValue ? coverage * alpha / byte.MaxValue : alpha;

        var palette = new uint[PaletteBlockSize * (EffectLevels + 1)];
        for (var coverage = 0; coverage < PaletteBlockSize; coverage++)
        {
            var textAlpha = ramp[coverage];
            palette[coverage] = Pack(textAlpha, red, green, blue);
            for (var level = 0; level < EffectLevels; level++)
            {
                var transparency = (byte.MaxValue - textAlpha) * (byte.MaxValue - ramp[level * EffectLevels]) / byte.MaxValue;
                var combinedAlpha = byte.MaxValue - transparency;
                var color = transparency == byte.MaxValue
                    ? Pack(combinedAlpha, red, green, blue)
                    : Pack(combinedAlpha, textAlpha * red / combinedAlpha, textAlpha * green / combinedAlpha, textAlpha * blue / combinedAlpha);
                palette[coverage + (level + 1) * PaletteBlockSize] = color;
            }
        }
        return palette;
    }

    private static uint Pack(int alpha, int red, int green, int blue) =>
        (uint)alpha << 24 | (uint)red << 16 | (uint)green << 8 | (uint)blue;
}
