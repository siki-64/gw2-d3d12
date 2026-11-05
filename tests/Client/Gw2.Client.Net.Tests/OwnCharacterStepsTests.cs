using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Gw2.Client.Net.Game;
using Gw2.Protocol.Capture;
using Gw2.Protocol.MsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public sealed class OwnCharacterStepsTests
{
    private const int StepField = 1;
    private const int PositionField = 3;
    private const int MotionFlagsField = 5;
    private const int StateField = 6;
    private const int ComparedFieldCount = 6;
    private const int OtherCommandStepField = 2;
    private static readonly int[] OtherOwnMovementCommands = [0x18, 0x1a];

    [TestMethod]
    public void SendsTheRecordedStepsWhileWalkingOnTheGround()
    {
        var (recorded, otherCommandSteps) = RecordedStates();
        var byStep = recorded.ToDictionary(message => StepOf(message));
        var steps = new OwnCharacterSteps();
        var walkedSteps = 0;
        Vector3? position = null;
        Vector3? lastSentPosition = null;
        var stopRepeats = 0;
        for (var step = byStep.Keys.Min(); step <= byStep.Keys.Max(); step++)
        {
            var recordedMessage = byStep.GetValueOrDefault(step);
            if (otherCommandSteps.Contains(step) || recordedMessage is not null && !IsOnGround(recordedMessage))
            {
                steps = new OwnCharacterSteps();
                position = null;
                continue;
            }
            if (recordedMessage is not null) position = PositionOf(recordedMessage);
            if (position is not { } current) continue;
            var sent = steps.StateToSend(step, current);
            if (recordedMessage is null)
            {
                if (sent is null) continue;
                Assert.AreEqual(lastSentPosition, current, $"step {step} sends without moving only to repeat a stop");
                stopRepeats++;
                continue;
            }
            Assert.IsNotNull(sent, $"step {step}");
            lastSentPosition = current;
            walkedSteps++;
            for (var field = 1; field <= ComparedFieldCount; field++)
                Assert.AreEqual(Comparable(recordedMessage!.Fields[field].Value), Comparable(sent[field - 1]), $"step {step} field {field}");
        }
        Assert.IsTrue(walkedSteps > 100, $"{walkedSteps} walked steps");
        Assert.IsTrue(stopRepeats * 20 < walkedSteps, $"{stopRepeats} stop repeats in {walkedSteps} steps");
    }

    [TestMethod]
    public void ReportsTheRecordedTakeOffAirAndLandingStatesOfAJump()
    {
        var (recorded, _) = RecordedStates();
        var jumps = 0;
        var earlyAirborneFlags = 0;
        for (var index = 1; index < recorded.Count; index++)
        {
            if (!IsTakeOff(recorded[index]) || !IsOnGround(recorded[index - 1]) || StepOf(recorded[index]) != StepOf(recorded[index - 1]) + 1) continue;
            var landing = index + 1;
            while (landing < recorded.Count && IsInPlainAir(recorded[landing]) && StepOf(recorded[landing]) == StepOf(recorded[landing - 1]) + 1) landing++;
            if (landing >= recorded.Count || !IsOnGround(recorded[landing]) || StepOf(recorded[landing]) != StepOf(recorded[landing - 1]) + 1) continue;
            var steps = new OwnCharacterSteps();
            steps.StateToSend(StepOf(recorded[index - 1]), PositionOf(recorded[index - 1]));
            for (var air = index; air <= landing; air++)
            {
                var sent = steps.StateToSend(StepOf(recorded[air]), PositionOf(recorded[air]), air < landing);
                Assert.IsNotNull(sent, $"step {StepOf(recorded[air])}");
                var motionFlagsMatch = Comparable(recorded[air].Fields[MotionFlagsField].Value) == Comparable(sent[MotionFlagsField - 1]);
                if (!motionFlagsMatch && air == index + 1) earlyAirborneFlags++;
                else Assert.IsTrue(motionFlagsMatch, $"step {StepOf(recorded[air])} motion flags");
                Assert.AreEqual(Comparable(recorded[air].Fields[StateField].Value), Comparable(sent[StateField - 1]), $"step {StepOf(recorded[air])} state");
            }
            jumps++;
        }
        if (jumps == 0) Assert.Inconclusive("the capture holds no jump");
        Assert.IsTrue(earlyAirborneFlags * 5 < jumps, $"{earlyAirborneFlags} of {jumps} jumps flag the first air step");
    }

    private const ulong TakingOffState = 2;
    private const ulong InAirState = 1;

    private static bool IsTakeOff(DecodedMessage message) =>
        (ulong)message.Fields[MotionFlagsField].Value! == 0 && (ulong)message.Fields[StateField].Value! == TakingOffState;

    private static bool IsInPlainAir(DecodedMessage message) => (ulong)message.Fields[StateField].Value! == InAirState;

    private static (List<DecodedMessage> States, HashSet<long> OtherCommandSteps) RecordedStates()
    {
        var decoded = CaptureDecoding.DecodeAll(RecordedSession.Capture(), RecordedSession.Schemas);
        var outbound = decoded.FirstOrDefault(result => result.Corpus == "game" && result.Stream.Direction == CaptureRecordKind.Outbound);
        if (outbound is null) Assert.Inconclusive("the capture holds no game connection");
        var otherCommandSteps = outbound.Messages.Where(message => OtherOwnMovementCommands.Contains(message.MessageId))
            .Select(message => (long)(ulong)message.Fields[OtherCommandStepField].Value!).ToHashSet();
        return (outbound.Messages.Where(message => message.MessageId == GameMessageIds.OwnCharacterState).ToList(), otherCommandSteps);
    }

    private static long StepOf(DecodedMessage message) => (long)(ulong)message.Fields[StepField].Value!;

    private static bool IsOnGround(DecodedMessage message) =>
        (ulong)message.Fields[MotionFlagsField].Value! == 0 && (ulong)message.Fields[StateField].Value! == 0;

    private static Vector3 PositionOf(DecodedMessage message)
    {
        var bytes = (byte[])message.Fields[PositionField].Value!;
        return new Vector3(BitConverter.ToSingle(bytes, 0), BitConverter.ToSingle(bytes, 4), BitConverter.ToSingle(bytes, 8));
    }

    private static string Comparable(object? value) => value switch
    {
        byte[] bytes => Convert.ToHexString(bytes),
        uint number => number.ToString(),
        byte number => number.ToString(),
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "",
    };

    [TestMethod]
    public void AppliesTheServersControlAndReportsTheStartingMotionLikeTheRecordedClient()
    {
        var decoded = CaptureDecoding.DecodeAll(RecordedSession.Capture(), RecordedSession.Schemas);
        var inbound = decoded.FirstOrDefault(result => result.Corpus == "game" && result.Stream.Direction == CaptureRecordKind.Inbound);
        var outbound = decoded.FirstOrDefault(result => result.Corpus == "game" && result.Stream.Direction == CaptureRecordKind.Outbound);
        if (inbound is null || outbound is null) Assert.Inconclusive("the capture holds no game connection");
        var recordedControlApplied = outbound.Messages.First(message => message.MessageId == OwnCharacterControl.ControlAppliedMessageId);
        var recordedMotionState = outbound.Messages.First(message => message.MessageId == OwnCharacterControl.MotionStateMessageId);
        var recordedFirstState = outbound.Messages.First(message => message.MessageId == GameMessageIds.OwnCharacterState);

        var control = new OwnCharacterControl();
        control.Handle(inbound.Messages.First(message => message.MessageId == OwnCharacterControl.ControlMessageId));
        var firstStep = (long)(ulong)recordedControlApplied.Fields[2].Value!;
        var position = PositionOf(recordedFirstState);
        var first = control.MessagesForStep(firstStep, position);
        var second = control.MessagesForStep(firstStep + 1, position);

        Assert.AreEqual(OwnCharacterControl.ControlAppliedMessageId, first[0].MessageId);
        AssertSameFields(recordedControlApplied, first[0].Values);
        Assert.AreEqual(OwnCharacterControl.MotionStateMessageId, second[0].MessageId);
        AssertSameFields(recordedMotionState, second[0].Values);
        Assert.AreEqual(GameMessageIds.OwnCharacterState, first[^1].MessageId);
        Assert.AreEqual(GameMessageIds.OwnCharacterState, second[^1].MessageId);
    }

    private static void AssertSameFields(DecodedMessage recorded, object[] values)
    {
        Assert.AreEqual(recorded.Fields.Count - 1, values.Length, $"0x{recorded.MessageId:x} field count");
        for (var field = 1; field < recorded.Fields.Count; field++)
            Assert.AreEqual(Comparable(recorded.Fields[field].Value), Comparable(values[field - 1]), $"0x{recorded.MessageId:x} field {field}");
    }
}
