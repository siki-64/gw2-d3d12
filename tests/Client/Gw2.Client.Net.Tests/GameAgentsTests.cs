using System.Collections.Generic;
using System;
using System.Linq;
using System.Numerics;
using Gw2.Client.Net.Game;
using Gw2.Dat.Archive;
using Gw2.Dat.Content;
using Gw2.Client.Net.Keys;
using Gw2.Formats;
using Gw2.Protocol.Capture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public sealed class GameAgentsTests
{
    private const int PlayerControlMessageId = 0x1bc;
    private const int ControlledAgentField = 3;
    private const int OwnMovementMessageId = 0x1a;
    private const int OwnMovementPositionField = 4;
    private const float CollisionUnitsPerGameUnit = 1f / 32f;
    private const int SkillCommandMessageId = 0x17;
    private const int SkillCommandPayloadField = 5;
    private const byte InteractSubcommand = 0x1f;

    [TestMethod]
    public void PlacesTheOwnCharacterWhereTheRecordedClientReportsIt()
    {
        var decoded = CaptureDecoding.DecodeAll(RecordedSession.Capture(), RecordedSession.Schemas);
        var inbound = decoded.FirstOrDefault(result => result.Corpus == "game" && result.Stream.Direction == CaptureRecordKind.Inbound);
        var outbound = decoded.FirstOrDefault(result => result.Corpus == "game" && result.Stream.Direction == CaptureRecordKind.Outbound);
        if (inbound is null || outbound is null) Assert.Inconclusive("the capture holds no game connection");
        var ownAgentId = (uint)(ulong)inbound.Messages.First(message => message.MessageId == PlayerControlMessageId).Fields[ControlledAgentField].Value!;
        var ownMovement = outbound.Messages.FirstOrDefault(message => message.MessageId == OwnMovementMessageId);
        if (ownMovement is null) Assert.Inconclusive("the recorded client never moved");

        var agents = new GameAgents();
        foreach (var message in inbound.Messages)
        {
            agents.Handle(message);
            if (agents.TryGet(ownAgentId, out _)) break;
        }

        Assert.IsTrue(agents.TryGet(ownAgentId, out var own), "own character created");
        var reported = (byte[])ownMovement.Fields[OwnMovementPositionField].Value!;
        var reportedInGameUnits = new Vector3(BitConverter.ToSingle(reported, 0), BitConverter.ToSingle(reported, 4), -BitConverter.ToSingle(reported, 8)) / CollisionUnitsPerGameUnit;
        Assert.IsTrue(Vector3.Distance(own.PositionInGameUnits, reportedInGameUnits) < 1f, $"created at {own.PositionInGameUnits}, reported {reportedInGameUnits}");
        Assert.IsTrue(own.HeightInGameUnits > 2f * own.RadiusInGameUnits, $"height {own.HeightInGameUnits}, radius {own.RadiusInGameUnits}");
    }

    [TestMethod]
    public void TakesTheTravelSpeedOnlyFromSpeedKindZero()
    {
        var decoded = CaptureDecoding.DecodeAll(RecordedSession.Capture(), RecordedSession.Schemas);
        var inbound = decoded.FirstOrDefault(result => result.Corpus == "game" && result.Stream.Direction == CaptureRecordKind.Inbound);
        if (inbound is null) Assert.Inconclusive("the capture holds no game connection");
        var agents = new GameAgents();
        var factorMessages = 0;
        foreach (var message in inbound.Messages)
        {
            agents.Handle(message);
            if (message.MessageId != GameAgents.AgentSpeed || message.Fields[4].Value is not ulong kind || kind == 0) continue;
            factorMessages++;
            var agentId = (uint)(ulong)message.Fields[1].Value!;
            if (agents.TryGet(agentId, out var agent)) Assert.IsTrue(agent.TravelSpeed == 0f || agent.TravelSpeed > 50f, $"agent {agentId} travel speed {agent.TravelSpeed} after speed kind {kind}");
        }
        if (factorMessages == 0) Assert.Inconclusive("the capture holds no speed factor");
    }

    [TestMethod]
    public void KeepsOnlyAgentsTheServerHasNotRemoved()
    {
        var decoded = CaptureDecoding.DecodeAll(RecordedSession.Capture(), RecordedSession.Schemas);
        var inbound = decoded.FirstOrDefault(result => result.Corpus == "game" && result.Stream.Direction == CaptureRecordKind.Inbound);
        if (inbound is null) Assert.Inconclusive("the capture holds no game connection");
        var agents = new GameAgents();
        foreach (var message in inbound.Messages) agents.Handle(message);

        var created = inbound.Messages.Where(message => message.MessageId == GameAgents.CharacterCreate).Select(message => (uint)(ulong)message.Fields[1].Value!).ToHashSet();
        var removedLast = inbound.Messages.Where(message => message.MessageId is GameAgents.CharacterCreate or GameAgents.AgentRemove)
            .GroupBy(message => (uint)(ulong)message.Fields[1].Value!)
            .Where(group => group.Last().MessageId == GameAgents.AgentRemove).Select(group => group.Key).ToHashSet();
        Assert.IsTrue(agents.All.Count > 0, "agents in the world");
        Assert.IsTrue(agents.All.All(agent => created.Contains(agent.AgentId) && !removedLast.Contains(agent.AgentId)));
        Assert.AreEqual(created.Count - created.Count(removedLast.Contains), agents.All.Count);
    }

    [TestMethod]
    public void NamesAndJudgesTheAgentsLikeTheServerSays()
    {
        var decoded = CaptureDecoding.DecodeAll(RecordedSession.Capture(), RecordedSession.Schemas);
        var inbound = decoded.FirstOrDefault(result => result.Corpus == "game" && result.Stream.Direction == CaptureRecordKind.Inbound);
        if (inbound is null) Assert.Inconclusive("the capture holds no game connection");
        var agents = new GameAgents();
        foreach (var message in inbound.Messages) agents.Handle(message);

        var coded = agents.All.Where(agent => agent.CodedName is not null).ToList();
        Assert.IsTrue(coded.Count > 0, "coded names");
        Assert.IsTrue(coded.All(agent => CodedNpcName.TryToTextId(agent.CodedName!, out _, out _)), "every coded name decodes");
        var players = agents.All.Where(agent => agent.PlayerId is not null).ToList();
        Assert.IsTrue(players.Count > 0 && players.All(agent => agents.PlayerNameOf(agent) is { Length: > 0 }), "every player named");
        Assert.IsTrue(agents.All.Select(agent => agent.Attitude).Distinct().Count() > 1, "attitudes vary");
    }

    [TestMethod]
    public void FollowsTheRecordedMovementOfEveryAgent()
    {
        var decoded = CaptureDecoding.DecodeAll(RecordedSession.Capture(), RecordedSession.Schemas);
        var inbound = decoded.FirstOrDefault(result => result.Corpus == "game" && result.Stream.Direction == CaptureRecordKind.Inbound);
        if (inbound is null) Assert.Inconclusive("the capture holds no game connection");
        var agents = new GameAgents();
        var moved = 0;
        foreach (var message in inbound.Messages)
        {
            var agentId = message.Fields.Count > 1 && message.Fields[1].Value is ulong id ? (uint)id : 0u;
            var before = agents.TryGet(agentId, out var known) ? known.PositionInGameUnits : (Vector3?)null;
            agents.Handle(message);
            if (message.MessageId is not (GameAgents.AgentTransform or GameAgents.AgentPosition) || before is not { } start) continue;
            Assert.IsTrue(agents.TryGet(agentId, out var after));
            Assert.IsTrue(float.IsFinite(after.PositionInGameUnits.Length()), $"agent {agentId} at {after.PositionInGameUnits}");
            if (after.PositionInGameUnits != start) moved++;
        }
        Assert.IsTrue(moved > 0, "some agent moved");
    }

    [TestMethod]
    public void WalksTravellingAgentsTowardsTheirDestinationAtTheirSpeed()
    {
        const float StepSeconds = 0.1f;
        var decoded = CaptureDecoding.DecodeAll(RecordedSession.Capture(), RecordedSession.Schemas);
        var inbound = decoded.FirstOrDefault(result => result.Corpus == "game" && result.Stream.Direction == CaptureRecordKind.Inbound);
        if (inbound is null) Assert.Inconclusive("the capture holds no game connection");
        var agents = new GameAgents();
        foreach (var message in inbound.Messages) agents.Handle(message);

        var travellers = agents.All.Where(agent => agent.TravelDestination is { } destination && agent.TravelSpeed > 0f
            && Vector3.Distance(agent.PositionInGameUnits, destination) > agent.TravelSpeed * StepSeconds).ToList();
        Assert.IsTrue(travellers.Count > 0, "agents travelling at the end of the capture");
        agents.Advance(StepSeconds);
        foreach (var before in travellers)
        {
            Assert.IsTrue(agents.TryGet(before.AgentId, out var after));
            Assert.AreEqual(before.TravelSpeed * StepSeconds, Vector3.Distance(before.PositionInGameUnits, after.PositionInGameUnits), 0.01f, $"agent {before.AgentId} step");
            Assert.IsTrue(Vector3.Distance(after.PositionInGameUnits, before.TravelDestination!.Value) < Vector3.Distance(before.PositionInGameUnits, before.TravelDestination!.Value));
        }
    }

    [TestMethod]
    public void NamesEveryUnnamedCharacterByItsSpecies()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        var decoded = CaptureDecoding.DecodeAll(RecordedSession.Capture(), RecordedSession.Schemas);
        var inbound = decoded.FirstOrDefault(result => result.Corpus == "game" && result.Stream.Direction == CaptureRecordKind.Inbound);
        if (inbound is null) Assert.Inconclusive("the capture holds no game connection");
        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var delivery = new TextKeyDelivery((textId, key) => reader.Keys.Store(Gw2KeyStore.TextKind, textId, key), () => { });
        var agents = new GameAgents();
        foreach (var message in inbound.Messages)
        {
            agents.Handle(message);
            delivery.Handle(message);
        }
        var species = new Gw2SpeciesDefinitions(reader.ContentIndex, SpeciesContentLayout.Current);

        var unnamed = agents.All.Where(agent => agent.CodedName is null && agent.SpeciesId is not null).ToList();
        Assert.IsTrue(unnamed.Count > 0, "characters without a coded name");
        foreach (var agent in unnamed)
        {
            var textId = species.NameTextIdOf(agent.SpeciesId!.Value);
            Assert.IsNotNull(textId, $"species {agent.SpeciesId} has a name");
            var read = reader.ReadKnownText(textId.Value, Gw2Language.English);
            Assert.IsTrue(read.Status is Gw2TextStatus.Plain or Gw2TextStatus.Decrypted && read.Text.Length > 0, $"species {agent.SpeciesId} text {textId}: {read.Status}");
        }
    }

    [TestMethod]
    public void SpeciesNameAModelFileTheArchiveDecodesAsAModel()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var species = new Gw2SpeciesDefinitions(reader.ContentIndex, SpeciesContentLayout.Current);

        var modelFiles = reader.ContentIndex.All(Gw2SpeciesDefinitions.SpeciesContentType)
            .Select(location => species.ModelFileIdOf(reader.ContentIndex.DataIdOf(Gw2SpeciesDefinitions.SpeciesContentType, location)))
            .OfType<uint>().Distinct().Take(50).ToList();
        Assert.IsTrue(modelFiles.Count > 0, "species with a model file");
        foreach (var fileId in modelFiles)
            Assert.AreEqual(Gw2FourCc.Model, System.Text.Encoding.ASCII.GetString(reader.ReadBytes(fileId), 8, 4), $"model file {fileId}");
    }

    [TestMethod]
    public void PlacesTheGadgetsTheServerCreatesUntilItRemovesThem()
    {
        var decoded = CaptureDecoding.DecodeAll(RecordedSession.Capture(), RecordedSession.Schemas);
        var inbound = decoded.FirstOrDefault(result => result.Corpus == "game" && result.Stream.Direction == CaptureRecordKind.Inbound);
        if (inbound is null) Assert.Inconclusive("the capture holds no game connection");
        if (!inbound.Messages.Any(message => message.MessageId == GameAgents.KeyframedCreate)) Assert.Inconclusive("the capture holds no gadgets");
        var agents = new GameAgents();
        foreach (var message in inbound.Messages) agents.Handle(message);

        var lastKeyframed = inbound.Messages.Where(message => message.MessageId is GameAgents.KeyframedCreate or GameAgents.AgentRemove)
            .GroupBy(message => (uint)(ulong)message.Fields[1].Value!)
            .Where(group => group.Any(message => message.MessageId == GameAgents.KeyframedCreate) && group.Last().MessageId == GameAgents.KeyframedCreate)
            .Select(group => group.Key).ToHashSet();
        Assert.IsTrue(lastKeyframed.SetEquals(agents.Keyframed.Select(agent => agent.AgentId)));
        Assert.IsTrue(agents.Keyframed.All(agent => agent.AgentType is GameAgentType.Gadget or GameAgentType.GadgetAttackTarget or GameAgentType.Item), "keyframed agents are gadgets, attack targets or items");
        Assert.IsTrue(agents.Keyframed.Any(agent => agent.AgentType == GameAgentType.Gadget && agent.ModelFileId is > 0), "gadgets name a model");
        Assert.IsTrue(agents.Keyframed.All(agent => agent.AgentType != GameAgentType.GadgetAttackTarget || agent.ModelFileId is null), "attack targets carry no model");
        Assert.IsTrue(agents.Keyframed.All(agent => MathF.Abs(agent.Rotation.Length() - 1f) < 1e-3f && agent.Scale > 0f), "rotations are unit and scales positive");
        var characters = agents.All.Select(agent => agent.PositionInGameUnits).ToList();
        Assert.IsTrue(agents.Keyframed.Any(agent => characters.Any(position => Vector3.Distance(position, agent.PositionInGameUnits) < 100f)), "a gadget stands beside a character");
    }

    [TestMethod]
    public void NamesTheItemsOnTheGroundAndRemovesTheOnePickedUp()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        var decoded = CaptureDecoding.DecodeAll(RecordedSession.Capture(), RecordedSession.Schemas);
        var outbound = decoded.LastOrDefault(result => result.Corpus == "game" && result.Stream.Direction == CaptureRecordKind.Outbound);
        var inbound = decoded.LastOrDefault(result => result.Corpus == "game" && result.Stream.Direction == CaptureRecordKind.Inbound);
        if (inbound is null || outbound is null) Assert.Inconclusive("the capture holds no game connection");
        var pickedUp = outbound.Messages.Where(message => message.MessageId == SkillCommandMessageId && message.Fields[SkillCommandPayloadField].Value is byte[] { Length: 5 } payload && payload[0] == InteractSubcommand)
            .Select(message => BitConverter.ToUInt32((byte[])message.Fields[SkillCommandPayloadField].Value!, 1)).ToHashSet();
        if (pickedUp.Count == 0) Assert.Inconclusive("the recorded client picked nothing up");
        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var delivery = new TextKeyDelivery((textId, key) => reader.Keys.Store(Gw2KeyStore.TextKind, textId, key), () => { });
        var agents = new GameAgents();
        var equipment = new AgentEquipment();
        var items = new Gw2Items(reader.ContentIndex, ItemContentLayout.Current);
        var namedBeforePickup = new HashSet<uint>();
        foreach (var message in inbound.Messages)
        {
            agents.Handle(message);
            equipment.Handle(message);
            delivery.Handle(message);
            foreach (var item in agents.Keyframed.Where(agent => agent.AgentType == GameAgentType.Item && pickedUp.Contains(agent.AgentId)))
                if (equipment.ItemDataIdOfInstance(item.ContentInstanceId) is { } dataId && items.NameTextIdOf(dataId) is { } textId
                    && reader.ReadKnownText(textId, Gw2Language.English) is { Status: Gw2TextStatus.Plain or Gw2TextStatus.Decrypted, Text.Length: > 0 })
                    namedBeforePickup.Add(item.AgentId);
        }

        Assert.IsTrue(pickedUp.All(namedBeforePickup.Contains), "every picked up item had a name on the ground");
        Assert.IsFalse(agents.Keyframed.Any(agent => pickedUp.Contains(agent.AgentId)), "picked up items are gone");
        Assert.IsTrue(agents.Keyframed.Where(agent => agent.AgentType == GameAgentType.Item).All(agent => agent.ModelFileId is null), "items carry no model");
    }

    [TestMethod]
    public void NamesTheGadgetsThatCarryANameplate()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        var decoded = CaptureDecoding.DecodeAll(RecordedSession.Capture(), RecordedSession.Schemas);
        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var named = new List<string>();
        var unnamed = new List<uint>();
        foreach (var inbound in decoded.Where(result => result.Corpus == "game" && result.Stream.Direction == CaptureRecordKind.Inbound))
        {
            var delivery = new TextKeyDelivery((textId, key) => reader.Keys.Store(Gw2KeyStore.TextKind, textId, key), () => { });
            var agents = new GameAgents();
            var nameplated = new Dictionary<uint, GadgetCreate>();
            foreach (var message in inbound.Messages)
            {
                agents.Handle(message);
                delivery.Handle(message);
            }
            foreach (var gadget in agents.Keyframed.Where(agent => agent.AgentType == GameAgentType.Gadget))
            {
                Assert.IsNotNull(gadget.Gadget, "every gadget create holds the gadget struct");
                if (gadget.Gadget is not { HasNameplate: true, NameIsTemplated: false } create) continue;
                var read = create.NameTextId != 0
                    ? reader.ReadKnownText(create.NameTextId, Gw2Language.English)
                    : CodedNpcName.TryToTextId(create.CodedName, out var textId, out var key) ? reader.ReadText(textId, Gw2Language.English, key) : default;
                if (read.Status is Gw2TextStatus.Plain or Gw2TextStatus.Decrypted && read.Text.Length > 0) named.Add(read.Text);
                else unnamed.Add(gadget.AgentId);
            }
        }

        if (named.Count == 0 && unnamed.Count == 0) Assert.Inconclusive("the capture holds no gadget with a nameplate");
        Assert.AreEqual(0, unnamed.Count, "gadgets without a name: " + string.Join(",", unnamed));
    }
}
