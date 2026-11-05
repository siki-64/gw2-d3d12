using System;
using System.Buffers.Binary;

namespace Gw2.Client.Net.Game;

public sealed record GameJoinRecord(uint Build, uint Ticket, uint MapId, uint InstanceTicket, byte[] JoinToken, byte[] ShardIdentity)
{
    public const uint Kind = 0xC0500;
    public const uint Program = 0x65;
    public const int Length = 0x54;
    public const int JoinTokenLength = 32;
    public const int ShardIdentityLength = 16;

    private const int BuildOffset = 0x04;
    private const int ProgramOffset = 0x08;
    private const int SizeOffset = 0x0C;
    private const int TicketOffset = 0x10;
    private const int MapIdOffset = 0x18;
    private const int InstanceTicketOffset = 0x1C;
    private const int JoinTokenOffset = 0x20;
    private const int ShardIdentityOffset = 0x40;

    public byte[] Encode()
    {
        if (JoinToken.Length != JoinTokenLength) throw new InvalidOperationException($"the join token is {JoinToken.Length} bytes, not {JoinTokenLength}");
        if (ShardIdentity.Length != ShardIdentityLength) throw new InvalidOperationException($"the shard identity is {ShardIdentity.Length} bytes, not {ShardIdentityLength}");
        var bytes = new byte[Length];
        var span = bytes.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(span, Kind);
        BinaryPrimitives.WriteUInt32LittleEndian(span[BuildOffset..], Build);
        BinaryPrimitives.WriteUInt32LittleEndian(span[ProgramOffset..], Program);
        BinaryPrimitives.WriteUInt32LittleEndian(span[SizeOffset..], Length - SizeOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(span[TicketOffset..], Ticket);
        BinaryPrimitives.WriteUInt32LittleEndian(span[MapIdOffset..], MapId);
        BinaryPrimitives.WriteUInt32LittleEndian(span[InstanceTicketOffset..], InstanceTicket);
        JoinToken.CopyTo(span[JoinTokenOffset..]);
        ShardIdentity.CopyTo(span[ShardIdentityOffset..]);
        return bytes;
    }
}
