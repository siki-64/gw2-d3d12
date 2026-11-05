#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Gw2.Protocol.MsgPack;
using Gw2.Protocol.Schema;

namespace Gw2.Protocol.Capture
{
    public sealed class MessageRoundTripMismatch
    {
        public MessageRoundTripMismatch(int messageIndex, DecodedMessage message, byte[] reencoded, string? failure)
        {
            MessageIndex = messageIndex;
            Message = message;
            Reencoded = reencoded;
            Failure = failure;
        }

        public int MessageIndex { get; }
        public DecodedMessage Message { get; }
        public byte[] Reencoded { get; }
        public string? Failure { get; }

        public long FirstDifferentByte => CaptureComparison.FirstDifference(Message.Wire, Reencoded);

        public override string ToString() => Failure is null
            ? $"message #{MessageIndex} id 0x{Message.MessageId:x}: {Message.WireLength} bytes captured, {Reencoded.Length} re-encoded, first difference at byte {FirstDifferentByte}"
            : $"message #{MessageIndex} id 0x{Message.MessageId:x}: {Failure}";
    }

    public sealed class StreamRoundTrip
    {
        public StreamRoundTrip(StreamDecodeResult decoded, IReadOnlyList<MessageRoundTripMismatch> mismatches)
        {
            Decoded = decoded;
            Mismatches = mismatches;
        }

        public StreamDecodeResult Decoded { get; }
        public IReadOnlyList<MessageRoundTripMismatch> Mismatches { get; }
        public bool Exact => Decoded.Complete && Mismatches.Count == 0;

        public override string ToString() =>
            $"{Decoded}; {Decoded.Messages.Count - Mismatches.Count} of {Decoded.Messages.Count} messages re-encode exactly";
    }

    public static class CaptureRoundTrip
    {
        public static IReadOnlyList<StreamRoundTrip> ReencodeAll(NetworkCapture capture, ExecutableProtocolSchemas schemas)
        {
            var results = new List<StreamRoundTrip>();
            foreach (var decoded in CaptureDecoding.DecodeAll(capture, schemas))
            {
                var corpus = decoded.Corpus == "login" ? schemas.Login : schemas.Game;
                var messageSchemas = decoded.Stream.Direction == CaptureRecordKind.Inbound ? corpus.Recv : corpus.Send;
                results.Add(new StreamRoundTrip(decoded, Reencode(messageSchemas, decoded.Messages)));
            }
            return results;
        }

        public static IReadOnlyList<MessageRoundTripMismatch> Reencode(MessageSchemaSet schemas, IReadOnlyList<DecodedMessage> messages)
        {
            var mismatches = new List<MessageRoundTripMismatch>();
            for (int index = 0; index < messages.Count; index++)
            {
                var message = messages[index];
                if (!schemas.TryGet(message.MessageId, out var schema))
                {
                    mismatches.Add(new MessageRoundTripMismatch(index, message, Array.Empty<byte>(), "no schema"));
                    continue;
                }
                byte[] reencoded;
                try
                {
                    reencoded = MsgPackWriter.WriteMessage(schema, message.Fields);
                }
                catch (FormatException failure)
                {
                    mismatches.Add(new MessageRoundTripMismatch(index, message, Array.Empty<byte>(), failure.Message));
                    continue;
                }
                if (!reencoded.AsSpan().SequenceEqual(message.Wire)) mismatches.Add(new MessageRoundTripMismatch(index, message, reencoded, null));
            }
            return mismatches;
        }

        public static IReadOnlyList<int> MessageBoundaries(IReadOnlyList<DecodedMessage> messages) =>
            messages.Select(message => message.WireOffset + message.WireLength).ToArray();
    }
}
