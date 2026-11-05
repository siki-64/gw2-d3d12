using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Gw2.Client.Net.Game;
using Gw2.Formats;
using Gw2.Protocol.Capture;
using Gw2.Protocol.MsgPack;
using Gw2.Protocol.Schema;

var schemas = ExecutableProtocolSchemas.FromExecutable(Gw2Installation.ExecutablePath()!);
foreach (var capturePath in args)
{
    NetworkCapture capture;
    try { capture = NetworkCapture.Read(capturePath); } catch (FormatException) { continue; }
    if (capture.Build != schemas.Build) continue;
    var decoded = CaptureDecoding.DecodeAll(capture, schemas);
    var outbound = decoded.FirstOrDefault(result => result.Corpus == "game" && result.Stream.Direction == CaptureRecordKind.Outbound);
    var inbound = decoded.FirstOrDefault(result => result.Corpus == "game" && result.Stream.Direction == CaptureRecordKind.Inbound);
    if (outbound is null || inbound is null) continue;
    var loadMap = inbound.Messages.FirstOrDefault(message => message.MessageId == GameMessageIds.LoadMap);
    var states = outbound.Messages.Where(message => message.MessageId == GameMessageIds.OwnCharacterState).ToList();
    var runs = 0;
    var longest = 0;
    var current = 0;
    for (var index = 1; index < states.Count; index++)
    {
        var consecutive = StepOf(states[index]) == StepOf(states[index - 1]) + 1 && IsGround(states[index]) && IsGround(states[index - 1]);
        current = consecutive ? current + 1 : 0;
        if (current == 1) runs++;
        longest = Math.Max(longest, current);
    }
    Console.WriteLine($"{System.IO.Path.GetFileName(capturePath)} map {loadMap?.Fields[4].Value} states {states.Count} moves {outbound.Messages.Count(message => message.MessageId == 0x0b)} jumps {outbound.Messages.Count(message => message.MessageId == 0x09)} longestGroundRun {longest}");
    if (args.Length == 1 && loadMap is not null)
    {
        var ownAgentId = (ulong)outbound.Messages.First(message => message.MessageId == 0x05).Fields[1].Value!;
        var create = inbound.Messages.First(message => message.MessageId == 0x25 && (ulong)message.Fields[1].Value! == ownAgentId);
        var collider = (IReadOnlyList<DecodedField[]>)create.Fields[14].Value!;
        var colliderValues = collider.Select(entry => BitConverter.Int32BitsToSingle((int)(ulong)entry[^1].Value!)).ToArray();
        var radius = BitConverter.Int32BitsToSingle((int)(ulong)create.Fields[15].Value!);
        Console.WriteLine($"own {ownAgentId} radius {radius} collider {string.Join(", ", colliderValues)}");
        Replay.OneStepPredictions((uint)(ulong)loadMap.Fields[4].Value!, outbound.Messages, radius, colliderValues[3]);
    }
    if (args.Length == 1)
    {
        foreach (var move in outbound.Messages.Where(message => message.MessageId is 0x0b or 0x04 or 0x09))
            Console.WriteLine($"cmd 0x{move.MessageId:x2} step {move.Fields[2].Value} flags {move.Fields[3].Value} code {(move.Fields.Count > 5 ? Hex(move.Fields[5].Value) : "-")} dir {Floats(move.Fields.Count > 7 ? move.Fields[7].Value : null)} facing {Floats(move.Fields.Count > 8 ? move.Fields[8].Value : null)}");
        for (var index = 1; index < states.Count; index++)
        {
            if (StepOf(states[index]) != StepOf(states[index - 1]) + 1) continue;
            var delta = PositionOf(states[index]) - PositionOf(states[index - 1]);
            Console.WriteLine($"{StepOf(states[index])} d=({delta.X:R}, {delta.Y:R}, {delta.Z:R}) h={new Vector2(delta.X, delta.Y).Length() * 32f / 0.04f:F3}u/s state {states[index].Fields[5].Value}/{states[index].Fields[6].Value}");
        }
    }
}

static long StepOf(DecodedMessage message) => (long)(ulong)message.Fields[1].Value!;
static bool IsGround(DecodedMessage message) => (ulong)message.Fields[5].Value! == 0 && (ulong)message.Fields[6].Value! == 0;
static Vector3 PositionOf(DecodedMessage message)
{
    var bytes = (byte[])message.Fields[3].Value!;
    return new Vector3(BitConverter.ToSingle(bytes, 0), BitConverter.ToSingle(bytes, 4), BitConverter.ToSingle(bytes, 8));
}

static string Hex(object? value) => value is byte[] bytes ? Convert.ToHexString(bytes) : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
static string Floats(object? value) => value is byte[] bytes ? string.Join(",", Enumerable.Range(0, bytes.Length / 4).Select(index => BitConverter.ToSingle(bytes, index * 4).ToString("F4", System.Globalization.CultureInfo.InvariantCulture))) : "-";
