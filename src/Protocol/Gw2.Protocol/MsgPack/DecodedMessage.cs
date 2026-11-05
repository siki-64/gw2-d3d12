#nullable enable
using System;
using System.Collections.Generic;

namespace Gw2.Protocol.MsgPack
{
    public sealed class DecodedField
    {
        public DecodedField(int fieldType, bool present, object? value)
        {
            FieldType = fieldType;
            Present = present;
            Value = value;
        }

        public int FieldType { get; }

        public bool Present { get; }

        public object? Value { get; }
    }

    public sealed class DecodedArrayHeader
    {
        public DecodedArrayHeader(byte[] header, int count)
        {
            Header = header;
            Count = count;
        }

        public byte[] Header { get; }
        public int Count { get; }
    }

    public sealed class DecodedMessage
    {
        public DecodedMessage(int messageId, int wireOffset, int wireLength,
                              IReadOnlyList<DecodedField> fields, byte[]? wire = null)
        {
            Wire = wire;
            MessageId = messageId;
            WireOffset = wireOffset;
            WireLength = wireLength;
            Fields = fields;
        }

        public int MessageId { get; }

        public int WireOffset { get; }

        public int WireLength { get; }

        public IReadOnlyList<DecodedField> Fields { get; }

        public byte[]? Wire { get; }
    }
}
