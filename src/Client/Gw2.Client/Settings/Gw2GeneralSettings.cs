using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Gw2.Client.Settings;

public sealed record Gw2GeneralState(bool CameraSmoothing)
{
    public static Gw2GeneralState Defaults { get; } = new(CameraSmoothing: true);
}

public static class Gw2GeneralSettings
{
    private const string RootElement = "GENERAL";
    private const string OptionElement = "OPTION";
    private const string CameraSmoothing = "cameraSmoothing";

    public static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Gw2ClientRemake", "GeneralSettings.xml");

    public static Gw2GeneralState Load() => File.Exists(FilePath) ? Parse(File.ReadAllText(FilePath)) : Gw2GeneralState.Defaults;

    public static void Save(Gw2GeneralState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, ToXml(state));
    }

    public static string ToXml(Gw2GeneralState state) =>
        new XDocument(new XElement(RootElement,
            new XElement(OptionElement, new XAttribute("Name", CameraSmoothing), new XAttribute("Value", state.CameraSmoothing ? "true" : "false")))).ToString();

    public static Gw2GeneralState Parse(string xml)
    {
        var options = XDocument.Parse(xml).Root?.Elements(OptionElement) ?? [];
        var smoothing = options.FirstOrDefault(option => option.Attribute("Name")?.Value == CameraSmoothing)?.Attribute("Value")?.Value;
        return new Gw2GeneralState(bool.TryParse(smoothing, out var on) ? on : Gw2GeneralState.Defaults.CameraSmoothing);
    }
}
