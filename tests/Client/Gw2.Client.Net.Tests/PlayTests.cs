using Gw2.Formats;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Gw2.Client.Net.Game;
using Gw2.Client.Net.Login;
using Gw2.Dat.Content;
using Gw2.Protocol.Capture;
using Gw2.Protocol.MsgPack;
using Gw2.Protocol.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public sealed class PlayTests
{
    private const uint LoginProtocol = 3;
    private static readonly int[] PlayMessageIds = [LoginMessageIds.WorldSelect, LoginMessageIds.PlayCharacter, LoginMessageIds.MapTravel];

    [TestMethod]
    public void PlaySendsTheRecordedRequestsAndReadsTheReferral()
    {
        var capture = RecordedSession.Capture();
        var decoded = CaptureDecoding.DecodeAll(capture, RecordedSession.Schemas);
        var outbound = decoded.SingleOrDefault(result => result.Corpus == "login" && result.Stream.Direction == CaptureRecordKind.Outbound);
        if (outbound is null) Assert.Inconclusive("the capture holds no login connection");
        var recordedPlay = outbound.Messages.Where(message => PlayMessageIds.Contains(message.MessageId)).ToList();
        if (recordedPlay.Count != PlayMessageIds.Length) Assert.Inconclusive("the capture holds no Play");

        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !System.IO.File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        Gw2MapMetadata metadata;
        using (var reader = Gw2ContentReader.Open(dat, verifyChecksums: false))
            metadata = Gw2PackDecoders.Decode<Gw2MapMetadata>(reader.ReadDocument(Gw2MapMetadata.FileId))!;

        var seed = RandomNumberGenerator.GetBytes(HandshakeKeyExchange.SeedLength);
        var server = new LoopbackServer(RecordedSession.Group, seed);
        var connection = new ProtocolConnection(RecordedSession.Schemas.Login, RecordedSession.Group, seed, server.OnClientBytes);
        var identity = LoginSessionTests.IdentityFrom(outbound.Messages);
        var session = new LoginSession(connection, identity, () => 0);
        session.Receive(server.KeyFrame());

        var playName = (string)recordedPlay.Single(message => message.MessageId == LoginMessageIds.PlayCharacter).Fields[2].Value!;
        var playChunk = capture.Chunks.First(chunk => chunk.Kind == CaptureRecordKind.Outbound && chunk.Protocol == LoginProtocol && chunk.Plaintext.Length >= 2 && chunk.Plaintext[0] == LoginMessageIds.WorldSelect && chunk.Plaintext[1] == 0);
        var played = false;
        foreach (var chunk in capture.Chunks)
        {
            if (!played && chunk == playChunk)
            {
                session.Play(session.Characters.Characters.Single(character => character.Name == playName), mapId => MapTravelGameTypes.ForMapType(metadata.MapTypeOf(mapId)));
                connection.EndFrame();
                played = true;
            }
            if (chunk.Kind != CaptureRecordKind.Inbound || chunk.Protocol != LoginProtocol) continue;
            session.Receive(server.Encrypt(chunk.Plaintext));
            connection.EndFrame();
            if (session.Referral is not null) break;
        }

        var sent = MessageStreamDecoder.Decode(RecordedSession.Schemas.Login.Send, server.ReceivedPlaintext.SelectMany(bytes => bytes).ToArray());
        var replayedPlay = sent.Where(message => PlayMessageIds.Contains(message.MessageId)).ToList();
        CollectionAssert.AreEqual(recordedPlay.Select(message => Convert.ToHexString(message.Wire!)).ToList(), replayedPlay.Select(message => Convert.ToHexString(message.Wire!)).ToList(), "Play requests");
        Assert.IsNotNull(session.Referral, "no game server referral");
        Assert.IsNull(session.PlayFailure);

        var inbound = decoded.Single(result => result.Corpus == "login" && result.Stream.Direction == CaptureRecordKind.Inbound);
        var recordedReferral = Login.GameServerReferral.FromMessage(inbound.Messages.Last(message => message.MessageId == LoginMessageIds.GameServerReferral).Fields);
        Assert.AreEqual(recordedReferral.MapId, session.Referral.MapId);
        Assert.AreEqual(recordedReferral.Ticket, session.Referral.Ticket);
        Assert.AreEqual(recordedReferral.InstanceTicket, session.Referral.InstanceTicket);
        var record = new GameJoinRecord(identity.Build, session.Referral.Ticket, session.Referral.MapId, session.Referral.InstanceTicket, session.JoinToken.ToArray(), identity.PortalUserId).Encode();
        Assert.AreEqual(GameJoinRecord.Length, record.Length);
    }
}
