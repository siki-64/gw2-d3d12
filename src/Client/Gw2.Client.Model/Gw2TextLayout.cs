using Gw2.Dat.Content;

namespace Gw2.Client.Model;

public readonly record struct Gw2TextLineSpan(int Start, int DrawLength, float Width);

public readonly record struct Gw2TextBlock(string Text, uint Argb, Gw2TextLayoutFlags Flags);

public readonly record struct Gw2TextSnippet(string Text, uint Argb, int LinesBefore, bool Drawn);

public sealed record Gw2TextFrame(float Width, float Height, float FontHeight, float LineHeight, bool SingleLine);

public static class Gw2TextLayout
{
    private const char LineFeed = '\n';
    private const char Space = ' ';
    private const char Hyphen = '-';
    private const char UnicodeHyphen = '‐';
    private const char EnDash = '–';
    private const char EmDash = '—';

    private static readonly ushort[] NoBreakBefore =
    [
        0x21, 0x22, 0x25, 0x27, 0x29, 0x2C, 0x2E, 0x3A, 0x3B, 0x3F, 0x5D, 0x7D, 0xB0, 0xB7, 0x2013, 0x2014, 0x2019, 0x201D,
        0x2022, 0x2025, 0x2026, 0x2027, 0x2032, 0x2033, 0x3001, 0x3002, 0x3009, 0x300B, 0x300D, 0x300F, 0x3011, 0x3015,
        0x301E, 0x3041, 0x3043, 0x3045, 0x3047, 0x3049, 0x3063, 0x3083, 0x3085, 0x3087, 0x308E, 0x30A1, 0x30A3, 0x30A5,
        0x30A7, 0x30A9, 0x30C3, 0x30E3, 0x30E5, 0x30E7, 0x30EE, 0x30FB, 0x30FC, 0xFF01, 0xFF05, 0xFF09, 0xFF0C, 0xFF0E,
        0xFF1A, 0xFF1B, 0xFF1F, 0xFF3D, 0xFF5D, 0xFF61, 0xFF64, 0xFF70, 0xFF9E, 0xFF9F, 0xFFE0,
    ];

    private static readonly ushort[] NoBreakAfter =
    [
        0x24, 0x28, 0x5B, 0x5C, 0x7B, 0xA3, 0xA5, 0x2018, 0x2019, 0x201C, 0x2035, 0x3008, 0x300A, 0x300C, 0x300E, 0x3010,
        0x3014, 0x301D, 0xFF04, 0xFF08, 0xFF3B, 0xFF5B, 0xFFE1, 0xFFE5, 0xFFE6,
    ];

    public static int FitCount(Gw2FontFace face, ReadOnlySpan<char> text, Gw2TextLayoutFlags flags, uint maximumPixels, out uint extent)
    {
        var pen = new Gw2FontPen(face.Metrics, (flags & Gw2TextLayoutFlags.WidenedSpacing) != 0);
        var mnemonicPending = false;
        var count = 0;
        extent = 0;
        foreach (var character in text)
        {
            if (character == '\0') break;
            if (character == '_' && (flags & Gw2TextLayoutFlags.Mnemonics) != 0 && !mnemonicPending)
            {
                mnemonicPending = true;
                count++;
                continue;
            }
            mnemonicPending = false;
            if (face.TryGetGlyph(character, out _, out var profile)) pen.Place(character, profile);
            else pen.AdvanceMissingCharacter();
            if (pen.Extent > maximumPixels) break;
            extent = pen.Extent;
            count++;
        }
        return count;
    }

    public static uint PixelsFor(Gw2FontFace face, float width, float fontHeight)
    {
        var pixels = (face.CellHeight * width + fontHeight * 0.5f) / fontHeight;
        return pixels <= 0f ? 0u : pixels >= uint.MaxValue ? uint.MaxValue : (uint)pixels;
    }

    public static float WidthFor(Gw2FontFace face, uint pixels, float fontHeight) => pixels * fontHeight / face.CellHeight;

