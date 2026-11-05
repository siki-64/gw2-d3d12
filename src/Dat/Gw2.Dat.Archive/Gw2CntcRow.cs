using System.Buffers.Binary;

namespace Gw2.Dat.Archive;

public readonly struct Gw2CntcRow
{
    private readonly Gw2CntcPack _pack;
    private readonly uint _offset;

    internal Gw2CntcRow(Gw2CntcPack pack, uint offset, int length)
    {
        _pack = pack;
        _offset = offset;
        Length = length;
    }

    public int Length { get; }

    public byte U8(int relativeOffset) => Bytes(relativeOffset, 1)[0];
    public ushort U16(int relativeOffset) => BinaryPrimitives.ReadUInt16LittleEndian(Bytes(relativeOffset, 2));
    public short I16(int relativeOffset) => BinaryPrimitives.ReadInt16LittleEndian(Bytes(relativeOffset, 2));
    public uint U32(int relativeOffset) => BinaryPrimitives.ReadUInt32LittleEndian(Bytes(relativeOffset, 4));
    public int I32(int relativeOffset) => BinaryPrimitives.ReadInt32LittleEndian(Bytes(relativeOffset, 4));
    public ulong U64(int relativeOffset) => BinaryPrimitives.ReadUInt64LittleEndian(Bytes(relativeOffset, 8));
    public float F32(int relativeOffset) => Gw2Binary.ReadSingleLittleEndian(Bytes(relativeOffset, 4));
    public Guid Guid(int relativeOffset) => new(Bytes(relativeOffset, 16));

    public ReadOnlySpan<byte> Bytes(int relativeOffset, int length)
    {
        if (relativeOffset < 0 || length < 0 || relativeOffset + length > Length)
            throw new ArgumentOutOfRangeException(nameof(relativeOffset));
        return _pack.ReadContent(checked(_offset + (uint)relativeOffset), length).Span;
    }
}
