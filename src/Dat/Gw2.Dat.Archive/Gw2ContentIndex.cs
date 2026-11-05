using System.Buffers.Binary;
using Gw2.Formats;

namespace Gw2.Dat.Archive;

public readonly record struct Gw2ContentLocation(int PackIndex, uint Offset);

public readonly record struct Gw2ContentFieldOffsets(int GuidOffset, int UidOffset, int DataIdOffset, int NameOffset)
{
    public const int Absent = -1;
}

public sealed class Gw2ContentIndex
{
    public const int ContentIdBits = 22;
    public const uint ContentIdMask = 0x3FFFFF;
    private const int TypeInfoSize = 0x11;
    private const int TypeInfoGuidOffset = 0x0;
    private const int TypeInfoUidOffset = 0x4;
    private const int TypeInfoDataIdOffset = 0x8;
    private const int TypeInfoNameOffset = 0xC;
    private const int ObjectNameHashOffset = 0x8;
    private const int PointerSize = 8;
    private const int GuidSize = 16;

    private readonly IReadOnlyList<Gw2CntcPack> _packs;
    private readonly Gw2ContentFieldOffsets[] _fieldOffsets;
    private readonly Dictionary<uint, Gw2ContentLocation> _byTypedUid = [];
    private readonly Dictionary<uint, Gw2ContentLocation> _byTypedDataId = [];
    private readonly Dictionary<uint, List<Gw2ContentLocation>> _byType = [];

    public Gw2ContentIndex(IReadOnlyList<Gw2CntcPack> packs)
    {
        ArgumentNullException.ThrowIfNull(packs);
        _packs = packs;
        _fieldOffsets = packs.Count == 0 ? [] : ReadFieldOffsets(packs[0]);
        for (int packIndex = 0; packIndex < packs.Count; packIndex++)
            foreach (var entry in packs[packIndex].IndexEntries)
                Register(entry.Type, new Gw2ContentLocation(packIndex, entry.Offset));
    }

    public IReadOnlyList<Gw2CntcPack> Packs => _packs;

    public int TypeCount => _fieldOffsets.Length;

    public static uint TypedKey(uint type, uint id) => (id & ContentIdMask) | (type << ContentIdBits);

    public Gw2ContentFieldOffsets FieldOffsetsOf(uint type) =>
        type < _fieldOffsets.Length ? _fieldOffsets[type] : new(Gw2ContentFieldOffsets.Absent, Gw2ContentFieldOffsets.Absent, Gw2ContentFieldOffsets.Absent, Gw2ContentFieldOffsets.Absent);

    public Gw2ContentLocation? Find(uint type, uint uid) =>
        _byTypedUid.TryGetValue(TypedKey(type, uid), out var location) ? location : null;

    public Gw2ContentLocation? FindTyped(uint type, uint dataId) =>
        _byTypedDataId.TryGetValue(TypedKey(type, dataId), out var location) ? location : null;

    public IReadOnlyList<Gw2ContentLocation> All(uint type) =>
        _byType.TryGetValue(type, out var locations) ? locations : [];

    public Gw2ContentLocation? FindByContentKey(uint type, Guid contentKey)
    {
        int guidOffset = FieldOffsetsOf(type).GuidOffset;
        if (guidOffset == Gw2ContentFieldOffsets.Absent) return null;
        foreach (var location in All(type))
            if (ReadGuid(location, guidOffset) == contentKey) return location;
        return null;
    }

    public Gw2ContentLocation? FindByName(uint type, string fullName)
    {
        int nameOffset = FieldOffsetsOf(type).NameOffset;
        if (nameOffset == Gw2ContentFieldOffsets.Absent) return null;
        uint nameHash = NameHash.OfFullName(fullName);
        foreach (var location in All(type))
            if (ReadPointer(location, nameOffset) is { } name && ReadUInt32(name, ObjectNameHashOffset) == nameHash) return location;
        return null;
    }

