using System.Runtime.InteropServices;

namespace Gw2.Formats;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct PackfileChunkHeader
{
    internal const int DistanceOrigin = 8;
    internal const ushort SizeWithoutRelocationTable = 12;

    internal readonly uint ChunkType;
    internal readonly uint NextChunkDistance;
    internal readonly ushort Version;
    internal readonly ushort HeaderSize;
    internal readonly uint RelocationTableOffset;
}
