using System.Collections.Generic;
using System;
using System.Linq;
using System.Numerics;
using System.Text;
using Gw2.Client.Net.Game;
using Gw2.Protocol.Capture;
using Gw2.Protocol.MsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public sealed class OwnCharacterMovementTests
{
    private const int AgentField = 1;
    private const int StepField = 2;
    private const int FlagsField = 3;
    private const int SequenceField = 4;
    private const int CodeField = 5;
    private const int MotionField = 6;
    private const int StopMotionField = 4;
    private const int DirectionField = 7;
    private const int FacingField = 8;
    private const int MotionPositionField = 0;
    private const int MotionVelocityField = 2;
    private const int MotionDirectionField = 3;
    private const int MotionCounterField = 5;
    private const int AlreadyMoving = 64;
    private const int ThreeLaneLength = 12;
    private static readonly byte[] MoveCode = [0x6B, 0x01];

    [TestMethod]
    public void BuildsTheRecordedMoveCommandsFromTheirMotion()
    {
        var decoded = CaptureDecoding.DecodeAll(RecordedSession.Capture(), RecordedSession.Schemas);
        var outbound = decoded.FirstOrDefault(result => result.Corpus == "game" && result.Stream.Direction == CaptureRecordKind.Outbound);
        if (outbound is null) Assert.Inconclusive("the capture holds no game connection");
        Assert.IsTrue(RecordedSession.Schemas.Game.Send.TryGet(OwnCharacterMovement.MoveCommandMessageId, out var schema));
        var moves = outbound.Messages.Where(message => message.MessageId == OwnCharacterMovement.MoveCommandMessageId && MoveCode.SequenceEqual((byte[])message.Fields[CodeField].Value!)).ToList();
        Assert.IsTrue(moves.Count > 10, $"{moves.Count} recorded moves");

        var withCounter = 0;
        foreach (var recorded in moves)
        {
            var motion = (DecodedField[])recorded.Fields[MotionField].Value!;
            if ((ulong)motion[MotionCounterField].Value! != 0)
            {
                withCounter++;
                continue;
            }
            var velocity = motion[MotionVelocityField].Present ? VectorOf((byte[])((DecodedField[])motion[MotionVelocityField].Value!)[0].Value!) : (Vector3?)null;
            var built = OwnCharacterMovement.MoveCommandOf(
                (uint)(ulong)recorded.Fields[AgentField].Value!,
                (long)(ulong)recorded.Fields[StepField].Value!,
                (uint)(ulong)recorded.Fields[SequenceField].Value!,
                (ulong)recorded.Fields[FlagsField].Value! == AlreadyMoving,
                VectorOf((byte[])motion[MotionPositionField].Value!),
                velocity,
                OwnCharacterSteps.DirectionInCollisionUnits(VectorOf((byte[])((DecodedField[])motion[MotionDirectionField].Value!)[0].Value!)),
                new OwnMovementInput(1, VectorOf((byte[])recorded.Fields[DirectionField].Value!), VectorOf((byte[])recorded.Fields[FacingField].Value!)));

            MsgPackWriter.WriteMessage(schema, built);
            Assert.AreEqual(Describe(recorded.Fields), Describe(built));
        }
        Assert.IsTrue(withCounter * 5 < moves.Count, $"{withCounter} of {moves.Count} moves carry the undecoded motion counter");
    }

    [TestMethod]
    public void BuildsTheRecordedStopCommandsFromTheirMotion()
    {
        var decoded = CaptureDecoding.DecodeAll(RecordedSession.Capture(), RecordedSession.Schemas);
        var outbound = decoded.FirstOrDefault(result => result.Corpus == "game" && result.Stream.Direction == CaptureRecordKind.Outbound);
        if (outbound is null) Assert.Inconclusive("the capture holds no game connection");
        Assert.IsTrue(RecordedSession.Schemas.Game.Send.TryGet(OwnCharacterMovement.StopCommandMessageId, out var schema));
        var stops = outbound.Messages.Where(message => message.MessageId == OwnCharacterMovement.StopCommandMessageId).ToList();
        if (stops.Count == 0) Assert.Inconclusive("the capture holds no stop");

        foreach (var recorded in stops)
        {
            var motion = (DecodedField[])recorded.Fields[StopMotionField].Value!;
            if ((ulong)motion[MotionCounterField].Value! != 0) continue;
            var velocity = motion[MotionVelocityField].Present ? VectorOf((byte[])((DecodedField[])motion[MotionVelocityField].Value!)[0].Value!) : (Vector3?)null;
            var built = OwnCharacterMovement.StopCommandOf(
                (uint)(ulong)recorded.Fields[AgentField].Value!,
                (long)(ulong)recorded.Fields[StepField].Value!,
                (ulong)recorded.Fields[FlagsField].Value! == AlreadyMoving,
                VectorOf((byte[])motion[MotionPositionField].Value!),
                velocity,
                motion[MotionDirectionField].Present ? OwnCharacterSteps.DirectionInCollisionUnits(VectorOf((byte[])((DecodedField[])motion[MotionDirectionField].Value!)[0].Value!)) : null);

            MsgPackWriter.WriteMessage(schema, built);
            Assert.AreEqual(Describe(recorded.Fields), Describe(built));
        }
    }

    [TestMethod]
    public void BuildsTheRecordedDodgesAndTheMotionStateTheyEndWith()
    {
        var decoded = CaptureDecoding.DecodeAll(RecordedSession.Capture(), RecordedSession.Schemas);
        var outbound = decoded.FirstOrDefault(result => result.Corpus == "game" && result.Stream.Direction == CaptureRecordKind.Outbound);
        if (outbound is null) Assert.Inconclusive("the capture holds no game connection");
        Assert.IsTrue(RecordedSession.Schemas.Game.Send.TryGet(OwnCharacterMovement.MoveCommandMessageId, out var moveSchema));
        Assert.IsTrue(RecordedSession.Schemas.Game.Send.TryGet(OwnCharacterMovement.MotionStateMessageId, out var motionStateSchema));
        var messages = outbound.Messages.ToList();
        var dodges = messages.Select((message, index) => (message, index)).Where(entry => entry.message.MessageId == OwnCharacterMovement.MoveCommandMessageId && ((byte[])entry.message.Fields[CodeField].Value!)[0] == DodgeCodeLead).ToList();
        if (dodges.Count == 0) Assert.Inconclusive("the capture holds no dodge");

        foreach (var (recorded, index) in dodges)
        {
            var motion = (DecodedField[])recorded.Fields[MotionField].Value!;
            var built = OwnCharacterMovement.DodgeCommandOf(
                (uint)(ulong)recorded.Fields[AgentField].Value!,
                (long)(ulong)recorded.Fields[StepField].Value!,
                (uint)(ulong)recorded.Fields[SequenceField].Value!,
                ((byte[])recorded.Fields[CodeField].Value!)[2],
                VectorOf((byte[])motion[MotionPositionField].Value!),
                NestedVectorOf(motion[MotionVelocityField]),
                NestedVectorOf(motion[MotionDirectionField]) is { } direction ? OwnCharacterSteps.DirectionInCollisionUnits(direction) : null,
                VectorOf((byte[])recorded.Fields[DirectionField].Value!),
                VectorOf((byte[])recorded.Fields[FacingField].Value!));
            MsgPackWriter.WriteMessage(moveSchema, built);
            Assert.AreEqual(DescribeWithoutCounter(recorded.Fields), DescribeWithoutCounter(built));

            var dodgeStep = (long)(ulong)recorded.Fields[StepField].Value!;
            var end = messages.Skip(index + 1).FirstOrDefault(message => message.MessageId == OwnCharacterMovement.MotionStateMessageId);
            if (end is null) continue;
            Assert.AreEqual(dodgeStep + OwnCharacterMovement.DodgeSteps, (long)(ulong)end.Fields[StepField].Value!);
            var velocity = VectorOf((byte[])end.Fields[MotionStateVelocityField].Value!);
            Assert.AreEqual(OwnCharacterMovement.DodgeSpeedInGameUnits / GameAgents.GameUnitsPerCollisionUnit, velocity.Length(), 0.05f);
            var dodgeDirection = VectorOf((byte[])recorded.Fields[DirectionField].Value!);
            Assert.AreEqual(MathF.Atan2(dodgeDirection.Y, dodgeDirection.X), MathF.Atan2(velocity.Y, velocity.X), 0.01f);
            var resumed = messages[messages.IndexOf(end) + 1];
            Assert.AreEqual(OwnCharacterMovement.MoveCommandMessageId, resumed.MessageId);
            var builtEnd = OwnCharacterMovement.MotionStateOf(
                (uint)(ulong)end.Fields[AgentField].Value!,
                (long)(ulong)end.Fields[StepField].Value!,
                VectorOf((byte[])end.Fields[MotionStatePositionField].Value!),
                velocity,
                VectorOf((byte[])resumed.Fields[DirectionField].Value!));
            MsgPackWriter.WriteMessage(motionStateSchema, builtEnd);
            Assert.AreEqual(DescribeDirection(end.Fields), DescribeDirection(builtEnd), 0.002f);
            Assert.AreEqual(0UL, (ulong)resumed.Fields[FlagsField].Value!);
        }
    }

    [TestMethod]
    public void SendsTheRecordedMotionStateOneStepAfterEachLanding()
    {
        var decoded = CaptureDecoding.DecodeAll(RecordedSession.Capture(), RecordedSession.Schemas);
        var outbound = decoded.FirstOrDefault(result => result.Corpus == "game" && result.Stream.Direction == CaptureRecordKind.Outbound);
        if (outbound is null) Assert.Inconclusive("the capture holds no game connection");
        var positions = new Dictionary<long, Vector3>();
        var airborneSteps = new HashSet<long>();
        var landings = 0;
        foreach (var message in outbound.Messages)
        {
            if (message.MessageId == 0x12)
            {
                var step = (long)(ulong)message.Fields[StepField].Value!;
                positions[step] = VectorOf((byte[])message.Fields[3].Value!);
                if ((ulong)message.Fields[6].Value! != 0) airborneSteps.Add(step);
            }
            if (message.MessageId != OwnCharacterMovement.MotionStateMessageId) continue;
            var motionStep = (long)(ulong)message.Fields[StepField].Value!;
            var landingStep = motionStep - 1;
            if (!airborneSteps.Contains(landingStep - 1) || airborneSteps.Contains(landingStep)) continue;
            if (!positions.TryGetValue(landingStep, out var landed) || !positions.TryGetValue(landingStep - 1, out var beforeLanding)) continue;
            landings++;
            var expectedVelocity = (landed - beforeLanding) / (float)OwnCharacterSteps.StepLength.TotalSeconds;
            var recordedVelocity = VectorOf((byte[])message.Fields[MotionStateVelocityField].Value!);
            Assert.AreEqual(0f, Vector3.Distance(landed, VectorOf((byte[])message.Fields[MotionStatePositionField].Value!)), 0.001f);
            Assert.AreEqual(0f, Vector3.Distance(expectedVelocity, recordedVelocity), 0.05f);
        }
        if (landings == 0) Assert.Inconclusive("the capture holds no landing");
    }

    [TestMethod]
    public void BuildsTheRecordedTargetSelections()
    {
        var decoded = CaptureDecoding.DecodeAll(RecordedSession.Capture(), RecordedSession.Schemas);
        var outbound = decoded.FirstOrDefault(result => result.Corpus == "game" && result.Stream.Direction == CaptureRecordKind.Outbound);
        if (outbound is null) Assert.Inconclusive("the capture holds no game connection");
        Assert.IsTrue(RecordedSession.Schemas.Game.Send.TryGet(OwnCharacterMovement.AgentCommandMessageId, out var schema));
        var selections = outbound.Messages.Where(message => message.MessageId == OwnCharacterMovement.AgentCommandMessageId && ((byte[])message.Fields[5].Value!)[0] == OwnCharacterMovement.TargetSelectionLead).ToList();
        if (selections.Count == 0) Assert.Inconclusive("the capture holds no target selection");
        foreach (var recorded in selections)
        {
            var payload = (byte[])recorded.Fields[5].Value!;
            var built = OwnCharacterMovement.TargetSelectionCommandOf(
                (uint)(ulong)recorded.Fields[AgentField].Value!,
                (long)(ulong)recorded.Fields[StepField].Value!,
                (uint)(ulong)recorded.Fields[4].Value!,
                BitConverter.ToUInt32(payload, 1));
            MsgPackWriter.WriteMessage(schema, built);
            Assert.AreEqual(Convert.ToHexString(MsgPackWriter.WriteMessage(schema, recorded.Fields)), Convert.ToHexString(MsgPackWriter.WriteMessage(schema, built)));
        }
    }

    [TestMethod]
    public void BuildsTheRecordedSkillSlotsAndSkillUses()
    {
        var decoded = CaptureDecoding.DecodeAll(RecordedSession.Capture(), RecordedSession.Schemas);
        var outbound = decoded.FirstOrDefault(result => result.Corpus == "game" && result.Stream.Direction == CaptureRecordKind.Outbound);
        if (outbound is null) Assert.Inconclusive("the capture holds no game connection");
        Assert.IsTrue(RecordedSession.Schemas.Game.Send.TryGet(OwnCharacterMovement.AgentCommandMessageId, out var schema));
        var commands = outbound.Messages.Where(message => message.MessageId == OwnCharacterMovement.AgentCommandMessageId).ToList();
        int built = 0;
        foreach (var recorded in commands)
        {
            var payload = (byte[])recorded.Fields[5].Value!;
            uint agent = (uint)(ulong)recorded.Fields[AgentField].Value!;
            long step = (long)(ulong)recorded.Fields[StepField].Value!;
            uint sequence = (uint)(ulong)recorded.Fields[4].Value!;
            DecodedField[]? command = payload[0] switch
            {
                OwnCharacterMovement.SkillSlotPressedLead or OwnCharacterMovement.SkillSlotReleasedLead when payload.Length == 2 =>
                    OwnCharacterMovement.SkillSlotCommandOf(agent, step, sequence, payload[0], payload[1]),
                OwnCharacterMovement.SkillUseLead when payload.Length == 10 =>
                    OwnCharacterMovement.SkillUseCommandOf(agent, step, sequence, BitConverter.ToUInt32(payload, 1), BitConverter.ToUInt32(payload, 5), payload[9]),
                _ => null,
            };
            if (command is null) continue;
            built++;
            Assert.AreEqual(Convert.ToHexString(MsgPackWriter.WriteMessage(schema, recorded.Fields)), Convert.ToHexString(MsgPackWriter.WriteMessage(schema, command)));
        }
        if (built == 0) Assert.Inconclusive("the capture holds no skill use");
    }

    [TestMethod]
    public void SwapsLegendsOnTheOwnAgentAndUsesTheLegendSkillUntargetedAsRecorded()
    {
        var movement = new OwnCharacterMovement();
        var legendSkill = movement.CommandsToSend(6550, 23587, 0, new OwnMovementInput(0, Vector3.Zero, Vector3.UnitX, SkillUse: new OwnSkillUse(1, 13, 29371, 0)), Vector3.Zero, 9f);
        CollectionAssert.AreEqual(new[] { "2A0D", "2300000000BB7200000D" }, legendSkill.Select(command => Convert.ToHexString((byte[])command[5].Value!)).ToArray());
        CollectionAssert.AreEqual(new ulong[] { 0x80000000, 0x80000002 }, legendSkill.Select(command => (ulong)command[4].Value!).ToArray());

        var swap = new OwnCharacterMovement().CommandsToSend(6550, 23719, 0, new OwnMovementInput(0, Vector3.Zero, Vector3.UnitX, SkillUse: new OwnSkillUse(2, 12, 28134, 6550, ReleasesAtOnce: true)), Vector3.Zero, 9f);
        var payloads = swap.Select(command => Convert.ToHexString((byte[])command[5].Value!)).ToArray();
        Assert.AreEqual(4, payloads.Length);
        Assert.AreEqual("2A0C", payloads[0]);
        StringAssert.StartsWith(payloads[1], "2B96190000");
        Assert.AreEqual("2396190000E66D00000C", payloads[2]);
        Assert.AreEqual("290C", payloads[3]);
        CollectionAssert.AreEqual(new ulong[] { 0x80000000, 0x80000001, 0x80000003, 0x80000004 }, swap.Select(command => (ulong)command[4].Value!).ToArray());
    }

    [TestMethod]
    public void PressesUsesAndReleasesASkillSlot()
    {
        var movement = new OwnCharacterMovement();
        var using_ = new OwnMovementInput(0, Vector3.Zero, Vector3.UnitX, SkillUse: new OwnSkillUse(1, 5, 41253, 0));
        var leads = new List<byte>();
        for (long step = 100; step <= 100 + OwnCharacterMovement.SkillSlotHoldSteps; step++)
            leads.AddRange(movement.CommandsToSend(1, step, (step - 100) * 40, using_, Vector3.Zero, 9f)
                .Where(command => (int)(ulong)command[0].Value! == OwnCharacterMovement.AgentCommandMessageId)
                .Select(command => ((byte[])command[5].Value!)[0]));
        CollectionAssert.AreEqual(new[] { OwnCharacterMovement.SkillSlotPressedLead, OwnCharacterMovement.SkillUseLead, OwnCharacterMovement.SkillSlotReleasedLead }, leads);
    }

    [TestMethod]
    public void DodgesFromRestWithFlagsThreeAndEndsWithOnlyAMotionStateWhenNoKeysAreHeld()
    {
        var movement = new OwnCharacterMovement();
        var dodging = new OwnMovementInput(0, Vector3.Zero, Vector3.UnitX, Dodge: new OwnDodge(1, 0, -Vector3.UnitX));
        var start = movement.CommandsToSend(1, 100, 0, dodging, Vector3.Zero, 9f).Single();
        Assert.AreEqual(OwnCharacterMovement.MoveCommandMessageId, (int)(ulong)start[0].Value!);
        Assert.AreEqual(3UL, (ulong)start[3].Value!);
        for (long step = 101; step < 100 + OwnCharacterMovement.DodgeSteps; step++)
            Assert.AreEqual(0, movement.CommandsToSend(1, step, (step - 100) * 40, dodging, Vector3.Zero, 9f).Count);
        var end = movement.CommandsToSend(1, 100 + OwnCharacterMovement.DodgeSteps, 800, dodging, Vector3.Zero, 9f);
        Assert.AreEqual(1, end.Count);
        Assert.AreEqual(OwnCharacterMovement.MotionStateMessageId, (int)(ulong)end[0][0].Value!);
        CollectionAssert.AreEqual(new byte[16], (byte[])end[0][7].Value!);
    }

    [TestMethod]
    public void EngagesTheTargetAndNamesItBeforeATargetedSkillUseAsRecorded()
    {
        var movement = new OwnCharacterMovement();
        var use = new OwnMovementInput(0, Vector3.Zero, Vector3.UnitX, SkillUse: new OwnSkillUse(1, 5, 29057, 5254));
        var first = movement.CommandsToSend(6550, 739, 0, use, Vector3.Zero, 9f);
        Assert.AreEqual(4, first.Count);
        Assert.AreEqual(OwnCharacterMovement.EngagedTargetMessageId, (int)(ulong)first[0][0].Value!);
        Assert.AreEqual(5254UL, (ulong)first[0][1].Value!);
        Assert.AreEqual("2A05", Convert.ToHexString((byte[])first[1][5].Value!));
        Assert.AreEqual("2B861400000000807F0000807F0000807F000000000000807F0000807F0000807F00000000", Convert.ToHexString((byte[])first[2][5].Value!));
        Assert.AreEqual("23861400008171000005", Convert.ToHexString((byte[])first[3][5].Value!));
        Assert.AreEqual(2UL, (ulong)first[3][4].Value! - (ulong)first[2][4].Value!);
        var again = movement.CommandsToSend(6550, 779, 0, use with { SkillUse = new OwnSkillUse(2, 5, 29057, 5254) }, Vector3.Zero, 9f);
        Assert.IsFalse(again.Any(command => (int)(ulong)command[0].Value! == OwnCharacterMovement.EngagedTargetMessageId));
    }

    [TestMethod]
    public void UsesAGroundTargetedSkillAtThePointAsRecorded()
    {
        var movement = new OwnCharacterMovement();
        var point = new Vector3(BitConverter.ToSingle(Convert.FromHexString("FE969846")), BitConverter.ToSingle(Convert.FromHexString("488CA5C6")), BitConverter.ToSingle(Convert.FromHexString("D4FFC7C2")));
        var use = new OwnMovementInput(0, Vector3.Zero, Vector3.UnitX, SkillUse: new OwnSkillUse(1, 9, 28110, 0, point));
        var commands = movement.CommandsToSend(4867, 801, 0, use, Vector3.Zero, 9f);
        CollectionAssert.AreEqual(
            new[] { "2A09", "2B00000000FE969846488CA5C6D4FFC7C2000000000000807F0000807F0000807F00000000", "25FE969846488CA5C6D4FFC7C200000000CE6D000009", "2B00000000FE969846488CA5C6D4FFC7C2000000000000807F0000807F0000807F00000000", "2800000000" },
            commands.Select(command => Convert.ToHexString((byte[])command[5].Value!)).ToArray());
        CollectionAssert.AreEqual(new ulong[] { 3, 3, 3, 3, 0 }, commands.Select(command => (ulong)command[3].Value!).ToArray());
        CollectionAssert.AreEqual(new ulong[] { 0, 1, 3, 4, 5 }, commands.Select(command => (ulong)command[4].Value! - (ulong)commands[0][4].Value!).ToArray());
    }

    [TestMethod]
    public void NamesTheHeldReticleEveryFiveStepsAndUpdatesItBetweenAsRecorded()
    {
        var movement = new OwnCharacterMovement();
        var cast = new Vector3(36029.77f, -3674.86f, -1333.99f);
        var held = new OwnMovementInput(0, Vector3.Zero, Vector3.UnitX, SkillUse: new OwnSkillUse(1, 7, 72938, 0, cast), HeldGroundReticleInGameUnits: cast);
        var press = movement.CommandsToSend(6550, 48282, 0, held, Vector3.Zero, 9f);
        CollectionAssert.AreEqual(new[] { "2A", "2B", "25", "2B", "28", "2B" }, press.Select(command => Convert.ToHexString((byte[])command[5].Value!)[..2]).ToArray());
        CollectionAssert.AreEqual(new ulong[] { 0, 1, 3, 4, 5, 7 }, press.Select(command => (ulong)command[4].Value! - (ulong)press[0][4].Value!).ToArray());
        var leads = new List<(long Step, string Lead)>();
        for (long step = 48283; step <= 48308; step++)
        {
            var reticle = step < 48298 ? cast + new Vector3(step - 48282, 0f, 0f) : cast + new Vector3(15f, 0f, 0f);
            var input = held with { HeldGroundReticleInGameUnits = step < 48308 ? reticle : null };
            foreach (var command in movement.CommandsToSend(6550, step, 0, input, Vector3.Zero, 9f))
                leads.Add((step, Convert.ToHexString((byte[])command[5].Value!)[..2]));
        }
        CollectionAssert.AreEqual(
            new (long, string)[] { (48284, "2C"), (48286, "2C"), (48287, "2B"), (48289, "2C"), (48291, "2C"), (48292, "2B"), (48294, "2C"), (48296, "2C"), (48297, "2B"), (48302, "2B"), (48307, "2B"), (48308, "29") },
            leads);
    }

    [TestMethod]
    public void SwapsWeaponsWithTheSingleByteCommand()
    {
        var movement = new OwnCharacterMovement();
        var swap = movement.CommandsToSend(6550, 664, 0, new OwnMovementInput(0, Vector3.Zero, Vector3.UnitX, WeaponSwapNumber: 1), Vector3.Zero, 9f).Single();
        Assert.AreEqual("0A", Convert.ToHexString((byte[])swap[5].Value!));
        Assert.AreEqual(0, movement.CommandsToSend(6550, 665, 0, new OwnMovementInput(0, Vector3.Zero, Vector3.UnitX, WeaponSwapNumber: 1), Vector3.Zero, 9f).Count);
    }

    [TestMethod]
    public void SendsATargetSelectionOncePerSelection()
    {
        var movement = new OwnCharacterMovement();
        var selecting = new OwnMovementInput(0, Vector3.Zero, Vector3.UnitX, Target: new OwnTargetSelection(1, 11082));
        Assert.AreEqual(1, movement.CommandsToSend(1, 100, 0, selecting, Vector3.Zero, 9f).Count(command => (int)(ulong)command[0].Value! == OwnCharacterMovement.AgentCommandMessageId));
        Assert.AreEqual(0, movement.CommandsToSend(1, 101, 40, selecting, Vector3.Zero, 9f).Count(command => (int)(ulong)command[0].Value! == OwnCharacterMovement.AgentCommandMessageId));
    }

    [TestMethod]
    public void SendsAMotionStateOneStepAfterLanding()
    {
        var movement = new OwnCharacterMovement();
        var walking = new OwnMovementInput(1, Vector3.UnitX, Vector3.UnitX);
        var airborne = walking with { IsAirborne = true };
        movement.CommandsToSend(1, 100, 0, airborne, new Vector3(0f, 0f, 2f), 9f);
        movement.CommandsToSend(1, 101, 40, airborne, new Vector3(0.4f, 0f, 1f), 9f);
        Assert.IsFalse(movement.CommandsToSend(1, 102, 80, walking, new Vector3(0.8f, 0f, 0f), 9f).Any(IsMotionState));
        var afterLanding = movement.CommandsToSend(1, 103, 120, walking, new Vector3(1.2f, 0f, 0f), 9f);
        var motionState = afterLanding.Single(IsMotionState);
        Assert.AreEqual(103UL, (ulong)motionState[2].Value!);
        Assert.IsFalse(movement.CommandsToSend(1, 104, 160, walking, new Vector3(1.6f, 0f, 0f), 9f).Any(IsMotionState));
    }

    private static bool IsMotionState(DecodedField[] command) => (int)(ulong)command[0].Value! == OwnCharacterMovement.MotionStateMessageId;

    [TestMethod]
    public void SendsTheDodgeThenNothingUntilItsMotionStateAndAMoveFromRest()
    {
        var movement = new OwnCharacterMovement();
        var walking = new OwnMovementInput(1, Vector3.UnitX, Vector3.UnitX);
        var dodging = walking with { Dodge = new OwnDodge(1, 3, Vector3.UnitX) };
        var position = Vector3.Zero;
        Assert.AreEqual(OwnCharacterMovement.MoveCommandMessageId, (int)(ulong)movement.CommandsToSend(1, 100, 0, walking, position, 9f).Single()[0].Value!);
        var dodge = movement.CommandsToSend(1, 101, 40, dodging, position, 9f).Single();
        Assert.AreEqual("6C0003", Convert.ToHexString((byte[])dodge[CodeField].Value!));
        for (var step = 102L; step < 101 + OwnCharacterMovement.DodgeSteps; step++)
            Assert.AreEqual(0, movement.CommandsToSend(1, step, step * 40, dodging, position += Vector3.UnitX * 0.46f, 9f).Count);
        var end = movement.CommandsToSend(1, 101 + OwnCharacterMovement.DodgeSteps, 840, walking, position, 9f);
        Assert.AreEqual(OwnCharacterMovement.MotionStateMessageId, (int)(ulong)end[0][0].Value!);
        Assert.AreEqual(OwnCharacterMovement.MoveCommandMessageId, (int)(ulong)end[1][0].Value!);
        Assert.AreEqual(0UL, (ulong)end[1][FlagsField].Value!);
    }

    private const byte DodgeCodeLead = 0x6C;
    private const int MotionStatePositionField = 4;
    private const int MotionStateVelocityField = 6;
    private const int MotionStateDirectionField = 7;

    private static Vector3? NestedVectorOf(DecodedField field) => field.Present ? VectorOf((byte[])((DecodedField[])field.Value!)[0].Value!) : null;

    private static float DescribeDirection(System.Collections.Generic.IReadOnlyList<DecodedField> fields)
    {
        var direction = VectorOf((byte[])fields[MotionStateDirectionField].Value!);
        return MathF.Atan2(direction.Y, direction.X);
    }

    private static string DescribeWithoutCounter(System.Collections.Generic.IReadOnlyList<DecodedField> fields)
    {
        var motion = (DecodedField[])fields[MotionField].Value!;
        var withoutCounter = motion.Take(MotionCounterField).ToArray();
        return Describe(fields.Take(MotionField).ToArray()) + Describe(withoutCounter) + Describe(fields.Skip(MotionField + 1).ToArray());
    }

    private static Vector3 VectorOf(byte[] lanes) => new(BitConverter.ToSingle(lanes, 0), BitConverter.ToSingle(lanes, 4), BitConverter.ToSingle(lanes, 8));

    private static string Describe(System.Collections.Generic.IReadOnlyList<DecodedField> fields)
    {
        var text = new StringBuilder();
        foreach (var field in fields)
        {
            text.Append(field.Value switch
            {
                _ when !field.Present => "-",
                DecodedField[] nested => "{" + Describe(nested) + "}",
                byte[] bytes => Convert.ToHexString(bytes, 0, Math.Min(bytes.Length, ThreeLaneLength)),
                var value => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture),
            });
            text.Append(' ');
        }
        return text.ToString();
    }
}