    public uint ReadUInt32(Gw2ContentLocation location, int fieldOffset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(Field(location, fieldOffset, sizeof(uint)));

    public int ReadInt32(Gw2ContentLocation location, int fieldOffset) =>
        BinaryPrimitives.ReadInt32LittleEndian(Field(location, fieldOffset, sizeof(int)));

    public float ReadSingle(Gw2ContentLocation location, int fieldOffset) =>
        BinaryPrimitives.ReadSingleLittleEndian(Field(location, fieldOffset, sizeof(float)));

    public byte ReadByte(Gw2ContentLocation location, int fieldOffset) => Field(location, fieldOffset, 1)[0];

    public Guid ReadGuid(Gw2ContentLocation location, int fieldOffset) => new(Field(location, fieldOffset, GuidSize));

    public ReadOnlySpan<byte> ReadBytes(Gw2ContentLocation location, int fieldOffset, int length) => Field(location, fieldOffset, length);

    public Gw2ContentLocation? ReadPointer(Gw2ContentLocation location, int fieldOffset)
    {
        uint relocation = checked(location.Offset + (uint)fieldOffset);
        if (!_packs[location.PackIndex].TryResolvePointer(relocation, (uint)location.PackIndex, out var pointer)) return null;
        return pointer.PackIndex < _packs.Count ? new Gw2ContentLocation((int)pointer.PackIndex, pointer.TargetOffset) : null;
    }

    public Gw2ContentLocation? ReadPointerArrayElement(Gw2ContentLocation array, int index) => ReadPointer(array, checked(index * PointerSize));

    public uint? ReadFileId(Gw2ContentLocation location, int fieldOffset)
    {
        uint relocation = checked(location.Offset + (uint)fieldOffset);
        return _packs[location.PackIndex].TryReadFileReferenceAt(relocation, _packs, out uint fileId) ? fileId : null;
    }

    public uint UidOf(uint type, Gw2ContentLocation location) => ReadIdAt(location, FieldOffsetsOf(type).UidOffset);

    public uint DataIdOf(uint type, Gw2ContentLocation location) => ReadIdAt(location, FieldOffsetsOf(type).DataIdOffset);

    private uint ReadIdAt(Gw2ContentLocation location, int offset) => offset == Gw2ContentFieldOffsets.Absent ? 0 : ReadUInt32(location, offset);

    private ReadOnlySpan<byte> Field(Gw2ContentLocation location, int fieldOffset, int length) =>
        _packs[location.PackIndex].ReadContent(checked(location.Offset + (uint)fieldOffset), length).Span;

    private void Register(uint type, Gw2ContentLocation location)
    {
        if (!_byType.TryGetValue(type, out var locations)) _byType[type] = locations = [];
        locations.Add(location);
        if (type >= _fieldOffsets.Length) return;
        RegisterId(_byTypedUid, type, location, _fieldOffsets[type].UidOffset);
        RegisterId(_byTypedDataId, type, location, _fieldOffsets[type].DataIdOffset);
    }

    private void RegisterId(Dictionary<uint, Gw2ContentLocation> map, uint type, Gw2ContentLocation location, int idOffset)
    {
        if (idOffset == Gw2ContentFieldOffsets.Absent) return;
        uint id = ReadUInt32(location, idOffset);
        if (id != 0) map[TypedKey(type, id)] = location;
    }

    private static Gw2ContentFieldOffsets[] ReadFieldOffsets(Gw2CntcPack typePack)
    {
        var typeInfos = typePack.GetArray(Gw2CntcArrayKind.TypeInfos);
        var data = typePack.Data.Span;
        var offsets = new Gw2ContentFieldOffsets[typeInfos.Count];
        for (int type = 0; type < offsets.Length; type++)
        {
            var entry = data.Slice(checked((int)typeInfos.Offset + type * TypeInfoSize), TypeInfoSize);
            offsets[type] = new(
                BinaryPrimitives.ReadInt32LittleEndian(entry[TypeInfoGuidOffset..]),
                BinaryPrimitives.ReadInt32LittleEndian(entry[TypeInfoUidOffset..]),
                BinaryPrimitives.ReadInt32LittleEndian(entry[TypeInfoDataIdOffset..]),
                BinaryPrimitives.ReadInt32LittleEndian(entry[TypeInfoNameOffset..]));
        }
        return offsets;
    }
}
