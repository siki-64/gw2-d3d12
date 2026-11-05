namespace Gw2.Formats;

public readonly record struct MftEntry(
    ulong Offset,
    uint Size,
    ushort Compression,
    ushort Flags,
    uint Counter,
    uint Crc);
