using System.Buffers.Binary;

namespace Gw2.Formats;

internal enum PackFieldKind : ushort
{
    FixedArray = 1,
    Array = 2,
    PointerArray = 3,
    TypedArray = 4,
    Byte = 5,
    Byte4 = 6,
    Double = 7,
    Double2 = 8,
    Double3 = 9,
    Dword = 10,
    FileName = 11,
    Float = 12,
    Float2 = 13,
    Float3 = 14,
    Float4 = 15,
    Pointer = 16,
    Qword = 17,
    WideString = 18,
    String = 19,
    Struct = 20,
    Word = 21,
    Uuid = 22,
    Byte3 = 23,
    Dword2 = 24,
    Dword4 = 25,
    Word3 = 26,
    FileReference = 27,
    Variant = 28,
    StructBase = 29,
    WordArray = 30,
    WordPointerArray = 31,
    WordTypedArray = 32,
    ByteArray = 33,
    BytePointerArray = 34,
    ByteTypedArray = 35,
    DwordToken = 36,
    QwordToken = 37,
}

internal sealed class PackType(string id, string name)
{
    internal string Id { get; } = id;
    internal string Name { get; } = name;
    internal PackField[] Fields { get; set; } = [];
    private int? _size;
    internal int Size => _size ??= Fields.Sum(packField => packField.Size);
}

internal sealed class PackField(PackFieldKind kind, string name, int count)
{
    internal PackFieldKind Kind { get; } = kind;
    internal string Name { get; } = name;
    internal int Count { get; } = count;
    internal PackType? Reference { get; set; }
    internal PackType[] Variants { get; set; } = [];

    internal int Size => Kind switch
    {
        PackFieldKind.FixedArray => Count * Reference!.Size,
        PackFieldKind.Array or PackFieldKind.PointerArray or PackFieldKind.Variant => 12,
        PackFieldKind.TypedArray => 20,
        PackFieldKind.Struct or PackFieldKind.StructBase => Reference!.Size,
        PackFieldKind.WordArray or PackFieldKind.WordPointerArray => 10,
        PackFieldKind.WordTypedArray => 18,
        PackFieldKind.ByteArray or PackFieldKind.BytePointerArray => 9,
        PackFieldKind.ByteTypedArray => 17,
        PackFieldKind.Byte => 1,
        PackFieldKind.Word => 2,
        PackFieldKind.Byte3 => 3,
        PackFieldKind.Byte4 or PackFieldKind.Dword or PackFieldKind.Float or PackFieldKind.DwordToken => 4,
        PackFieldKind.Word3 => 6,
        PackFieldKind.Float3 => 12,
        PackFieldKind.Double2 or PackFieldKind.Float4 or PackFieldKind.Uuid or PackFieldKind.Dword4 => 16,
        PackFieldKind.Double3 => 24,
        _ => 8,
    };
}

internal sealed class PackRegistration
{
    internal Dictionary<ushort, PackType> Versions { get; } = [];
    internal HashSet<ushort> SkippedVersions { get; } = [];
}

internal sealed class PackfileSchema
{
    private const int RegistrationHeaderSize = 16;
    private const int VersionEntrySize = 24;
    private const int FieldRecordSize = 32;
    private const ulong KindMask = 0xFF;
    private const int MaxFields = 512;
    private const uint MaxVersions = 256;
    private static readonly HashSet<PackFieldKind> StructureKinds =
        [PackFieldKind.FixedArray, PackFieldKind.Array, PackFieldKind.PointerArray, PackFieldKind.Pointer, PackFieldKind.Struct, PackFieldKind.StructBase];
    private readonly Dictionary<uint, List<PackRegistration>> _registrations = [];

    private static PackfileSchema? _default;

    internal IEnumerable<(uint ChunkType, PackRegistration Registration)> Registrations =>
        _registrations.SelectMany(pair => pair.Value.Select(registration => (pair.Key, registration)));

    internal static PackfileSchema Default => _default ??= Extract(ExecutableImage.Current);

    private static readonly Dictionary<uint, string> RegistrationFamilyByFileType = new()
    {
        [FourCc("MODL")] = "ModelFile",
        [FourCc("AMAT")] = "Amat",
        [FourCc("cntc")] = "PackContent",
        [FourCc("mMet")] = "PackMapMetadata",
        [FourCc("prlt")] = "ContentPortalManifest",
        [FourCc("ARMF")] = "PackAssetManifest",
        [FourCc("emoc")] = "PackEmoteAnimations",
    };

    private static readonly (uint ChunkType, ushort Version, string TypeName)[] UnregisteredChunks =
    [
        (FourCc("scal"), 0, "PackCompositeBoneScaleV20"),
    ];

    internal (ushort Version, PackType Type)? Latest(uint chunkType, ushort fileVersion, uint fileType = 0)
    {
        if (Registration(chunkType, fileVersion, fileType) is not { } registration) return null;
        ushort latest = registration.Versions.Keys.Max();
        return (latest, registration.Versions[latest]);
    }

    internal PackType? Type(uint chunkType, ushort version, uint fileType = 0) =>
        Registration(chunkType, version, fileType) is { } registration ? registration.Versions[version] : null;

    internal PackRegistration? Registration(uint chunkType, ushort version, uint fileType)
    {
        if (!_registrations.TryGetValue(chunkType, out var registrations)) return null;
        PackRegistration? first = null;
        foreach (var registration in registrations)
        {
            if (!registration.Versions.ContainsKey(version)) continue;
            first ??= registration;
            if (RegistrationFamilyByFileType.TryGetValue(fileType, out string? family) && registration.Versions[version].Name.StartsWith(family, StringComparison.Ordinal))
                return registration;
        }
        return first;
    }

