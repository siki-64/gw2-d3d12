using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Gw2.Formats;

public static partial class Gw2Installation
{
    public const string ExecutableFileName = "Gw2-64.exe";
    public const string DatFileName = "Gw2.dat";
    public const string LocalDatFileName = "Local.dat";

    private const string ProductName = "Guild Wars 2";
    private const string ArenaNetKey = @"Software\ArenaNet\" + ProductName;
    private const string ArenaNetSteamKey = @"Software\WOW6432Node\ArenaNet\" + ProductName + " Steam";
    private const string SteamInstallPathValue = "steamInstallPath";
    private const string SteamKey = @"Software\Valve\Steam";
    private const string SteamPathValue = "SteamPath";
    private const string SteamLibraryFile = @"steamapps\libraryfolders.vdf";
    private const string SteamGameFolder = @"steamapps\common\" + ProductName;

    public static string? ExecutablePath() => FirstExisting(ExecutableFileName);

    public static string? DatPath() => FirstExisting(DatFileName);

    public static string? LocalDatPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ProductName, LocalDatFileName) is var path && File.Exists(path) ? path : null;

    public static string? GraphicsSettingsPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ProductName, $"GFXSettings.{ExecutableFileName}.xml") is var path && File.Exists(path) ? path : null;

    public static IEnumerable<string> CandidateDirectories()
    {
        var directories = new List<string?> { Path.GetDirectoryName(Environment.ProcessPath) };
        if (OperatingSystem.IsWindows())
        {
            directories.Add(Registry.LocalMachine.OpenSubKey(ArenaNetSteamKey)?.GetValue(SteamInstallPathValue) as string);
            directories.Add(Registry.LocalMachine.OpenSubKey(ArenaNetKey)?.GetValue(SteamInstallPathValue) as string);
            if (Registry.CurrentUser.OpenSubKey(SteamKey)?.GetValue(SteamPathValue) is string steamPath)
                directories.AddRange(SteamLibraries(steamPath).Select(library => Path.Combine(library, SteamGameFolder)));
        }
        directories.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), ProductName));
        directories.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), ProductName));
        return directories.OfType<string>().Where(directory => directory.Length > 0).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static string? FirstExisting(string fileName) =>
        CandidateDirectories().Select(directory => Path.Combine(directory, fileName)).FirstOrDefault(File.Exists);

    private static IEnumerable<string> SteamLibraries(string steamPath)
    {
        yield return steamPath;
        var libraryFile = Path.Combine(steamPath, SteamLibraryFile);
        if (!File.Exists(libraryFile)) yield break;
        foreach (Match match in LibraryPathLine().Matches(File.ReadAllText(libraryFile)))
            yield return match.Groups[1].Value.Replace(@"\\", @"\");
    }

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"")]
    private static partial Regex LibraryPathLine();
}
