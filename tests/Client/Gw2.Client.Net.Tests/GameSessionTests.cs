using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using Gw2.Client.Net.Game;
using Gw2.Client.Net.Login;
using Gw2.Protocol.Capture;
using Gw2.Protocol.MsgPack;
using Gw2.Protocol.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public sealed class GameSessionTests
{
    private const int SystemRecordMessageId = 0x4;
    private const int AccountLoginMessageId = 0xA;
    private const int ProviderFieldIndex = 5;
    private const int AdapterNameField = 7;
    private const int AdapterDeviceIdField = 8;
    private const int VideoMemoryMegabytesField = 11;
    private const int AdapterVendorIdField = 12;
    private const int SessionIdField = 21;
    private const int MegabyteShift = 20;
    private const int CreateAgentIdField = 1;
    private const int CreatePositionField = 11;
    private const float SpawnToCreateToleranceInGameUnits = 1f;
    private static readonly int[] MapEntryAnswers =
        [GameMessageIds.DeviceReport, GameMessageIds.AccountLogin, GameMessageIds.Languages, GameMessageIds.MapStateAcknowledged, GameMessageIds.MapLoaded, GameMessageIds.AgentsLoaded];

    [TestMethod]
    public void AnswersTheMapEntryLikeTheRecordedClient()
    {
        var (_, recorded, sent) = PlayRecordedMapEntry();
        CollectionAssert.AreEqual(recorded.Select(message => Convert.ToHexString(message.Wire!)).ToList(), sent.Select(message => Convert.ToHexString(message.Wire!)).ToList());
    }

    [TestMethod]
    public void TakesTheSpawnPositionFromTheLoadMapMessage()
    {
        var (session, _, _) = PlayRecordedMapEntry();
        var ownCreate = CaptureDecoding.DecodeAll(RecordedSession.Capture(), RecordedSession.Schemas)
            .Where(result => result.Corpus == "game" && result.Stream.Direction == CaptureRecordKind.Inbound)
            .SelectMany(result => result.Messages)
            .First(message => message.MessageId == GameAgents.CharacterCreate && (ulong)message.Fields[CreateAgentIdField].Value! == session.ControlledAgentId);
        var header = ((DecodedArrayHeader)ownCreate.Fields[CreatePositionField].Value!).Header;
        var createPosition = new Vector3(BitConverter.ToSingle(header, 0), BitConverter.ToSingle(header, 4), BitConverter.ToSingle(header, 8));
        Assert.IsNotNull(session.LoadMapSpawnPositionInGameUnits);
        Console.WriteLine($"spawn {session.LoadMapSpawnPositionInGameUnits} create {createPosition}");
        Assert.IsTrue(Vector3.Distance(session.LoadMapSpawnPositionInGameUnits.Value, createPosition) < SpawnToCreateToleranceInGameUnits);
    }

    private static (GameSession Session, List<DecodedMessage> Recorded, IReadOnlyList<DecodedMessage> Sent) PlayRecordedMapEntry()
    {
        var capture = RecordedSession.Capture();
        var decoded = CaptureDecoding.DecodeAll(capture, RecordedSession.Schemas);
        var gameOutbound = decoded.FirstOrDefault(result => result.Corpus == "game" && result.Stream.Direction == CaptureRecordKind.Outbound);
        if (gameOutbound is null) Assert.Inconclusive("the capture holds no game connection");
        var characterName = (string)decoded.First(result => result.Corpus == "login" && result.Stream.Direction == CaptureRecordKind.Outbound).Messages.Last(message => message.MessageId == LoginMessageIds.PlayCharacter).Fields[2].Value!;
        var recorded = gameOutbound.Messages.Where(message => MapEntryAnswers.Contains(message.MessageId)).Take(MapEntryAnswers.Length).ToList();
        CollectionAssert.AreEqual(MapEntryAnswers, recorded.Select(message => message.MessageId).ToList(), "recorded map entry");

        var loginOutbound = decoded.First(result => result.Corpus == "login" && result.Stream.Direction == CaptureRecordKind.Outbound).Messages;
        var system = SystemRecord.Parse((byte[])loginOutbound.First(message => message.MessageId == SystemRecordMessageId && message.Fields[1].Value is byte[] { Length: SystemRecord.Length }).Fields[1].Value!);
        var provider = (string)loginOutbound.First(message => message.MessageId == AccountLoginMessageId).Fields[ProviderFieldIndex].Value!;
        var recordedDevice = recorded[0].Fields;
        var adapter = new GameAdapter((string)recordedDevice[AdapterNameField].Value!, (uint)(ulong)recordedDevice[AdapterVendorIdField].Value!,
            (uint)(ulong)recordedDevice[AdapterDeviceIdField].Value!, (long)((ulong)recordedDevice[VideoMemoryMegabytesField].Value! << MegabyteShift));
        var sessionId = (byte[])recordedDevice[SessionIdField].Value!;
        var machineSeed = BinaryPrimitives.ReadUInt32LittleEndian(sessionId) ^ 1;
        var seconds = BinaryPrimitives.ReadUInt32LittleEndian(sessionId.AsSpan(4));
        CollectionAssert.AreEqual(sessionId, DebugSessionId.Create(machineSeed, new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(seconds)), "session id layout");
        var storefront = provider == "Steam" ? Storefront.Steam : Storefront.None;
        var accountLoginName = (string)recorded[1].Fields[1].Value!;
        var identity = new GameIdentity(characterName, accountLoginName, provider, LoginFlow.EnglishLanguage, LoginFlow.EnglishLanguage, new DeviceReport(system, adapter, storefront, sessionId));

        var seed = RandomNumberGenerator.GetBytes(HandshakeKeyExchange.SeedLength);
        var server = new LoopbackServer(RecordedSession.Group, seed);
        var connection = new ProtocolConnection(RecordedSession.Schemas.Game, RecordedSession.Group, seed, server.OnClientBytes);
        var session = new GameSession(null, connection, identity);
        session.Receive(server.KeyFrame());

        var streamConnections = capture.Chunks.Where(chunk => NetworkCapture.IsStream(chunk.Kind)).Select(chunk => chunk.Connection).Distinct().ToList();
        var gameConnection = streamConnections[gameOutbound.Stream.ConnectionOrder];
        foreach (var chunk in capture.Chunks.Where(chunk => chunk.Kind == CaptureRecordKind.Inbound && chunk.Connection == gameConnection))
        {
            session.Receive(server.Encrypt(chunk.Plaintext));
            if (session.LoadMapId is not null) session.ReportMapLoaded();
            session.SendMapLoadedWhenReported();
            connection.EndFrame();
            if (session.ControlledAgentId is not null) break;
        }

        var sent = MessageStreamDecoder.Decode(RecordedSession.Schemas.Game.Send, server.ReceivedPlaintext.SelectMany(bytes => bytes).ToArray());
        return (session, recorded, sent);
    }
}
