using System;
using System.IO;
using System.Xml.Linq;
using Gw2.Client.Net.Transport;

namespace Gw2.Client.Net.Login;

public sealed record CustomServerState(LoginServerSettings Servers, bool IsSelected)
{
    public static CustomServerState Defaults { get; } = new(LoginServerSettings.Retail, IsSelected: false);
}

public static class CustomServerSettings
{
    private const string RootElement = "CUSTOMSERVERS";
    private const string SelectedAttribute = "selected";
    private const string PortalElement = "PORTAL";
    private const string PortalCertificateElement = "PORTALCERTIFICATE";
    private const string AuthServerElement = "AUTHSERVER";
    private const string AuthServerPublicValueElement = "AUTHSERVERPUBLICVALUE";

    public static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Gw2ClientRemake", "CustomServers.xml");

    public static CustomServerState Load()
    {
        try
        {
            return File.Exists(FilePath) ? Parse(File.ReadAllText(FilePath)) : CustomServerState.Defaults;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return CustomServerState.Defaults;
        }
    }

    public static void Save(CustomServerState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, ToXml(state));
    }

    public static string ToXml(CustomServerState state) =>
        new XDocument(new XElement(RootElement,
            new XAttribute(SelectedAttribute, state.IsSelected ? "true" : "false"),
            new XElement(PortalElement, state.Servers.Portal),
            new XElement(PortalCertificateElement, state.Servers.PortalCertificatePath),
            new XElement(AuthServerElement, state.Servers.AuthServer),
            new XElement(AuthServerPublicValueElement, state.Servers.AuthServerPublicValue))).ToString();

    public static CustomServerState Parse(string xml)
    {
        var root = XDocument.Parse(xml).Root;
        string Text(string element) => root?.Element(element)?.Value ?? "";
        return new CustomServerState(
            new LoginServerSettings(Text(PortalElement), Text(AuthServerElement), Text(PortalCertificateElement), Text(AuthServerPublicValueElement)),
            bool.TryParse(root?.Attribute(SelectedAttribute)?.Value, out var selected) && selected);
    }

    public static CustomServerState WithCommandLine(CustomServerState saved, LoginServerSettings commandLine)
    {
        if (commandLine == LoginServerSettings.Retail) return saved;
        string Pick(string fromCommandLine, string fromSaved) => fromCommandLine.Length > 0 ? fromCommandLine : fromSaved;
        return new CustomServerState(new LoginServerSettings(
            Pick(commandLine.Portal, saved.Servers.Portal),
            Pick(commandLine.AuthServer, saved.Servers.AuthServer),
            Pick(commandLine.PortalCertificatePath, saved.Servers.PortalCertificatePath),
            Pick(commandLine.AuthServerPublicValue, saved.Servers.AuthServerPublicValue)), IsSelected: true);
    }
}
