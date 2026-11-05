using System.Collections.Generic;
using System.Linq;
using Gw2.Formats;

namespace Gw2.Protocol.Portal;

public sealed record TlsTrustAnchor(byte[] SubjectNameContent, byte[] PublicKey);

public static class TlsTrustAnchors
{
    private const string TableAnchor = "48 8D 1D ?? ?? ?? ?? 48 8D 3D ?? ?? ?? ?? 0F 1F 40 00 8B 43 08 39 46 10";
    private const int EntryLength = 32;

    public static IReadOnlyList<TlsTrustAnchor> Current => Read(ExecutableImage.Current);

    public static IReadOnlyList<TlsTrustAnchor> FromExecutable(string path) => Read(ExecutableImage.Open(path));

    private static IReadOnlyList<TlsTrustAnchor> Read(ExecutableImage executable)
    {
        ulong tableStart = CodeAnchor.Reference(TableAnchor, 3, 7).Locate(executable);
        ulong tableEnd = CodeAnchor.Reference(TableAnchor, 10, 14).Locate(executable);
        var anchors = new List<TlsTrustAnchor>();
        for (ulong entry = tableStart; entry + EntryLength <= tableEnd; entry += EntryLength)
            anchors.Add(new TlsTrustAnchor(
                executable.Bytes(executable.Qword(entry), (int)executable.Dword(entry + 8)).ToArray(),
                executable.Bytes(executable.Qword(entry + 16), (int)executable.Dword(entry + 24)).ToArray()));
        return anchors;
    }
}
