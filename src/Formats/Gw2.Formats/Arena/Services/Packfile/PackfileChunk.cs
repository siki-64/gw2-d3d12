namespace Gw2.Formats;

internal readonly unsafe struct PackfileChunk(uint chunkType, ushort version, byte* data, uint dataSize)
{
    internal readonly uint ChunkType = chunkType;
    internal readonly ushort Version = version;
    internal readonly byte* Data = data;
    internal readonly uint DataSize = dataSize;
}
