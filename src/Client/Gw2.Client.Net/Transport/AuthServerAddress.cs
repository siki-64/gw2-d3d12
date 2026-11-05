namespace Gw2.Client.Net.Transport;

public static class AuthServerAddress
{
    public const string DnsSuffix = "ArenaNetworks.com";

    public static string HostName(uint server, uint region) => $"Auth{server}.{region}.{DnsSuffix}";
}
