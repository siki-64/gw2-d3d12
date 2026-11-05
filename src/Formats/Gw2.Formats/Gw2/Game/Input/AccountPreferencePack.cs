using System;
using System.Buffers.Binary;

namespace Gw2.Formats;

public static class AccountPreferencePack
{
    private const int PackHeaderSizeOffset = 6;
    private const int ChunkSizeOffset = 4;
    private const int ChunkPrefixSize = 8;
    private const int ChunkHeaderFieldsSize = 8;

    public static (int Offset, int Length)? FindChunk(ReadOnlySpan<byte> pack, uint magic)
    {
        if (pack.Length < PackHeaderSizeOffset + 2) return null;
        var offset = (int)BinaryPrimitives.ReadUInt16LittleEndian(pack[PackHeaderSizeOffset..]);
        while (offset + ChunkPrefixSize <= pack.Length)
        {
            var length = ChunkPrefixSize + (int)BinaryPrimitives.ReadUInt32LittleEndian(pack[(offset + ChunkSizeOffset)..]);
            if (offset + length > pack.Length) return null;
            if (BinaryPrimitives.ReadUInt32LittleEndian(pack[offset..]) == magic) return (offset, length);
            offset += length;
        }
        return null;
    }

    public static byte[] Chunk(uint magic, ushort version, ushort headerSize, uint descriptorOffset, ReadOnlySpan<byte> data)
    {
        var chunk = new byte[ChunkPrefixSize + ChunkHeaderFieldsSize + data.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(chunk, magic);
        BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(ChunkSizeOffset), (uint)(ChunkHeaderFieldsSize + data.Length));
        BinaryPrimitives.WriteUInt16LittleEndian(chunk.AsSpan(8), version);
        BinaryPrimitives.WriteUInt16LittleEndian(chunk.AsSpan(10), headerSize);
        BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(12), descriptorOffset);
        data.CopyTo(chunk.AsSpan(ChunkPrefixSize + ChunkHeaderFieldsSize));
        return chunk;
    }

    public static byte[] ReplaceChunk(byte[] pack, uint magic, byte[] chunk)
    {
        if (FindChunk(pack, magic) is not var (offset, length)) return [.. pack, .. chunk];
        return [.. pack.AsSpan(0, offset), .. chunk, .. pack.AsSpan(offset + length)];
    }
}
