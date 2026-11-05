using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Gw2.Client.Net.Portal;
using Gw2.Protocol.Portal;
using Gw2.Protocol.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public sealed class LoginServerPinsTests
{
    [TestMethod]
    public void AnIncludedSelfSignedPortalCertificateIsAccepted()
    {
        using var own = SelfSigned("CN=cligate.101.ncplatform.net");
        using var other = SelfSigned("CN=cligate.101.ncplatform.net");
        var anchors = new PortalTrustAnchors(Array.Empty<TlsTrustAnchor>()).Including(own);

        Assert.IsTrue(anchors.Accepts(own));
        Assert.IsFalse(anchors.Accepts(other));
    }

    [TestMethod]
    public void TheServerPublicValueMustBeTheHandshakeValueLength()
    {
        var group = HandshakeGroup.Current;
        var value = new byte[HandshakeKeyExchange.ValueLength];
        value[0] = 7;

        CollectionAssert.AreEqual(value, group.WithServerPublicValue(value).ServerPublicValue);
        CollectionAssert.AreEqual(group.Modulus, group.WithServerPublicValue(value).Modulus);
        Assert.ThrowsExactly<ArgumentException>(() => group.WithServerPublicValue(new byte[HandshakeKeyExchange.ValueLength - 1]));
    }

    private static X509Certificate2 SelfSigned(string subject)
    {
        using var key = RSA.Create(1024);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }
}
