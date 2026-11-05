using System;
using System.Buffers.Binary;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace Gw2.Client.Net.Game;

public static class DebugSessionId
{
    public const int Length = 16;
    private static readonly DateTime SecondsEpoch = new(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static byte[] Create(uint machineSeed, DateTime utcNow)
    {
        var seconds = (uint)(utcNow - SecondsEpoch).TotalSeconds;
        var id = new byte[Length];
        var span = id.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(span, machineSeed ^ 1);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], seconds);
        BinaryPrimitives.WriteUInt32LittleEndian(span[8..], machineSeed ^ 2);
        BinaryPrimitives.WriteUInt32LittleEndian(span[12..], seconds);
        return id;
    }

    public static uint MachineSeedOfThisMachine()
    {
        var address = Dns.GetHostAddresses(Dns.GetHostName()).FirstOrDefault(candidate => candidate.AddressFamily == AddressFamily.InterNetwork);
        return address is null ? 0 : BinaryPrimitives.ReadUInt32LittleEndian(address.GetAddressBytes());
    }
}
