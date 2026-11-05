using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net;
using Gw2.Protocol.MsgPack;

namespace Gw2.Client.Net.Login;

public sealed record GameServerReferral(uint Kind, uint Ticket, uint MapId, byte[] SocketAddress, uint InstanceTicket)
{
    public const int DefaultPort = 6112;
    private const int PortOffset = 2;
    private const int AddressOffset = 4;
    private const int AddressLength = 4;

    public int Port
    {
        get
        {
            var port = BinaryPrimitives.ReadUInt16BigEndian(SocketAddress.AsSpan(PortOffset));
            return port != 0 ? port : DefaultPort;
        }
    }

    public IPAddress Address => new IPAddress(SocketAddress.AsSpan(AddressOffset, AddressLength));

    public static GameServerReferral FromMessage(IReadOnlyList<DecodedField> fields) => new(
        (uint)(ulong)fields[2].Value!,
        (uint)(ulong)fields[3].Value!,
        (uint)(ulong)fields[4].Value!,
        (byte[])fields[5].Value!,
        (uint)(ulong)fields[6].Value!);
}
