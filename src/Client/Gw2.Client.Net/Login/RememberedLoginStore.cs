using System;
using System.IO;
using System.Text;
using Gw2.Client.Net.Portal;
using Gw2.Formats;
using Gw2.Protocol.Login;

namespace Gw2.Client.Net.Login;

public sealed class RememberedLoginStore
{
    private const byte FormatVersion = 1;

    public RememberedLoginStore(string clientStorePath, string gameLocalArchivePath)
    {
        ClientStorePath = clientStorePath;
        GameLocalArchivePath = gameLocalArchivePath;
    }

    public static RememberedLoginStore ForCurrentUser() => new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Gw2ClientRemake", "RememberedLogin.bin"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Guild Wars 2", "Local.dat"));

    public string ClientStorePath { get; }

    public string GameLocalArchivePath { get; }

    public RememberedLogin? Read() => ReadClientStore() ?? ReadGameLocalArchive();

    public void Save(string email, byte[] passwordToken, string passwordTokenType)
    {
        int tokenVersion = IndexOfTokenType(passwordTokenType);
        if (tokenVersion < 0) return;
        byte[] protectedToken = RememberedPasswordToken.Protect(passwordToken);
        Directory.CreateDirectory(Path.GetDirectoryName(ClientStorePath)!);
        using var writer = new BinaryWriter(File.Create(ClientStorePath), Encoding.UTF8);
        writer.Write(FormatVersion);
        writer.Write(email);
        writer.Write((byte)tokenVersion);
        writer.Write(protectedToken.Length);
        writer.Write(protectedToken);
    }

    public void Forget()
    {
        if (File.Exists(ClientStorePath)) File.Delete(ClientStorePath);
    }

    private RememberedLogin? ReadClientStore()
    {
        if (!File.Exists(ClientStorePath)) return null;
        try
        {
            using var reader = new BinaryReader(File.OpenRead(ClientStorePath), Encoding.UTF8);
            if (reader.ReadByte() != FormatVersion) return null;
            string email = reader.ReadString();
            byte tokenVersion = reader.ReadByte();
            byte[] protectedToken = reader.ReadBytes(reader.ReadInt32());
            return new RememberedLogin(email, protectedToken, tokenVersion);
        }
        catch (EndOfStreamException)
        {
            return null;
        }
    }

    private RememberedLogin? ReadGameLocalArchive()
    {
        if (!File.Exists(GameLocalArchivePath)) return null;
        using var stream = new FileStream(GameLocalArchivePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var archive = FileArchive.Open(stream, verifyChecksums: false);
        return RememberedLogin.Read(archive);
    }

    private static int IndexOfTokenType(string passwordTokenType)
    {
        for (int version = 0; version < PortalCredential.PasswordTokenTypes.Count; version++)
            if (PortalCredential.PasswordTokenTypes[version] == passwordTokenType) return version;
        return -1;
    }
}
