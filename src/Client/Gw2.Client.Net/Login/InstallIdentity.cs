using System;
using System.IO;
using System.Net;
using Gw2.Formats;
using Gw2.Protocol.Login;

namespace Gw2.Client.Net.Login;

public static class InstallIdentity
{
    public static string GameLocalArchivePath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Guild Wars 2", "Local.dat");

    public static string ClientRecordPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Gw2ClientRemake", "Install.bin");

    public static Guid Current() => Resolve(GameLocalArchivePath, ClientRecordPath, Dns.GetHostName());

    public static Guid Resolve(string gameLocalArchivePath, string clientRecordPath, string computerName)
    {
        var gameRecord = ReadGameRecord(gameLocalArchivePath);
        if (gameRecord is not null && gameRecord.BelongsTo(computerName)) return gameRecord.InstallId;

        var clientRecord = File.Exists(clientRecordPath) ? InstallRecord.Parse(File.ReadAllBytes(clientRecordPath)) : null;
        if (clientRecord is not null && clientRecord.BelongsTo(computerName)) return clientRecord.InstallId;

        var previous = clientRecord ?? gameRecord;
        var replacement = new InstallRecord(Guid.NewGuid(), (previous?.UseCount ?? 0) + 1, InstallRecord.HashComputerName(computerName));
        Directory.CreateDirectory(Path.GetDirectoryName(clientRecordPath)!);
        File.WriteAllBytes(clientRecordPath, replacement.ToBytes());
        return replacement.InstallId;
    }

    private static InstallRecord? ReadGameRecord(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var archive = FileArchive.Open(stream, verifyChecksums: false);
            return InstallRecord.Read(archive);
        }
        catch (IOException)
        {
            return null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }
}
