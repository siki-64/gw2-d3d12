using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Gw2.Client.Net.Login;
using Gw2.Client.Net.Portal;
using Gw2.Protocol.Portal;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public class PortalLoginTests
{
    private static readonly Guid UserId = Guid.Parse("4afc811f-8786-f111-81c2-02de0bb68f60");
    private static readonly Guid GameToken = Guid.Parse("ea405e4b-6aed-4eef-9f65-2ada9117a1ee");

    [TestMethod]
    public void ASteamTicketLoginReturnsTheUserIdAndGameTokenTheAccountLoginSends()
    {
        using var server = new LoopbackPortal();
        byte[] ticket = { 0x14, 0x00, 0xAB, 0xCD };
        using var login = new PortalLogin(server.TrustAnchors, server.Port, null);

        var token = login.Login("127.0.0.1", PortalCredential.ForSteamTicket("steam name", ticket), Guid.NewGuid());
        var identity = EmptyIdentity().WithPortalToken(token);

        Assert.AreEqual(UserId, token.UserId);
        Assert.AreEqual(GameToken, token.Token);
        CollectionAssert.AreEqual(UserId.ToByteArray(), identity.PortalUserId);
        CollectionAssert.AreEqual(GameToken.ToByteArray(), identity.GameToken);
        Assert.AreEqual("Steam", identity.Provider);
        var startSsoLogin = server.Requests["/Auth/StartSsoLogin"];
        Assert.AreEqual("Steam", startSsoLogin.Element("Provider")!.Value);
        Assert.AreEqual(Convert.ToBase64String(Encoding.UTF8.GetBytes("1400abcd")), startSsoLogin.Element("Password")!.Value);
        Assert.AreEqual("Guild Wars 2", server.Requests["/Auth/RequestGameToken"].Element("AccountAlias")!.Value);
    }

    [TestMethod]
    public void ARememberedPasswordTokenIsSentAsPasswordTokenWithItsType()
    {
        using var server = new LoopbackPortal();
        byte[] passwordToken = Enumerable.Range(1, 20).Select(value => (byte)value).ToArray();
        using var login = new PortalLogin(server.TrustAnchors, server.Port, null);

        login.Login("127.0.0.1", PortalCredential.ForPasswordToken("name", passwordToken, 1), Guid.NewGuid());

        var startSsoLogin = server.Requests["/Auth/StartSsoLogin"];
        Assert.AreEqual("Portal", startSsoLogin.Element("Provider")!.Value);
        Assert.AreEqual(Convert.ToBase64String(passwordToken), startSsoLogin.Element("PasswordToken")!.Value);
        Assert.AreEqual("srp2", startSsoLogin.Element("PasswordType")!.Value);
        Assert.IsNull(startSsoLogin.Element("Password"));
    }

    [TestMethod]
    public void ACertificateOutsideTheTrustAnchorsIsRefused()
    {
        using var server = new LoopbackPortal();
        using var otherKey = RSA.Create(1024);
        var stranger = new PortalTrustAnchors(new[] { new TlsTrustAnchor(Array.Empty<byte>(), otherKey.ExportRSAPublicKey()) });
        using var login = new PortalLogin(stranger, server.Port, null);

        Assert.ThrowsExactly<System.Security.Authentication.AuthenticationException>(() =>
            login.Login("127.0.0.1", PortalCredential.ForPassword("name", "password"), Guid.NewGuid()));
    }

    private static LoginIdentity EmptyIdentity() =>
        new("", "", 0, Array.Empty<byte>(), 0, 0, Array.Empty<byte>(), Array.Empty<byte>(), "", "", Array.Empty<byte>(), 0);

    private sealed class LoopbackPortal : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly X509Certificate2 _certificate;

        public LoopbackPortal()
        {
            using var key = RSA.Create(1024);
            var request = new CertificateRequest("CN=cligate.test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            _certificate = X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pkcs12), null);
            var issuer = _certificate.IssuerName.RawData;
            TrustAnchors = new PortalTrustAnchors(new[] { new TlsTrustAnchor(issuer.AsSpan(2).ToArray(), _certificate.GetPublicKey()) });
            _listener.Start();
            _ = Task.Run(AcceptLoop);
        }

        public PortalTrustAnchors TrustAnchors { get; }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public ConcurrentDictionary<string, XElement> Requests { get; } = new();

        public void Dispose()
        {
            _listener.Stop();
            _certificate.Dispose();
        }

        private async Task AcceptLoop()
        {
            while (true)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync(); }
                catch (Exception) { return; }
                _ = Task.Run(() => Serve(client));
            }
        }

        private void Serve(TcpClient client)
        {
            using var _ = client;
            Stream stream = client.GetStream();
            var pending = new List<byte>();
            try
            {
                ReadMessage(stream, pending);
                var (startTlsPath, _, startTlsSequence) = ReadMessage(stream, pending);
                if (startTlsPath != "/Auth/StartTls") return;
                Write(stream, "400 Success", startTlsSequence, "<Error server=\"1001\" module=\"4\" line=\"330\"/>");
                var tls = new SslStream(stream, false);
                tls.AuthenticateAsServer(_certificate, false, System.Security.Authentication.SslProtocols.Tls12, false);
                stream = tls;
                while (true)
                {
                    var (path, body, sequence) = ReadMessage(stream, pending);
                    Requests[path] = body;
                    Write(stream, "200 OK", sequence, Reply(path));
                }
            }
            catch (Exception) { }
        }

        private static string Reply(string path) => path switch
        {
            "/Auth/GetHostname" => "<Reply><Local/><Hostname>127.0.0.1.</Hostname></Reply>",
            "/Auth/StartSsoLogin" => $"<Reply><UserId>{UserId.ToString().ToUpperInvariant()}</UserId><UserCenter>5</UserCenter><UserName>test.1234</UserName><LoginName>steam name</LoginName><Provider>Steam</Provider></Reply>",
            "/Account/ListMyGameAccounts" => "<Reply type=\"array\"><Row><GameCode>gw2</GameCode><Alias>Guild Wars 2</Alias></Row></Reply>",
            "/Auth/RequestGameToken" => $"<Reply><Token>{GameToken.ToString().ToUpperInvariant()}</Token></Reply>",
            _ => "<Reply/>",
        };

        private static void Write(Stream stream, string status, string sequence, string body)
        {
            var bodyBytes = Encoding.UTF8.GetBytes(body);
            var header = Encoding.ASCII.GetBytes($"STS/1.0 {status}\r\ns:{sequence}R\r\nl:{bodyBytes.Length}\r\n\r\n");
            stream.Write(header);
            stream.Write(bodyBytes);
            stream.Flush();
        }

        private static (string Path, XElement Body, string Sequence) ReadMessage(Stream stream, List<byte> pending)
        {
            var buffer = new byte[4096];
            int headerEnd;
            while ((headerEnd = IndexOfHeaderEnd(pending)) < 0) Fill(stream, pending, buffer);
            var lines = Encoding.ASCII.GetString(pending.GetRange(0, headerEnd).ToArray()).Split("\r\n");
            var headers = new Dictionary<string, string>();
            foreach (var line in lines[1..])
            {
                var colon = line.IndexOf(':');
                if (colon > 0) headers[line[..colon]] = line[(colon + 1)..].Split(';')[0];
            }
            var bodyLength = int.Parse(headers["l"]);
            while (pending.Count < headerEnd + 4 + bodyLength) Fill(stream, pending, buffer);
            var body = Encoding.UTF8.GetString(pending.GetRange(headerEnd + 4, bodyLength).ToArray());
            pending.RemoveRange(0, headerEnd + 4 + bodyLength);
            return (lines[0].Split(' ')[1], XElement.Parse(body), headers.GetValueOrDefault("s", ""));
        }

        private static void Fill(Stream stream, List<byte> pending, byte[] buffer)
        {
            var read = stream.Read(buffer);
            if (read == 0) throw new EndOfStreamException();
            pending.AddRange(buffer.AsSpan(0, read).ToArray());
        }

        private static int IndexOfHeaderEnd(List<byte> pending)
        {
            for (var index = 0; index + 3 < pending.Count; index++)
                if (pending[index] == '\r' && pending[index + 1] == '\n' && pending[index + 2] == '\r' && pending[index + 3] == '\n') return index;
            return -1;
        }
    }
}