    public static bool CanBreakBefore(ReadOnlySpan<char> text, int index, int lineStart)
    {
        var character = index < text.Length ? text[index] : '\0';
        if (index == lineStart) return character <= Space;
        var previous = text[index - 1];
        if (Array.IndexOf(NoBreakBefore, (ushort)character) >= 0) return false;
        if (Array.IndexOf(NoBreakAfter, (ushort)previous) >= 0) return false;
        return character <= Space
            || previous is Hyphen or UnicodeHyphen or EnDash or EmDash
            || IsIdeographic(character)
            || IsIdeographic(previous);
    }

    public static bool IsIdeographic(char character) =>
        character is >= '　' and <= '龯'
            or >= '가' and <= '힣'
            or >= '豈' and <= '﫿'
            or >= '＀' and <= 'ￜ';

    public static Gw2TextLineSpan NextLine(Gw2FontFace face, string text, ref int position, Gw2TextLayoutFlags flags, float width, float offset, float fontHeight, bool singleLine)
    {
        var start = position;
        var remaining = text.Length - start;
        var fitted = FitCount(face, text.AsSpan(start, remaining), flags, PixelsFor(face, width - offset, fontHeight), out var extent);
        if (singleLine)
        {
            position = start + fitted;
            return new Gw2TextLineSpan(start, fitted, WidthFor(face, extent, fontHeight));
        }

        var lineFeed = text.IndexOf(LineFeed, start, fitted);
        var end = lineFeed >= 0 ? lineFeed : start + fitted;
        int next;
        if (end >= text.Length || end - start == remaining)
        {
            next = end;
        }
        else
        {
            var breakAt = end;
            while (breakAt != start && !CanBreakBefore(text, breakAt, start)) breakAt--;
            if (CanBreakBefore(text, breakAt, start))
            {
                next = text[breakAt] > Space ? breakAt : breakAt + 1;
                end = breakAt;
                while (end != start && text[end - 1] <= Space) end--;
            }
            else if (offset <= 0f)
            {
                next = end;
            }
            else
            {
                end = start;
                next = start;
            }
        }

        position = next;
        var drawLength = end - start;
        if (drawLength != fitted)
            FitCount(face, text.AsSpan(start, drawLength), flags, PixelsFor(face, width, fontHeight), out extent);
        return new Gw2TextLineSpan(start, drawLength, WidthFor(face, extent, fontHeight));
    }

    public static int MaximumLines(Gw2TextFrame frame, float offsetY)
    {
        if (frame.SingleLine) return 1;
        var height = Math.Max(0f, frame.Height + offsetY);
        var lines = (int)Math.Round(height / frame.LineHeight, MidpointRounding.ToEven);
        return Math.Max(1, lines);
    }

    public static List<Gw2TextBlock> Blocks(Gw2FontFace face, IReadOnlyList<Gw2TextSnippet> snippets, Gw2TextLayoutFlags flags, Gw2TextFrame frame)
    {
        var blocks = new List<Gw2TextBlock>();
        var offsetX = 0f;
        var offsetY = 0f;
        foreach (var snippet in snippets)
        {
            if (snippet.LinesBefore > 0 && !frame.SingleLine)
            {
                offsetX = 0f;
                offsetY -= frame.LineHeight * snippet.LinesBefore;
            }
            var text = frame.SingleLine ? snippet.Text.Replace(LineFeed, Space) : snippet.Text;
            var maximumLines = MaximumLines(frame, offsetY);
            var position = 0;
            var lineCount = 0;
            var lineOffset = offsetX;
            while (position < text.Length && lineCount != maximumLines)
            {
                var line = NextLine(face, text, ref position, flags, frame.Width, lineOffset, frame.FontHeight, frame.SingleLine);
                if (lineCount > 0)
                {
                    offsetY -= frame.LineHeight;
                    offsetX = 0f;
                }
                if (snippet.Drawn) blocks.Add(new Gw2TextBlock(text.Substring(line.Start, line.DrawLength), snippet.Argb, flags));
                offsetX += line.Width;
                lineOffset = 0f;
                lineCount++;
            }
        }
        return blocks;
    }
}
