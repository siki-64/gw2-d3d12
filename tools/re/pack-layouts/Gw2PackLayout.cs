using Gw2.Formats;
using System.Runtime.InteropServices;

namespace Gw2.Exe.Objects.Dat;

[StructLayout(LayoutKind.Explicit, Size = 0xC)]
internal struct Gw2PackArray<TElement>
{
    [FieldOffset(0x00)] internal uint Count;
    [FieldOffset(0x04)] internal long RelativeOffset;
}

[StructLayout(LayoutKind.Explicit, Size = 0xC)]
internal struct Gw2PackPointerArray<TElement>
{
    [FieldOffset(0x00)] internal uint Count;
    [FieldOffset(0x04)] internal long RelativeOffsetToPointers;
}

[StructLayout(LayoutKind.Explicit, Size = 0x8)]
internal struct Gw2PackPointer<TTarget>
{
    [FieldOffset(0x00)] internal long RelativeOffset;
}

[StructLayout(LayoutKind.Explicit, Size = 0x8)]
internal struct Gw2PackFileReferencePointer
{
    [FieldOffset(0x00)] internal long RelativeOffsetToFileReference;
}

[StructLayout(LayoutKind.Explicit, Size = 0x6)]
internal struct Gw2PackFileReference
{
    [FieldOffset(0x00)] internal ushort Low;
    [FieldOffset(0x02)] internal ushort High;
    [FieldOffset(0x04)] internal ushort Terminator;
}

[StructLayout(LayoutKind.Explicit, Size = 0x8)]
internal struct Gw2PackString
{
    [FieldOffset(0x00)] internal long RelativeOffsetToUtf8;
}

[StructLayout(LayoutKind.Explicit, Size = 0x8)]
internal struct Gw2PackWideString
{
    [FieldOffset(0x00)] internal long RelativeOffsetToUtf16;
}

[StructLayout(LayoutKind.Explicit, Size = 0xC)]
internal struct Gw2PackVariantPointer
{
    [FieldOffset(0x00)] internal uint Case;
    [FieldOffset(0x04)] internal long RelativeOffset;
}

[StructLayout(LayoutKind.Explicit, Size = 0x3)]
internal struct Gw2PackRgb
{
    [FieldOffset(0x00)] internal byte First;
    [FieldOffset(0x01)] internal byte Second;
    [FieldOffset(0x02)] internal byte Third;
}

[StructLayout(LayoutKind.Explicit, Size = 0x8)]
internal struct Gw2PackUInt2
{
    [FieldOffset(0x00)] internal uint X;
    [FieldOffset(0x04)] internal uint Y;
}

[StructLayout(LayoutKind.Explicit, Size = 0x10)]
internal struct Gw2PackUInt4
{
    [FieldOffset(0x00)] internal uint X;
    [FieldOffset(0x04)] internal uint Y;
    [FieldOffset(0x08)] internal uint Z;
    [FieldOffset(0x0C)] internal uint W;
}

[StructLayout(LayoutKind.Explicit, Size = 0x6)]
internal struct Gw2PackUShort3
{
    [FieldOffset(0x00)] internal ushort X;
    [FieldOffset(0x02)] internal ushort Y;
    [FieldOffset(0x04)] internal ushort Z;
}

[StructLayout(LayoutKind.Explicit, Size = 0x4)]
internal struct Gw2PackRaw4
{
    [FieldOffset(0x00)] internal uint Bits;
}

[StructLayout(LayoutKind.Explicit, Size = 0x10)]
internal struct Gw2PackRaw16
{
    [FieldOffset(0x00)] internal Gw2PackUInt4 Bits;
}
