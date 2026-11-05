using System;
using System.Collections.Generic;
using System.Linq;
using Gw2.Client.Net.Game;
using Gw2.Dat.Content;
using Gw2.Formats;
using Gw2.Protocol.Capture;
using Gw2.Protocol.MsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public sealed class ContentFunctionCaptureTests
{
    private const uint LeapMovementModifierUid = 278;

    [TestMethod]
    public void ReproducesTheRecordedSpecialDodgeMessages()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        var decoded = CaptureDecoding.DecodeAll(RecordedSession.Capture(), RecordedSession.Schemas);
        var inbound = decoded.First(result => result.Corpus == "game" && result.Stream.Direction == CaptureRecordKind.Inbound).Messages;
        var outbound = decoded.First(result => result.Corpus == "game" && result.Stream.Direction == CaptureRecordKind.Outbound).Messages;
        var recordedStart = outbound.FirstOrDefault(message => message.MessageId == OwnContentFunctionState.InstanceStart && Value(message, 4) != 0);
        if (recordedStart is null) Assert.Inconclusive("the capture holds no special dodge");
        var recordedInstance = Value(recordedStart, 1);
        var recorded = outbound
            .SkipWhile(message => message != recordedStart)
            .Where(message => message.MessageId is OwnContentFunctionState.InstanceStart or OwnContentFunctionState.InputPress
                || (message.MessageId == OwnContentFunctionState.ConditionResult && Value(message, 1) == recordedInstance))
            .TakeWhile(message => message.MessageId != OwnContentFunctionState.InstanceStart || message == recordedStart)
            .Select(message => Row(message.MessageId, Values(message).Skip(1)))
            .ToList();

        var recordedFunction = Value(recordedStart, 4);
        var state = new OwnContentFunctionState();
        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var runner = new Gw2ContentFunctionRunner(new Gw2ContentFunctions(reader.ContentIndex, ContentFunctionContentLayout.Current, SkillPaletteContentLayout.Current.SkillContentType));
        foreach (var message in inbound)
        {
            state.Handle(message);
            runner.SyncBindings(state.ActiveBindingUids);
            if (runner.BindingOfInput(OwnContentFunctionState.DodgeInputId)?.FunctionUid == recordedFunction) break;
        }
        var world = new LeapingHost();
        var reports = new List<ContentFunctionReport>(runner.PressInput(OwnContentFunctionState.DodgeInputId, 0, 0, world));
        for (var now = 16L; now < 5000 && runner.IsRunning; now += 16)
        {
            if (now == 96) reports.AddRange(runner.ReleaseInput(OwnContentFunctionState.DodgeInputId));
            if (now == 800) world.Leaping = true;
            reports.AddRange(runner.Tick(now, world));
        }

        var produced = reports.Select(ToMessage).Select(message => Row(message.MessageId, message.Values.Select(Convert.ToUInt64))).ToList();
        CollectionAssert.AreEqual(recorded, produced.Take(recorded.Count).ToList(), string.Join(" | ", recorded) + " vs " + string.Join(" | ", produced));
    }

    private sealed class LeapingHost : IContentFunctionHost
    {
        public bool Leaping { get; set; }

        public bool HasMovementModifier(uint movementModifierUid) => Leaping && movementModifierUid == LeapMovementModifierUid;

        public void RemoveMovementModifier(uint movementModifierUid) => Leaping = false;
    }

    private static (int MessageId, object[] Values) ToMessage(ContentFunctionReport report) => report switch
    {
        ContentFunctionInstanceStarted started => OwnContentFunctionState.InstanceStartMessage(started.InstanceId, started.TargetAgentId, started.Source, started.FunctionUid, started.InputId),
        ContentFunctionConditionEvaluated evaluated => OwnContentFunctionState.ConditionResultMessage(evaluated.InstanceId, evaluated.RootFunctionUid, evaluated.FunctionUid, evaluated.NodeIndex, evaluated.Result),
        ContentFunctionInputReleased released => OwnContentFunctionState.InputPressMessage(released.FunctionUid),
        _ => throw new InvalidOperationException(),
    };

    private static string Row(int messageId, IEnumerable<ulong> values) =>
        $"{messageId:x} {string.Join(" ", messageId == OwnContentFunctionState.InputPress ? values : values.Skip(1))}";

    private static ulong Value(DecodedMessage message, int field) => Convert.ToUInt64(message.Fields[field].Value);

    private static IEnumerable<ulong> Values(DecodedMessage message) => message.Fields.Select(field => Convert.ToUInt64(field.Value));
}
