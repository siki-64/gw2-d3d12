namespace Gw2.Client.Model;

public static class Gw2TextMarkup
{
    private const char TagOpen = '<';
    private const char TagClose = '>';
    private const char TagEnd = '/';
    private const char ValueSeparator = '=';
    private const char HexColorMarker = '#';
    private const char NamedColorMarker = '@';
    private const uint UnparsedColor = 0xFFFFFFFF;
    private const int HexColorDigits = 6;

    private static readonly Dictionary<string, uint> NamedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AbilityType"] = 0xFFFFEC8C,
        ["Flavor"] = 0xFF9BE8E4,
        ["Reminder"] = 0xFFB0B0B0,
        ["Quest"] = 0xFF00FF00,
        ["Task"] = 0xFFFFC957,
        ["Warning"] = 0xFFED0002,
        ["Event"] = 0xFFCC6633,
    };

    private static readonly Dictionary<string, string> InsertedTexts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ellipsis"] = "…",
        ["emdash"] = "—",
        ["music"] = "♪",
    };

    private const string OnomatopoeiaOpen = "ᓬ";
    private const string OnomatopoeiaClose = "ᕒ";

    public static List<Gw2TextSnippet> Split(string text, uint baseArgb, bool markupDisabled)
    {
        var snippets = new List<Gw2TextSnippet>();
        if (markupDisabled)
        {
            snippets.Add(new Gw2TextSnippet(text, baseArgb, 0, true));
            return snippets;
        }

        var colors = new Stack<uint>();
        var links = 0;
        var linesBefore = 0;
        var literalCharacters = 0;
        var position = 0;
        while (position < text.Length && text[position] != '\0')
        {
            var argb = colors.Count > 0 ? colors.Peek() : baseArgb;
            if (literalCharacters != 0 || text[position] != TagOpen)
            {
                var end = literalCharacters != 0
                    ? Math.Min(text.Length, position + (literalCharacters < 0 ? text.Length : literalCharacters))
                    : NextTag(text, position);
                if (literalCharacters > 0) literalCharacters -= end - position;
                snippets.Add(new Gw2TextSnippet(text[position..end], argb, linesBefore, links == 0));
                linesBefore = text[end - 1] == '\n' ? 1 : 0;
                position = end;
                continue;
            }

            var tagEnd = text.IndexOf(TagClose, position);
            if (tagEnd < 0) tagEnd = text.Length - 1;
            var tag = text[(position + 1)..tagEnd];
            position = tagEnd + 1;
            var closing = tag.StartsWith(TagEnd);
            var nameEnd = tag.IndexOfAny([' ', ValueSeparator]);
            var name = (nameEnd < 0 ? tag : tag[..nameEnd]).TrimStart(TagEnd).ToLowerInvariant();
            var value = UnnamedValue(tag);
            switch (name)
            {
                case "br":
                case "brx":
                    linesBefore += 1;
                    break;
                case "p":
                    linesBefore += 2;
                    break;
                case "c" when closing:
                    if (colors.Count > 0) colors.Pop();
                    break;
                case "c":
                    colors.Push(ParseColor(value) ?? UnparsedColor);
                    break;
                case "a" when closing:
                    if (links > 0) links--;
                    break;
                case "a":
                    links++;
                    break;
                case "ins" when value is not null && InsertedTexts.TryGetValue(value, out var inserted):
                    snippets.Add(new Gw2TextSnippet(inserted, argb, linesBefore, links == 0));
                    linesBefore = 0;
                    break;
                case "onoma":
                    snippets.Add(new Gw2TextSnippet(closing ? OnomatopoeiaClose : OnomatopoeiaOpen, argb, linesBefore, links == 0));
                    linesBefore = 0;
                    break;
                case "pg" when snippets.Count > 0:
                    return snippets;
                case "quote":
                    literalCharacters = value is not null && int.TryParse(value, out var count) ? count : -1;
                    break;
            }
        }
        return snippets;
    }

    public static uint? ParseColor(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        if (value[0] == HexColorMarker)
        {
            if (value.Length < 1 + HexColorDigits) return null;
            return uint.TryParse(value.AsSpan(1, HexColorDigits), System.Globalization.NumberStyles.HexNumber, null, out var rgb) ? 0xFF000000 | rgb : null;
        }
        if (value[0] == NamedColorMarker) return NamedColors.TryGetValue(value[1..], out var named) ? named : null;
        return uint.TryParse(value, out var number) ? number : null;
    }

    private static int NextTag(string text, int position)
    {
        var next = text.IndexOf(TagOpen, position);
        return next < 0 ? text.Length : next;
    }

    private static string? UnnamedValue(string tag)
    {
        var separator = tag.IndexOf(ValueSeparator);
        if (separator < 0) return null;
        var end = tag.IndexOf(' ', separator);
        return end < 0 ? tag[(separator + 1)..] : tag[(separator + 1)..end];
    }
}
