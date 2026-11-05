using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Gw2.Client.Net.Login;
using Gw2.Formats;
using Gw2.Protocol.Capture;
using Gw2.Protocol.MsgPack;
using Gw2.Protocol.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public sealed class AccountPreferencesUploadTests
{
    private const uint LoginProtocol = 3;
    private const uint WeaponSkillOne = 21;
    private const uint KeyF10 = 41;
    private const int LiveCollisionSensitivity = 7;
    private const string RebindCapture = "network-209026-20261102-173407.gw2ncap";

    [TestMethod]
    public void UploadsARebindWithTheSameWireBytesAsTheGame()
    {
        var path = Path.Combine(RepositoryRoot(), "captures", "local", RebindCapture);
        if (!File.Exists(path)) Assert.Inconclusive($"{RebindCapture} is not in captures/local.");
        var capture = NetworkCapture.Read(path);
        if (capture.Build != RecordedSession.Schemas.Build) Assert.Inconclusive($"capture is build {capture.Build}");
        var outbound = CaptureDecoding.DecodeAll(capture, RecordedSession.Schemas).Single(result => result.Corpus == "login" && result.Stream.Direction == CaptureRecordKind.Outbound);
        var recordedUpload = outbound.Messages.Single(message => message.MessageId == LoginMessageIds.AccountPreferencesUpload);

        var seed = RandomNumberGenerator.GetBytes(HandshakeKeyExchange.SeedLength);
        var server = new LoopbackServer(RecordedSession.Group, seed);
        var connection = new ProtocolConnection(RecordedSession.Schemas.Login, RecordedSession.Group, seed, server.OnClientBytes);
        var session = new LoginSession(connection, LoginSessionTests.IdentityFrom(outbound.Messages), () => 0);
        session.Receive(server.KeyFrame());
        foreach (var chunk in capture.Chunks.Where(chunk => chunk.Kind == CaptureRecordKind.Inbound && chunk.Protocol == LoginProtocol))
        {
            session.Receive(server.Encrypt(chunk.Plaintext));
            connection.EndFrame();
            if (session.AccountPreferencePack is not null) break;
        }
        Assert.IsNotNull(session.AccountPreferencePack, "no account preference pack");
        Assert.AreEqual(LiveCollisionSensitivity, CameraPreferenceChunk.CollisionSensitivityOf(session.AccountPreferencePack));
        Assert.IsTrue(CameraPreferenceChunk.CameraSnapsCloseToTheCharacter(session.AccountPreferencePack));
        var camera = CameraPreferenceChunk.Read(session.AccountPreferencePack);
        Assert.AreEqual(LiveCollisionSensitivity, camera.CollisionSensitivity);
        Assert.IsTrue(camera.SnapsCloseToTheCharacter);
        Assert.IsTrue(camera.FieldOfViewDegrees is >= CameraPreferenceChunk.MinimumFieldOfViewDegrees and <= CameraPreferenceChunk.MaximumFieldOfViewDegrees);
        Assert.IsTrue(camera.Distance is >= CameraPreferenceChunk.MinimumDistance and <= CameraPreferenceChunk.MaximumDistance);
        CollectionAssert.AreEqual(session.AccountPreferencePack, CameraPreferenceChunk.ReplaceIn(session.AccountPreferencePack, camera));

        var rebound = new InputControlBinding(WeaponSkillOne, new InputBind(InputDevice.Keyboard, KeyF10, InputModifiers.None), InputBind.Unbound);
        session.UploadAccountPreferences(InputBindingChunk.ReplaceIn(session.AccountPreferencePack, [rebound], InputBindingLayout.Current));
        connection.EndFrame();

        var sent = MessageStreamDecoder.Decode(RecordedSession.Schemas.Login.Send, server.ReceivedPlaintext.SelectMany(bytes => bytes).ToArray());
        var upload = sent.Single(message => message.MessageId == LoginMessageIds.AccountPreferencesUpload);
        Assert.AreEqual(Convert.ToHexString(recordedUpload.Wire!), Convert.ToHexString(upload.Wire!));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "captures"))) directory = directory.Parent;
        return directory?.FullName ?? AppContext.BaseDirectory;
    }
}
