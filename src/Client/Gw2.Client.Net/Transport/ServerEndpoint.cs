using System;
using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace Gw2.Client.Net.Transport;

public sealed record ServerEndpoint(string Host, int Port)
{
    public const int DefaultAuthServerPort = 6112;
    public const int DefaultPortalPort = 6600;

    public static ServerEndpoint Parse(string text, int defaultPort)
    {
        if (IPAddress.TryParse(text, out var addressOnly)) return new ServerEndpoint(addressOnly.ToString(), defaultPort);
        if (IPEndPoint.TryParse(text, out var endpoint)) return new ServerEndpoint(endpoint.Address.ToString(), endpoint.Port);
        throw new FormatException($"'{text}' is not a numeric address with an optional port");
    }

    public override string ToString() => $"{Host}:{Port}";
}

public sealed record LoginServers(ServerEndpoint? Portal, ServerEndpoint? AuthServer, X509Certificate2? PortalCertificate, byte[]? AuthServerPublicValue)
{
    public static readonly LoginServers Retail = new(null, null, null, null);
}

public sealed record LoginServerSettings(string Portal, string AuthServer, string PortalCertificatePath, string AuthServerPublicValue)
{
    public static readonly LoginServerSettings Retail = new("", "", "", "");

    public LoginServers Parse()
    {
        try
        {
            return new LoginServers(
                Portal.Trim() is { Length: > 0 } portal ? ServerEndpoint.Parse(portal, ServerEndpoint.DefaultPortalPort) : null,
                AuthServer.Trim() is { Length: > 0 } authServer ? ServerEndpoint.Parse(authServer, ServerEndpoint.DefaultAuthServerPort) : null,
                PortalCertificatePath.Trim().Trim('"') is { Length: > 0 } certificatePath ? X509CertificateLoader.LoadCertificateFromFile(certificatePath) : null,
                AuthServerPublicValue.Trim() is { Length: > 0 } publicValue ? Convert.FromHexString(publicValue) : null);
        }
        catch (Exception exception) when (exception is FormatException or System.IO.IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            throw new FormatException($"server settings: {exception.Message}", exception);
        }
    }
}
