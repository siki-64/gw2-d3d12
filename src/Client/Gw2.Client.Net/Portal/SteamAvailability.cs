using System.Diagnostics;
using Microsoft.Win32;

namespace Gw2.Client.Net.Portal;

public static class SteamAvailability
{
    public const string ActiveProcessKey = @"Software\Valve\Steam\ActiveProcess";
    public const string AppsKey = @"Software\Valve\Steam\Apps";

    public static bool CanLogIn(uint appId) => IsSteamRunning() && IsUserLoggedIn() && UserOwnsApp(appId);

    public static bool IsSteamRunning()
    {
        if (ReadNumber(ActiveProcessKey, "pid") is not { } processId || processId == 0) return false;
        try
        {
            using var steam = Process.GetProcessById(processId);
            return !steam.HasExited;
        }
        catch (System.ArgumentException)
        {
            return false;
        }
    }

    public static bool IsUserLoggedIn() => ReadNumber(ActiveProcessKey, "ActiveUser") is { } activeUser && activeUser != 0;

    public static bool UserOwnsApp(uint appId)
    {
        using var app = Registry.CurrentUser.OpenSubKey($@"{AppsKey}\{appId}");
        return app is not null;
    }

    private static int? ReadNumber(string keyPath, string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        return key?.GetValue(valueName) as int?;
    }
}
