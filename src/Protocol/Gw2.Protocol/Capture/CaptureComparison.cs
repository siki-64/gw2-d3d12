#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Gw2.Protocol.MsgPack;
using Gw2.Protocol.Schema;

namespace Gw2.Protocol.Capture
{
    public sealed class StreamDivergence
    {
        public StreamDivergence(int connectionOrder, CaptureRecordKind direction, uint protocol, long offset,
            long expectedLength, long actualLength, int expectedChunk, DecodedMessage? expectedMessage, int messageIndex)
        {
            ConnectionOrder = connectionOrder;
            Direction = direction;
            Protocol = protocol;
            Offset = offset;
            ExpectedLength = expectedLength;
            ActualLength = actualLength;
            ExpectedChunk = expectedChunk;
            ExpectedMessage = expectedMessage;
            MessageIndex = messageIndex;
        }

        public int ConnectionOrder { get; }
        public CaptureRecordKind Direction { get; }
        public uint Protocol { get; }
        public long Offset { get; }
        public long ExpectedLength { get; }
        public long ActualLength { get; }
        public int ExpectedChunk { get; }
        public DecodedMessage? ExpectedMessage { get; }
        public int MessageIndex { get; }

        public override string ToString()
        {
            var where = $"connection {ConnectionOrder} protocol {Protocol} {Direction} at byte {Offset} (chunk {ExpectedChunk}), lengths {ExpectedLength} vs {ActualLength}";
            return ExpectedMessage is null
                ? where
                : $"{where}, message #{MessageIndex} id 0x{ExpectedMessage.MessageId:x} byte {Offset - ExpectedMessage.WireOffset}";
        }
    }

    public static class CaptureComparison
    {
        public static IReadOnlyList<StreamDivergence> Compare(NetworkCapture expected, NetworkCapture actual,
            CaptureRecordKind direction, Func<uint, MessageSchemaSet?>? outboundSchemasOfProtocol = null)
        {
            var actualStreams = actual.Streams().Where(stream => stream.Direction == direction)
                .ToDictionary(stream => stream.ConnectionOrder);
            var divergences = new List<StreamDivergence>();
            foreach (var expectedStream in expected.Streams().Where(stream => stream.Direction == direction))
            {
                actualStreams.TryGetValue(expectedStream.ConnectionOrder, out var actualStream);
                var expectedBytes = expectedStream.Bytes;
                var actualBytes = actualStream is null ? ReadOnlySpan<byte>.Empty : actualStream.Bytes;
                long offset = FirstDifference(expectedBytes, actualBytes);
                if (offset < 0) continue;
                var schemas = direction == CaptureRecordKind.Outbound ? outboundSchemasOfProtocol?.Invoke(expectedStream.Protocol) : null;
                var (message, index) = schemas is null ? (null, -1) : MessageAt(schemas, expectedBytes, offset);
                divergences.Add(new StreamDivergence(expectedStream.ConnectionOrder, direction, expectedStream.Protocol, offset,
                    expectedBytes.Length, actualBytes.Length, expectedStream.ChunkAt(offset), message, index));
            }
            return divergences;
        }

        public static long FirstDifference(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual)
        {
            int common = Math.Min(expected.Length, actual.Length);
            int mismatch = expected[..common].CommonPrefixLength(actual[..common]);
            if (mismatch < common) return mismatch;
            return expected.Length == actual.Length ? -1 : common;
        }

        private static (DecodedMessage? Message, int Index) MessageAt(MessageSchemaSet schemas, ReadOnlySpan<byte> stream, long offset)
        {
            IReadOnlyList<DecodedMessage> messages;
            try
            {
                messages = MessageStreamDecoder.TryDecode(schemas, stream, out _);
            }
            catch (FormatException)
            {
                return (null, -1);
            }
            for (int index = 0; index < messages.Count; index++)
                if (offset < messages[index].WireOffset + messages[index].WireLength)
                    return (messages[index], index);
            return (null, -1);
        }
    }
}
