using System.Globalization;
using System.Text.RegularExpressions;

namespace Gw2.Dat.Content;

public readonly record struct Gw2TextParameter(string Name, string Value)
{
    public static Gw2TextParameter Number(int index, long value, bool groupDigits) =>
        new($"num{index}", value.ToString(groupDigits ? "N0" : "D", CultureInfo.InvariantCulture));

    public static Gw2TextParameter Text(int index, string value) => new($"str{index}", value);
}

public static partial class Gw2TextTemplate
{
    private const string NoSeparatorTag = "[nosep]";
    private const string LeftBracketTag = "lbracket";
    private const string RightBracketTag = "rbracket";

    public static bool DisablesDigitGrouping(string template) => template.Contains(NoSeparatorTag, StringComparison.Ordinal);

    public static string Fill(string template, params Gw2TextParameter[] parameters)
    {
        var text = template.Replace(NoSeparatorTag, string.Empty, StringComparison.Ordinal);
        return Placeholder().Replace(text, match =>
            parameters.FirstOrDefault(parameter => parameter.Name == match.Groups[1].Value) is { Name: not null } found
                ? found.Value
                : match.Value);
    }

    public static string WithoutTags(string text) => Tag().Replace(text, match => match.Groups[1].Value switch
    {
        LeftBracketTag => "[",
        RightBracketTag => "]",
        _ => string.Empty,
    });

    [GeneratedRegex("%([a-z]+[0-9]+)%")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"\[([^\[\]]*)\]")]
    private static partial Regex Tag();
}
