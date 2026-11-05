using System;
using System.IO;
using System.Text;
using Gw2.Formats;

namespace Gw2.Protocol.Portal;

public sealed record SteamApiDeployment(uint ArchiveFileId, uint AppId, string FolderName, string LibraryName)
{
    private const string LauncherAnchor = "48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? B9 ?? ?? ?? ?? E8 ?? ?? ?? ?? 85 C0 0F 84 ?? ?? ?? ?? 33 D2 48 8D 4D ?? 41 B8 00 10 00 00";
    private const string DeployAnchor = "4C 8D 0D ?? ?? ?? ?? 4C 8D 05 ?? ?? ?? ?? 48 8D 15 ?? ?? ?? ?? E8 ?? ?? ?? ?? 85 C0 75";
    private const int AppIdOffset = 13;
    private const int ArchiveNameFirstBase = 0xFF;
    private const int ArchiveNameSecondBase = 0x100;
    private const int ArchiveNameRadix = 0xFF00;

    public static SteamApiDeployment FromExecutable(string path) => Read(ExecutableImage.Open(path));

    public byte[] ReadLibrary(FileArchive archive)
    {
        using var library = new MemoryStream();
        archive.DecodeFileId(ArchiveFileId, library);
        return library.ToArray();
    }

    private static SteamApiDeployment Read(ExecutableImage executable)
    {
        ulong archiveName = CodeAnchor.Reference(LauncherAnchor, 3, 7).Locate(executable);
        ulong appIdSite = CodeAnchor.Site(LauncherAnchor, AppIdOffset).Locate(executable);
        var name = executable.Bytes(archiveName, 4);
        uint fileId = (uint)(BitConverter.ToUInt16(name) - ArchiveNameFirstBase + (BitConverter.ToUInt16(name[2..]) - ArchiveNameSecondBase) * ArchiveNameRadix);
        return new SteamApiDeployment(
            fileId,
            executable.Dword(appIdSite),
            WideText(executable, CodeAnchor.Reference(DeployAnchor, 10, 14).Locate(executable)),
            WideText(executable, CodeAnchor.Reference(DeployAnchor, 17, 21).Locate(executable)));
    }

    private static string WideText(ExecutableImage executable, ulong address)
    {
        var text = new StringBuilder();
        for (var next = address; executable.Bytes(next, 2) is { Length: 2 } unit && BitConverter.ToUInt16(unit) != 0; next += 2)
            text.Append((char)BitConverter.ToUInt16(executable.Bytes(next, 2)));
        return text.ToString();
    }
}
