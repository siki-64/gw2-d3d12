using System.Runtime.InteropServices;

namespace Gw2.Formats;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly struct PackfileHeader
{
    internal const ushort PackfileSignature = 0x4650;
    internal const ushort FlagPointers64 = 4;
    internal const ushort MaximumHeaderSize = 12;

    internal readonly ushort Signature;
    internal readonly ushort Flags;
    internal readonly ushort Endian;
    internal readonly ushort HeaderSize;
    internal readonly uint FileType;
}
