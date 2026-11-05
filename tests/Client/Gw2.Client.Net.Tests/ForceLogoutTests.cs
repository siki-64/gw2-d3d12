using System.Linq;
using System.Security.Cryptography;
using Gw2.Client.Net.Login;
using Gw2.Protocol.MsgPack;
using Gw2.Protocol.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public sealed class ForceLogoutTests
{
    private static LoginIdentity Identity => new("user", "host", (uint)RecordedSession.Schemas.Build, new byte[16], 1, 1, new byte[16], new byte[16], "", "Portal", [], 0);

    private static readonly byte[] UncompressedForceLogoutFrame = [0x00, 0x00, 0x03, 0x00, LoginMessageIds.ForceLogout, 0x00, 0x00];

    [TestMethod]
    public void AForcedLogoutIsAnsweredWithOneLogout()
    {
        var seed = RandomNumberGenerator.GetBytes(HandshakeKeyExchange.SeedLength);
        var server = new LoopbackServer(RecordedSession.Group, seed);
        var connection = new ProtocolConnection(RecordedSession.Schemas.Login, RecordedSession.Group, seed, server.OnClientBytes);
        var session = new LoginSession(connection, Identity, () => 0);
        session.Receive(server.KeyFrame());

        session.Receive(server.Encrypt(UncompressedForceLogoutFrame));
        connection.EndFrame();
        session.Logout();
        connection.EndFrame();

        Assert.IsTrue(session.IsForcedOut);
        var sent = MessageStreamDecoder.Decode(RecordedSession.Schemas.Login.Send, server.ReceivedPlaintext.SelectMany(bytes => bytes).ToArray());
        Assert.AreEqual(1, sent.Count(message => message.MessageId == LoginMessageIds.Logout && message.Fields[1].Value is 0UL));
    }
}
