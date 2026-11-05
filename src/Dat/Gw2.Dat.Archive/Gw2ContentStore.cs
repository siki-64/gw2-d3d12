using Gw2.Formats;
namespace Gw2.Dat.Archive;

public sealed record Gw2ContentTypeInfo(
    int GuidOffset,
    int UidOffset,
    int DataIdOffset,
    int NameOffset,
    byte TrackReferences);

public sealed record Gw2ContentNamespace(string Name, uint Domain, int ParentIndex);

public sealed record Gw2ContentEntry(int TypeIndex, int Offset, int NamespaceIndex, int RootIndex);

public sealed record Gw2ContentExternalOffsetFixup(int RelocOffset, int TargetFileIndex);

public sealed record Gw2ContentTrackedReference(int SourceOffset, int TargetFileIndex, int TargetOffset);

public sealed class Gw2ContentStore
{
    private Gw2ContentStore(
        uint flags,
        IReadOnlyList<Gw2ContentTypeInfo> typeInfos,
        IReadOnlyList<Gw2ContentNamespace> namespaces,
        IReadOnlyList<Gw2ContentEntry> entries,
        IReadOnlyList<string> strings,
        ReadOnlyMemory<byte> content)
    {
        Flags = flags;
        TypeInfos = typeInfos;
        Namespaces = namespaces;
        Entries = entries;
        Strings = strings;
        Content = content;
    }

    public uint Flags { get; }
    public IReadOnlyList<Gw2ContentTypeInfo> TypeInfos { get; }
    public IReadOnlyList<Gw2ContentNamespace> Namespaces { get; }
    public IReadOnlyList<Gw2ContentEntry> Entries { get; }
    public IReadOnlyList<string> Strings { get; }
    public ReadOnlyMemory<byte> Content { get; }
    public IReadOnlyList<uint> FileIds { get; init; } = [];
    public IReadOnlyList<int> LocalOffsetFixups { get; init; } = [];
    public IReadOnlyList<Gw2ContentExternalOffsetFixup> ExternalOffsetFixups { get; init; } = [];
    public IReadOnlyList<int> FileIndexFixups { get; init; } = [];
    public IReadOnlyList<int> StringIndexFixups { get; init; } = [];
    public IReadOnlyList<Gw2ContentTrackedReference> TrackedReferences { get; init; } = [];

    public ReadOnlyMemory<byte> EntryBytes(Gw2ContentEntry entry, int length)
    {
        if (entry.Offset < 0 || length < 0 || entry.Offset > Content.Length - length)
            throw new ArgumentOutOfRangeException(nameof(entry));
        return Content.Slice(entry.Offset, length);
    }

    public static Gw2ContentStore? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var root = document.Chunks
            .Select(chunk => chunk.Root)
            .FirstOrDefault(candidate => candidate is not null && candidate.TypeName == "PackContent");
        if (root is null) return null;

        var typeInfos = new List<Gw2ContentTypeInfo>();
        if (root.Fields.GetValueOrDefault("typeInfos") is object?[] typeObjects)
            foreach (var item in typeObjects)
                if (item is PackObject type)
                    typeInfos.Add(new(
                        I32(type.Fields.GetValueOrDefault("guidOffset")),
                        I32(type.Fields.GetValueOrDefault("uidOffset")),
                        I32(type.Fields.GetValueOrDefault("dataIdOffset")),
                        I32(type.Fields.GetValueOrDefault("nameOffset")),
                        (byte)U32(type.Fields.GetValueOrDefault("trackReferences"))));

        var namespaces = new List<Gw2ContentNamespace>();
        if (root.Fields.GetValueOrDefault("namespaces") is object?[] namespacesObjects)
            foreach (var item in namespacesObjects)
                if (item is PackObject space)
                    namespaces.Add(new(
                        space.Fields.GetValueOrDefault("name") as string ?? string.Empty,
                        U32(space.Fields.GetValueOrDefault("domain")),
                        I32(space.Fields.GetValueOrDefault("parentIndex"))));

        var entries = new List<Gw2ContentEntry>();
        if (root.Fields.GetValueOrDefault("indexEntries") is object?[] entryObjects)
            foreach (var item in entryObjects)
                if (item is PackObject entry)
                    entries.Add(new(
                        I32(entry.Fields.GetValueOrDefault("type")),
                        I32(entry.Fields.GetValueOrDefault("offset")),
                        I32(entry.Fields.GetValueOrDefault("namespaceIndex")),
                        I32(entry.Fields.GetValueOrDefault("rootIndex"))));

        var strings = new List<string>();
        if (root.Fields.GetValueOrDefault("strings") is object?[] stringObjects)
            foreach (var item in stringObjects) strings.Add(item as string ?? string.Empty);

        var content = Blob(root.Fields.GetValueOrDefault("content"));
        return new(U32(root.Fields.GetValueOrDefault("flags")), typeInfos, namespaces, entries, strings, content)
        {
            FileIds = Items(root, "fileRefs")
                .Select(item => item is PackFileReference reference && reference.TryGetFileId(out var fileId) ? fileId : 0u)
                .ToArray(),
            LocalOffsetFixups = RelocOffsets(root, "localOffsets"),
            ExternalOffsetFixups = Objects(root, "externalOffsets")
                .Select(fixup => new Gw2ContentExternalOffsetFixup(
                    I32(fixup.Optional("relocOffset")),
                    I32(fixup.Optional("targetFileIndex"))))
                .ToArray(),
            FileIndexFixups = RelocOffsets(root, "fileIndices"),
            StringIndexFixups = RelocOffsets(root, "stringIndices"),
            TrackedReferences = Objects(root, "trackedReferences")
                .Select(reference => new Gw2ContentTrackedReference(
                    I32(reference.Optional("sourceOffset")),
                    I32(reference.Optional("targetFileIndex")),
                    I32(reference.Optional("targetOffset"))))
                .ToArray(),
        };
    }

    private static object?[] Items(PackObject root, string name) =>
        root.Optional(name) as object?[] ?? [];

    private static IEnumerable<PackObject> Objects(PackObject root, string name) =>
        Items(root, name).OfType<PackObject>();

    private static int[] RelocOffsets(PackObject root, string name) =>
        Objects(root, name).Select(fixup => I32(fixup.Optional("relocOffset"))).ToArray();

    private static ReadOnlyMemory<byte> Blob(object? value) => value switch
    {
        ReadOnlyMemory<byte> memory => memory,
        byte[] bytes => bytes,
        _ => ReadOnlyMemory<byte>.Empty,
    };

    private static int I32(object? value) => unchecked((int)U32(value));

    private static uint U32(object? value) => value switch
    {
        uint unsigned => unsigned,
        int signed => unchecked((uint)signed),
        ulong wide => (uint)wide,
        ushort shortValue => shortValue,
        byte byteValue => byteValue,
        _ => 0,
    };
}
