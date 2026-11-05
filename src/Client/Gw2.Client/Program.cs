using Gw2.Client.Settings;
using System;

namespace Gw2.Client;

public static class Program
{
    [STAThread]
    public static void Main(string[] arguments)
    {
        using var client = new Gw2Client(Gw2ClientOptions.Parse(arguments));
        client.Run();
    }
}
