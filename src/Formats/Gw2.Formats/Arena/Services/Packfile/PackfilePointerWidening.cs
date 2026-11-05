using System.Runtime.InteropServices;

namespace Gw2.Formats;

internal static unsafe class PackfilePointerWidening
{
    private const int NarrowPointerSize = 4;
    private const int WidePointerSize = 8;
    private const int Growth = WidePointerSize - NarrowPointerSize;
    private const uint FooterMagic = 0x544F4F46;
    private const int FooterSize = 16;

    internal static byte* Widen(byte* data, ref uint dataSize, ref uint relocationTableOffset, List<nint> allocations)
    {
        if (relocationTableOffset == 0) return data;
        if (HasFooter(data, dataSize))
            throw new NotSupportedException("Packfile chunks with a FOOT record are not widened by this port.");

        uint* table = (uint*)(data + relocationTableOffset);
        uint slotCount = *table;
        var slots = new uint[slotCount];
        for (uint index = 0; index < slotCount; index++) slots[index] = table[1 + index];
        Array.Sort(slots);

        uint widenedSize = dataSize + (uint)(Growth * slots.Length);
        var widened = (byte*)NativeMemory.AllocZeroed(widenedSize);
        allocations.Add((nint)widened);

        uint firstSlot = slots.Length == 0 ? dataSize : slots[0];
        Buffer.MemoryCopy(data, widened, firstSlot, firstSlot);
        for (int index = 0; index < slots.Length; index++)
        {
            uint slot = slots[index];
            uint widenedSlot = slot + (uint)(Growth * index);
            int distance = *(int*)(data + slot);
            *(long*)(widened + widenedSlot) = distance + DistanceAdjustment(slots, index, distance);
            uint segmentStart = slot + NarrowPointerSize;
            uint segmentEnd = index == slots.Length - 1 ? dataSize : slots[index + 1];
            Buffer.MemoryCopy(data + segmentStart, widened + widenedSlot + WidePointerSize, segmentEnd - segmentStart, segmentEnd - segmentStart);
        }

        uint widenedTableOffset = WidenedOffset(slots, relocationTableOffset);
        uint* widenedTable = (uint*)(widened + widenedTableOffset);
        for (uint index = 0; index < slotCount; index++)
            widenedTable[1 + index] = WidenedOffset(slots, table[1 + index]);
        dataSize = widenedSize;
        relocationTableOffset = widenedTableOffset;
        return widened;
    }

    private static bool HasFooter(byte* data, uint dataSize) =>
        dataSize >= FooterSize && *(uint*)(data + dataSize - 4) == FooterMagic
        && *(ushort*)(data + dataSize - 8) == 0 && *(ushort*)(data + dataSize - 6) == FooterSize;

    private static int DistanceAdjustment(uint[] slots, int index, int distance)
    {
        int adjustment = 0;
        long target = slots[index] + (long)distance;
        if (distance > 0)
        {
            for (int next = index; next < slots.Length && target > slots[next]; next++) adjustment += Growth;
        }
        else if (distance < 0)
        {
            for (int previous = index - 1; previous >= 0 && target <= slots[previous]; previous--) adjustment -= Growth;
        }
        return adjustment;
    }

    private static uint WidenedOffset(uint[] slots, uint offset)
    {
        uint growth = 0;
        foreach (uint slot in slots)
        {
            if (offset <= slot) break;
            growth += Growth;
        }
        return offset + growth;
    }
}
