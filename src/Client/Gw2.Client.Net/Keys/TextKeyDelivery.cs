using System;
using System.Collections.Generic;
using System.Buffers.Binary;
using Gw2.Protocol.MsgPack;

namespace Gw2.Client.Net.Keys;

public sealed class TextKeyDelivery
{
    public const int DeliverMessageId = 0x2EC;
    public const int DoneMessageId = 0x2EB;

    private const int EntriesFieldIndex = 1;
    private const int TextIdFieldIndex = 0;
    private const int KeyFieldIndex = 1;

    private readonly Action<uint, ulong> _storeKey;
    private readonly Action _clearKeys;

    public TextKeyDelivery(Action<uint, ulong> storeKey, Action clearKeys)
    {
        _storeKey = storeKey;
        _clearKeys = clearKeys;
    }

    public bool Handle(DecodedMessage message)
    {
        switch (message.MessageId)
        {
            case DeliverMessageId:
                foreach (var entry in EntriesOf(message))
                    _storeKey(checked((uint)(ulong)entry[TextIdFieldIndex].Value!), BinaryPrimitives.ReadUInt64LittleEndian((byte[])entry[KeyFieldIndex].Value!));
                return true;
            case DoneMessageId:
                _clearKeys();
                return true;
            default:
                return false;
        }
    }

    private static IReadOnlyList<DecodedField[]> EntriesOf(DecodedMessage message) =>
        message.Fields.Count > EntriesFieldIndex && message.Fields[EntriesFieldIndex].Value is IReadOnlyList<DecodedField[]> entries ? entries : [];
}