    internal static uint FourCc(string text) => (uint)(text[0] | text[1] << 8 | text[2] << 16 | text[3] << 24);

    internal static PackfileSchema Extract(ExecutableImage executable)
    {
        var registrations = new List<(uint ChunkType, PackRegistration Registration)>();
        var types = new Dictionary<ulong, PackType>();
        var (rdataStart, rdataSize) = executable.Section(".rdata");
        for (ulong address = rdataStart; address + RegistrationHeaderSize <= rdataStart + rdataSize; address += 8)
            if (ReadRegistration(executable, address, types) is { } registration)
                registrations.Add(registration);
        if (registrations.Count == 0) throw new InvalidOperationException("The executable has no pack registrations in .rdata.");

        var schema = new PackfileSchema();
        foreach (var (chunkType, registration) in registrations)
        {
            if (!schema._registrations.TryGetValue(chunkType, out var list)) schema._registrations[chunkType] = list = [];
            list.Add(registration);
        }
        foreach (var (chunkType, version, typeName) in UnregisteredChunks)
            if (!schema._registrations.ContainsKey(chunkType) && types.Values.FirstOrDefault(type => type.Name == typeName) is { } type)
            {
                var packRegistration = new PackRegistration();
                packRegistration.Versions[version] = type;
                schema._registrations[chunkType] = [packRegistration];
            }
        return schema;
    }

    private static (uint ChunkType, PackRegistration Registration)? ReadRegistration(ExecutableImage executable, ulong address, Dictionary<ulong, PackType> types)
    {
        var header = executable.Bytes(address, RegistrationHeaderSize);
        if (header.Length < RegistrationHeaderSize) return null;
        var chunk = header[..4].TrimEnd((byte)0);
        if (chunk.Length < 3) return null;
        foreach (byte character in chunk)
            if (character is < 0x30 or >= 0x7B || !char.IsAsciiLetterOrDigit((char)character)) return null;
        uint versionCount = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
        ulong table = BinaryPrimitives.ReadUInt64LittleEndian(header[8..]);
        if (versionCount is 0 or > MaxVersions || !executable.InSection(table, ".rdata")) return null;

        var roots = new List<(ushort Version, ulong Root, bool Skipped)>();
        for (uint version = 0; version < versionCount; version++)
        {
            ulong entry = table + VersionEntrySize * version;
            ulong root = executable.Qword(entry);
            if (root == 0) continue;
            if (ReadFields(executable, root) is null) return null;
            roots.Add(((ushort)version, root, (executable.Qword(entry + 16) & 0xFFFFFFFF) != 0));
        }
        if (roots.Count == 0) return null;

        var registration = new PackRegistration();
        foreach (var (version, root, skipped) in roots)
        {
            registration.Versions[version] = ResolveType(executable, root, types)!;
            if (skipped) registration.SkippedVersions.Add(version);
        }
        return (BinaryPrimitives.ReadUInt32LittleEndian(header), registration);
    }

    private static PackType? ResolveType(ExecutableImage executable, ulong address, Dictionary<ulong, PackType> types)
    {
        if (types.TryGetValue(address, out var known)) return known;
        if (ReadFields(executable, address) is not var (name, records)) return null;
        var type = new PackType(address.ToString("X"), name);
        types[address] = type;
        var fields = new PackField[records.Count];
        for (int index = 0; index < records.Count; index++)
        {
            var (kind, fieldName, typeAddress, count) = records[index];
            var field = fields[index] = new PackField(kind, fieldName, count);
            if (kind == PackFieldKind.Variant)
                field.Variants = VariantMembers(executable, typeAddress, count)
                    .Select(member => ResolveType(executable, member, types))
                    .OfType<PackType>().ToArray();
            else if (typeAddress != 0 && StructureKinds.Contains(kind))
                field.Reference = ResolveType(executable, typeAddress, types);
        }
        type.Fields = fields;
        return type;
    }

    private static IEnumerable<ulong> VariantMembers(ExecutableImage executable, ulong address, int count)
    {
        for (int index = 0; index < count; index++)
            if (executable.Qword(address + 8 * (ulong)index) is var member and not 0)
                yield return member;
    }

    private static (string Name, List<(PackFieldKind Kind, string Name, ulong TypeAddress, int Count)> Records)? ReadFields(ExecutableImage executable, ulong address)
    {
        if (!executable.InSection(address, ".rdata") && !executable.InSection(address, ".data")) return null;
        var records = new List<(PackFieldKind, string, ulong, int)>();
        for (int index = 0; index < MaxFields; index++)
        {
            var record = executable.Bytes(address + FieldRecordSize * (ulong)index, FieldRecordSize);
            if (record.Length < FieldRecordSize) return null;
            ulong kind = BinaryPrimitives.ReadUInt64LittleEndian(record) & KindMask;
            ulong nameAddress = BinaryPrimitives.ReadUInt64LittleEndian(record[8..]);
            if (nameAddress == 0 || executable.AsciiName(nameAddress) is not { } name) return null;
            if (kind == 0) return (name, records);
            records.Add(((PackFieldKind)kind, name, BinaryPrimitives.ReadUInt64LittleEndian(record[16..]), (int)BinaryPrimitives.ReadUInt64LittleEndian(record[24..])));
        }
        return null;
    }
}
