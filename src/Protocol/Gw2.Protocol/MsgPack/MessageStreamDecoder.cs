using System;
using System.Collections.Generic;
using Gw2.Protocol.Schema;

namespace Gw2.Protocol.MsgPack
{
    public static class MessageStreamDecoder
    {
        public const int MaxMessageBytes = 0x2000;

        public static IReadOnlyList<DecodedMessage> Decode(MessageSchemaSet schemas,
                                                           ReadOnlySpan<byte> stream)
        {
            if (schemas == null) throw new ArgumentNullException(nameof(schemas));

            var messages = new List<DecodedMessage>();
            int offset = 0;
            while (offset < stream.Length)
            {
                if (offset + 2 > stream.Length)
                    throw new FormatException($"msgpack: truncated message id at offset {offset}");

                int messageId = stream[offset] | (stream[offset + 1] << 8);
                if (!schemas.TryGet(messageId, out MessageSchema schema))
                    throw new FormatException($"msgpack: unknown message id 0x{messageId:x} at offset {offset}");

                int start = offset;
                DecodedField[] fields = MsgPackReader.ReadFields(schema.Fields, stream, ref offset);
                int length = offset - start;
                if (length > MaxMessageBytes)
                    throw new FormatException($"msgpack: message 0x{messageId:x} exceeds {MaxMessageBytes} bytes");

                messages.Add(new DecodedMessage(messageId, start, length, fields, stream.Slice(start, length).ToArray()));
            }
            return messages;
        }

        public static IReadOnlyList<DecodedMessage> TryDecode(MessageSchemaSet schemas,
                                                              ReadOnlySpan<byte> stream,
                                                              out int consumed)
        {
            if (schemas == null) throw new ArgumentNullException(nameof(schemas));

            var messages = new List<DecodedMessage>();
            int offset = 0;
            while (offset < stream.Length)
            {
                if (offset + 2 > stream.Length) break;
                int messageId = stream[offset] | (stream[offset + 1] << 8);
                if (!schemas.TryGet(messageId, out MessageSchema schema))
                    throw new FormatException($"msgpack: unknown message id 0x{messageId:x} at offset {offset}");

                int start = offset;
                DecodedField[] fields;
                try
                {
                    fields = MsgPackReader.ReadFields(schema.Fields, stream, ref offset);
                }
                catch (MsgPackTruncatedException)
                {
                    offset = start;
                    break;
                }

                int length = offset - start;
                if (length > MaxMessageBytes)
                    throw new FormatException($"msgpack: message 0x{messageId:x} exceeds {MaxMessageBytes} bytes");
                messages.Add(new DecodedMessage(messageId, start, length, fields, stream.Slice(start, length).ToArray()));
            }
            consumed = offset;
            return messages;
        }
    }
}
