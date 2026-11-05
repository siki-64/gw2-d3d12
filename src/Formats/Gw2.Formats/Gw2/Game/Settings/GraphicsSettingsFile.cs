using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace Gw2.Formats;

public sealed class GraphicsSettingsFile
{
    private readonly Dictionary<string, XElement> _options;

    private GraphicsSettingsFile(Dictionary<string, XElement> options, (int Width, int Height)? resolution)
    {
        _options = options;
        Resolution = resolution;
    }

    public (int Width, int Height)? Resolution { get; }

    public static GraphicsSettingsFile Parse(string xml)
    {
        var settings = XDocument.Parse(xml).Root?.Element("GAMESETTINGS");
        var options = new Dictionary<string, XElement>(StringComparer.Ordinal);
        foreach (var option in settings?.Elements("OPTION") ?? [])
            if (option.Attribute("Name")?.Value is { } name) options[name] = option;
        var resolution = settings?.Element("RESOLUTION") is { } element
            && int.TryParse(element.Attribute("Width")?.Value, CultureInfo.InvariantCulture, out var width)
            && int.TryParse(element.Attribute("Height")?.Value, CultureInfo.InvariantCulture, out var height)
            ? (width, height)
            : ((int, int)?)null;
        return new GraphicsSettingsFile(options, resolution);
    }

    public string? ValueOf(string name) => _options.TryGetValue(name, out var option) ? option.Attribute("Value")?.Value : null;

    public int? EnumIndexOf(string name)
    {
        if (!_options.TryGetValue(name, out var option) || ValueOf(name) is not { } value) return null;
        var index = option.Elements("ENUM").Select(element => element.Attribute("EnumValue")?.Value).ToList().IndexOf(value);
        return index < 0 ? null : index;
    }

    public bool? BoolOf(string name) => bool.TryParse(ValueOf(name), out var value) ? value : null;

    public float? FloatOf(string name) => float.TryParse(ValueOf(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
}
