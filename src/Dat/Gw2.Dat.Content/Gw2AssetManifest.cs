using Gw2.Formats;
namespace Gw2.Dat.Content;

public sealed record Gw2AssetManifestFile(uint BaseId, uint FileId, uint Size, uint Flags, string Name);

public sealed record Gw2AssetExtraFile(uint BaseId, uint FileId, uint Size, uint FileType);

public sealed class Gw2AssetRootManifest
{
    public Gw2AssetRootManifest(uint buildId, IReadOnlyList<Gw2AssetManifestFile> manifests, IReadOnlyList<Gw2AssetExtraFile> extraFiles)
    {
        BuildId = buildId;
        Manifests = manifests;
        ExtraFiles = extraFiles;
    }

    public uint BuildId { get; }
    public IReadOnlyList<Gw2AssetManifestFile> Manifests { get; }
    public IReadOnlyList<Gw2AssetExtraFile> ExtraFiles { get; }

    public static Gw2AssetRootManifest? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.Chunks
            .Select(chunk => chunk.Root)
            .FirstOrDefault(candidate => candidate is not null && candidate.TypeName == "PackAssetRootManifest");
        if (root is null) return null;

        var manifests = new List<Gw2AssetManifestFile>();
        if (root.Fields.GetValueOrDefault("manifests") is object?[] manifestObjects)
            foreach (var manifestObject in manifestObjects)
                if (manifestObject is PackObject manifest)
                    manifests.Add(new(
                        PackValue.AsU32(manifest.Fields.GetValueOrDefault("baseId")),
                        PackValue.AsU32(manifest.Fields.GetValueOrDefault("fileId")),
                        PackValue.AsU32(manifest.Fields.GetValueOrDefault("size")),
                        PackValue.AsU32(manifest.Fields.GetValueOrDefault("flags")),
                        manifest.Fields.GetValueOrDefault("name") as string ?? string.Empty));

        var extraFiles = new List<Gw2AssetExtraFile>();
        if (root.Fields.GetValueOrDefault("extraFiles") is object?[] extraObjects)
            foreach (var extraObject in extraObjects)
                if (extraObject is PackObject extra)
                    extraFiles.Add(new(
                        PackValue.AsU32(extra.Fields.GetValueOrDefault("baseId")),
                        PackValue.AsU32(extra.Fields.GetValueOrDefault("fileId")),
                        PackValue.AsU32(extra.Fields.GetValueOrDefault("size")),
                        PackValue.AsU32(extra.Fields.GetValueOrDefault("fileType"))));

        return new(PackValue.AsU32(root.Fields.GetValueOrDefault("buildId")), manifests, extraFiles);
    }
}

public sealed record Gw2AssetManifestRecord(uint BaseId, uint FileId, uint Size, uint Flags);

public sealed record Gw2AssetManifestStream(uint ParentBaseId, uint StreamBaseId);

public sealed record Gw2AssetManifestProperty(uint Type, byte[] Data);

public sealed record Gw2AssetManifestPropertyIndex(uint BaseId, uint PropertyIndex);

public sealed class Gw2AssetManifest
{
    public Gw2AssetManifest(
        uint buildId,
        ulong totalRecordSize,
        IReadOnlyList<Gw2AssetManifestRecord> records,
        IReadOnlyList<Gw2AssetManifestStream> streams,
        IReadOnlyList<Gw2AssetManifestProperty> properties,
        IReadOnlyList<Gw2AssetManifestPropertyIndex> propertyTable)
    {
        BuildId = buildId;
        TotalRecordSize = totalRecordSize;
        Records = records;
        Streams = streams;
        Properties = properties;
        PropertyTable = propertyTable;
    }

    public uint BuildId { get; }
    public ulong TotalRecordSize { get; }
    public IReadOnlyList<Gw2AssetManifestRecord> Records { get; }
    public IReadOnlyList<Gw2AssetManifestStream> Streams { get; }
    public IReadOnlyList<Gw2AssetManifestProperty> Properties { get; }
    public IReadOnlyList<Gw2AssetManifestPropertyIndex> PropertyTable { get; }

    public static Gw2AssetManifest? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.FindRoot("PackAssetManifest");
        if (root is null) return null;

        return new(
            PackValue.AsU32(root.Fields.GetValueOrDefault("buildId")),
            PackValue.AsU64(root.Fields.GetValueOrDefault("totalRecordSize")),
            Rows(root, "records", row => new Gw2AssetManifestRecord(
                PackValue.AsU32(row.Fields.GetValueOrDefault("baseId")),
                PackValue.AsU32(row.Fields.GetValueOrDefault("fileId")),
                PackValue.AsU32(row.Fields.GetValueOrDefault("size")),
                PackValue.AsU32(row.Fields.GetValueOrDefault("flags")))),
            Rows(root, "streams", row => new Gw2AssetManifestStream(
                PackValue.AsU32(row.Fields.GetValueOrDefault("parentBaseId")),
                PackValue.AsU32(row.Fields.GetValueOrDefault("streamBaseId")))),
            Rows(root, "properties", row => new Gw2AssetManifestProperty(
                PackValue.AsU32(row.Fields.GetValueOrDefault("type")),
                PackValue.AsBytes(row.Fields.GetValueOrDefault("data")))),
            Rows(root, "propertyTable", row => new Gw2AssetManifestPropertyIndex(
                PackValue.AsU32(row.Fields.GetValueOrDefault("baseId")),
                PackValue.AsU32(row.Fields.GetValueOrDefault("properyIndex")))));
    }

    private static IReadOnlyList<T> Rows<T>(PackObject root, string field, Func<PackObject, T> read) =>
        PackValue.AsObjects(root.Fields.GetValueOrDefault(field)).Select(read).ToArray();
}
