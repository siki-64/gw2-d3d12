#nullable enable
using System;
using System.Collections.Generic;
using Gw2.Protocol.Framing;
using Gw2.Protocol.MsgPack;
using Gw2.Protocol.Schema;

namespace Gw2.Protocol.Capture
{
    public sealed class StreamDecodeResult
    {
        public StreamDecodeResult(CapturedStream stream, string corpus, IReadOnlyList<DecodedMessage> messages, int undecodedBytes, string? failure)
        {
            Stream = stream;
            Corpus = corpus;
            Messages = messages;
            UndecodedBytes = undecodedBytes;
            Failure = failure;
        }

        public CapturedStream Stream { get; }
        public string Corpus { get; }
        public IReadOnlyList<DecodedMessage> Messages { get; }
        public int UndecodedBytes { get; }
        public string? Failure { get; }
        public bool Complete => Failure is null && UndecodedBytes == 0;

        public override string ToString() =>
            $"connection {Stream.ConnectionOrder} protocol {Stream.Protocol} {Stream.Direction} as {Corpus}: {Messages.Count} messages, {UndecodedBytes} bytes undecoded{(Failure is null ? "" : ", " + Failure)}";
    }

    public static class CaptureDecoding
    {
        public static IReadOnlyList<StreamDecodeResult> DecodeAll(NetworkCapture capture, ExecutableProtocolSchemas schemas)
        {
            var results = new List<StreamDecodeResult>();
            foreach (var stream in capture.Streams())
            {
                if (stream.Direction == CaptureRecordKind.HandshakeOutbound) continue;
                var game = Decode(stream, "game", schemas.Game);
                results.Add(game.Complete ? game : Better(game, Decode(stream, "login", schemas.Login)));
            }
            return results;
        }

        public static StreamDecodeResult Decode(CapturedStream stream, string corpusName, ProtocolSchemaCorpus corpus)
        {
            ReadOnlySpan<byte> messageStream;
            int undecodedFrameBytes = 0;
            if (stream.Direction == CaptureRecordKind.Inbound)
            {
                byte[] deframed;
                try
                {
                    deframed = TransportFrame.Deframe(stream.Bytes, out int consumed);
                    undecodedFrameBytes = stream.Bytes.Length - consumed;
                }
                catch (Exception failure) when (failure is FormatException || failure is InvalidOperationException || failure is ArgumentException)
                {
                    return new StreamDecodeResult(stream, corpusName, Array.Empty<DecodedMessage>(), stream.Bytes.Length, "deframe: " + failure.Message);
                }
                messageStream = deframed;
            }
            else
            {
                messageStream = stream.Bytes;
            }

            var schemas = stream.Direction == CaptureRecordKind.Inbound ? corpus.Recv : corpus.Send;
            try
            {
                var messages = MessageStreamDecoder.TryDecode(schemas, messageStream, out int consumed);
                return new StreamDecodeResult(stream, corpusName, messages, messageStream.Length - consumed + undecodedFrameBytes, null);
            }
            catch (FormatException failure)
            {
                return new StreamDecodeResult(stream, corpusName, Array.Empty<DecodedMessage>(), messageStream.Length, failure.Message);
            }
        }

        private static StreamDecodeResult Better(StreamDecodeResult first, StreamDecodeResult second) =>
            second.Complete || (first.Failure is not null && second.Messages.Count > first.Messages.Count) ? second : first;
    }
}
