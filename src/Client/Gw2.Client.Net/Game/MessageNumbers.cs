using System;
using Gw2.Protocol.MsgPack;

namespace Gw2.Client.Net.Game;

public static class MessageNumbers
{
    private const int Fixed32FieldType = 0x06;

    public static float Number(DecodedMessage message, int field)
    {
        if (field >= message.Fields.Count) return 0f;
        if (message.Fields[field] is { FieldType: Fixed32FieldType, Value: ulong bits }) return BitConverter.UInt32BitsToSingle((uint)bits);
        return message.Fields[field].Value switch
        {
            float value => value,
            double value => (float)value,
            ulong value => value,
            long value => value,
            uint value => value,
            int value => value,
            byte value => value,
            _ => 0f,
        };
    }
}
