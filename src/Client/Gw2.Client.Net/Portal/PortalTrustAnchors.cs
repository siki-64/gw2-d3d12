using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using Gw2.Protocol.Portal;

namespace Gw2.Client.Net.Portal;

public sealed class PortalTrustAnchors
{
    private readonly IReadOnlyList<TlsTrustAnchor> _anchors;

    public PortalTrustAnchors(IReadOnlyList<TlsTrustAnchor> anchors) => _anchors = anchors;

    public static PortalTrustAnchors FromExecutable(string path) => new(TlsTrustAnchors.FromExecutable(path));

    public PortalTrustAnchors Including(X509Certificate2? certificate) => certificate is null
        ? this
        : new([.. _anchors, new TlsTrustAnchor(SequenceContent(certificate.SubjectName.RawData), certificate.GetPublicKey())]);

    public bool Accepts(X509Certificate certificate)
    {
        using var leaf = new X509Certificate2(certificate);
        byte[] issuerContent = SequenceContent(leaf.IssuerName.RawData);
        byte[] leafPublicKey = leaf.GetPublicKey();
        return _anchors.Any(anchor => anchor.SubjectNameContent.AsSpan().SequenceEqual(issuerContent) &&
                                      anchor.PublicKey.AsSpan().SequenceEqual(leafPublicKey));
    }

    private static byte[] SequenceContent(byte[] der)
    {
        int lengthOctet = der[1];
        int headerLength = lengthOctet < 0x80 ? 2 : 2 + (lengthOctet & 0x7f);
        return der.AsSpan(headerLength).ToArray();
    }
}
