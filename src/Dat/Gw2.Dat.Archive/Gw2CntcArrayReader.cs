using System.Buffers.Binary;

namespace Gw2.Dat.Archive;

public readonly record struct Gw2CntcArray(Gw2CntcPack Pack, uint PackIndex, uint Offset, int Count, int ElementSize)
{
    public uint ElementOffset(int index) => checked(Offset + (uint)(index * ElementSize));

    public Gw2CntcRow Element(int index) => Pack.ReadRow(ElementOffset(index), ElementSize);
}

public static class Gw2CntcArrayReader
{
    private const int PointerSize = 8;
    private const int DataIdOffset = 0x14;

    public static Gw2CntcArray? Read(Gw2CntcPack pack, uint packIndex, uint fieldOffset, int elementSize)
    {
        var count = (int)BinaryPrimitives.ReadUInt32LittleEndian(pack.ReadContent(fieldOffset + PointerSize, 4).Span);
        if (count == 0 || !pack.TryResolvePointer(fieldOffset, packIndex, out var pointer) || pointer.IsExternal) return null;
        return new Gw2CntcArray(pack, packIndex, pointer.TargetOffset, count, elementSize);
    }

    public static uint? ReadReferencedDataId(IReadOnlyList<Gw2CntcPack> contentStore, Gw2CntcPack pack, uint packIndex, uint relocationOffset)
    {
        if (!pack.TryResolvePointer(relocationOffset, packIndex, out var pointer) || pointer.PackIndex >= contentStore.Count) return null;
        var target = contentStore[(int)pointer.PackIndex];
        return BinaryPrimitives.ReadUInt32LittleEndian(target.ReadContent(pointer.TargetOffset + DataIdOffset, 4).Span);
    }

    public static uint[] ReadReferencedDataIds(IReadOnlyList<Gw2CntcPack> contentStore, Gw2CntcPack pack, uint packIndex, uint fieldOffset)
    {
        if (Read(pack, packIndex, fieldOffset, PointerSize) is not { } array) return [];

        var dataIds = new List<uint>(array.Count);
        for (var i = 0; i < array.Count; i++)
            if (ReadReferencedDataId(contentStore, pack, packIndex, array.ElementOffset(i)) is { } dataId)
                dataIds.Add(dataId);
        return [.. dataIds];
    }
}
