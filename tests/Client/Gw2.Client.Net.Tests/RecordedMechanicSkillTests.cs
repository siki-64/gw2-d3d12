using System;
using System.Collections.Generic;
using System.Linq;
using Gw2.Client.Net.Game;
using Gw2.Dat.Content;
using Gw2.Formats;
using Gw2.Protocol.Capture;
using Gw2.Protocol.Framing;
using Gw2.Protocol.MsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public sealed class RecordedMechanicSkillTests
{
    private const int SkillCommandMessageId = 0x17;
    private const int CommandAgentField = 1;
    private const int CommandPayloadField = 5;
    private const byte SkillUseCommand = 0x23;
    private const int SkillUseLength = 10;
    private const int SkillIdOffset = 5;
    private const int SlotOffset = 9;
    private const byte GroundSkillUseCommand = 0x25;
    private const int GroundSkillUseLength = 22;
    private const int GroundSkillIdOffset = 17;
    private const int GroundSlotOffset = 21;
    private const byte FirstMechanicSlot = 12;
    private const byte LastMechanicSlot = 16;

    private static readonly HashSet<uint> SkillsNotFromThePalettes = new()
    {
        1146,
        1148,
        76816,
    };

    [TestMethod]
    public void ResolvesEveryRecordedMechanicSkill()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        var capture = RecordedSession.Capture();
        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var palettes = new Gw2SkillPalettes(reader.ContentIndex, SkillPaletteContentLayout.Current);
        var ownSkills = new Gw2OwnSkills(palettes, new Gw2Items(reader.ContentIndex, ItemContentLayout.Current), LegendSkillPaletteLayout.Current, ProfessionMechanicPaletteLayout.Current);
        var equipment = new AgentEquipment();
        var skillbar = new OwnSkillbar();
        var buffs = new AgentBuffs();
        var uses = 0;
        var mismatches = new List<string>();

        foreach (var (outbound, message) in GameMessagesInTimeOrder(capture))
        {
            if (!outbound)
            {
                equipment.Handle(message);
                skillbar.Handle(message);
                buffs.Handle(message, 0);
                continue;
            }
            if (message.MessageId != SkillCommandMessageId || message.Fields[CommandPayloadField].Value is not byte[] payload) continue;
            if (SkillUseOf(payload) is not var (skill, slot)) continue;
            if (slot < FirstMechanicSlot || slot > LastMechanicSlot) continue;
            uint agent = Convert.ToUInt32(message.Fields[CommandAgentField].Value);
            uint recorded = skill;
            if (equipment.ProfessionOf(agent) is not { } profession) { mismatches.Add($"agent {agent}: no profession"); continue; }
            var (mainHand, offHand) = equipment.ActiveWeaponItemDataIdsOf(agent);
            var state = new OwnSkillState((Profession)profession, mainHand, offHand, skillbar.ProfessionStateOf(agent), skillbar.InactiveLegendOf(agent),
                skillbar.SpecializationsOf(agent), skillbar.TraitsOf(agent).ToArray(), buffs.SkillIdsOf(agent), index => skillbar.SlotPaletteOf(agent, index), skillbar.ActiveCompanionOf(agent), skill => skillbar.OverriddenSkillOf(agent, skill));
            uses++;
            var resolved = ownSkills.MechanicSkill(state, slot - FirstMechanicSlot);
            if (resolved != recorded && !SkillsNotFromThePalettes.Contains(recorded))
                mismatches.Add($"{(Profession)profession} elite {string.Join("/", state.Specializations)} state {state.ProfessionState} F{slot - FirstMechanicSlot + 1}: recorded {recorded}, resolved {resolved?.ToString() ?? "none"}");
        }

        if (uses == 0) Assert.Inconclusive("the capture holds no mechanic skill use");
        Assert.AreEqual(0, mismatches.Count, $"{mismatches.Count} of {uses} uses differ:\n{string.Join("\n", mismatches)}");
    }

    private static (uint Skill, byte Slot)? SkillUseOf(byte[] payload) => payload switch
    {
        [SkillUseCommand, ..] when payload.Length == SkillUseLength => (BitConverter.ToUInt32(payload, SkillIdOffset), payload[SlotOffset]),
        [GroundSkillUseCommand, ..] when payload.Length == GroundSkillUseLength => (BitConverter.ToUInt32(payload, GroundSkillIdOffset), payload[GroundSlotOffset]),
        _ => null,
    };

    private static IEnumerable<(bool Outbound, DecodedMessage Message)> GameMessagesInTimeOrder(NetworkCapture capture) =>
        CaptureDecoding.DecodeAll(capture, RecordedSession.Schemas)
            .Where(result => result.Corpus == "game")
            .SelectMany(result =>
            {
                bool outbound = result.Stream.Direction == CaptureRecordKind.Outbound;
                var frameStarts = outbound ? null : TransportFrame.FrameStarts(result.Stream.Bytes);
                return result.Messages.Select(message => (
                    Timestamp: result.Stream.TimestampOfChunk(result.Stream.ChunkAt(frameStarts is null ? message.WireOffset : TransportFrame.RawOffsetOf(frameStarts, message.WireOffset))),
                    Outbound: outbound,
                    Message: message));
            })
            .OrderBy(entry => entry.Timestamp)
            .Select(entry => (entry.Outbound, entry.Message));
}
