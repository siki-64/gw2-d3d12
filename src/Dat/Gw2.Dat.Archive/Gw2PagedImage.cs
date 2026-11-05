using Gw2.Formats;
namespace Gw2.Dat.Archive;

public sealed record Gw2PagedImageLayer(
    IReadOnlyList<uint> RawDims,
    IReadOnlyList<uint> StrippedDims,
    uint RawFormat,
    uint StrippedFormat,
    uint DiskFormat);

public sealed record Gw2PagedImagePage(
    uint Layer,
    IReadOnlyList<uint> Coord,
    PackFileReference Reference,
    uint Flags,
    uint SolidColor)
{
    public bool TryGetFileId(out uint fileId) => Reference.TryGetFileId(out fileId);
}

public sealed record Gw2PagedImageEmbeddedPage(uint Layer, IReadOnlyList<uint> Coord, byte[] Data);

public sealed class Gw2PagedImage
{
    public Gw2PagedImage(
        IReadOnlyList<Gw2PagedImageLayer> layers,
        IReadOnlyList<Gw2PagedImagePage> strippedPages,
        IReadOnlyList<Gw2PagedImagePage> rawPages,
        IReadOnlyList<Gw2PagedImageEmbeddedPage> embeddedPages,
        uint flags)
    {
        Layers = layers;
        StrippedPages = strippedPages;
        RawPages = rawPages;
        EmbeddedPages = embeddedPages;
        Flags = flags;
    }

    public IReadOnlyList<Gw2PagedImageLayer> Layers { get; }
    public IReadOnlyList<Gw2PagedImagePage> StrippedPages { get; }
    public IReadOnlyList<Gw2PagedImagePage> RawPages { get; }
    public IReadOnlyList<Gw2PagedImageEmbeddedPage> EmbeddedPages { get; }
    public uint Flags { get; }

    public static Gw2PagedImage? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var roots = document.Chunks.Select(chunk => chunk.Root).Where(root => root is not null).ToList();
        var table = roots.FirstOrDefault(candidate =>
                candidate!.TypeName.StartsWith("PagedImageTableData", StringComparison.Ordinal))
            ?? roots.Select(root => root!.Optional("tableData"))
                .OfType<PackObject>()
                .FirstOrDefault(candidate => candidate.TypeName.StartsWith("PagedImageTableData", StringComparison.Ordinal));
        if (table is null) return null;

        var data = roots.FirstOrDefault(candidate =>
                candidate!.TypeName.StartsWith("PagedImageEmbeddedPagesData", StringComparison.Ordinal))
            ?? roots.Select(root => root!.Optional("pageData"))
                .OfType<PackObject>()
                .FirstOrDefault(candidate => candidate.TypeName.StartsWith("PagedImageEmbeddedPagesData", StringComparison.Ordinal));

        var layers = new List<Gw2PagedImageLayer>();
        if (table.Optional("layers") is object?[] layerObjects)
        {
            foreach (var layerObject in layerObjects)
                if (layerObject is PackObject layer)
                    layers.Add(new(
                        UIntList(layer.Optional("rawDims")),
                        UIntList(layer.Optional("strippedDims") ?? layer.Optional("dims")),
                        AsU32(layer.Optional("rawFormat")),
                        AsU32(layer.Optional("strippedFormat")),
                        AsU32(layer.Optional("diskFormat"))));
        }

        var rawPages = ReadPages(table, "rawPages");
        if (rawPages.Count == 0) rawPages = ReadPages(table, "pages");

        return new(
            layers,
            ReadPages(table, "strippedPages"),
            rawPages,
            ReadEmbedded(data),
            AsU32(table.Optional("flags")));
    }

    public IEnumerable<uint> EnumerateDependencies()
    {
        var seen = new HashSet<uint>();
        foreach (var page in StrippedPages)
            if (page.TryGetFileId(out var id) && seen.Add(id)) yield return id;
        foreach (var page in RawPages)
            if (page.TryGetFileId(out var id) && seen.Add(id)) yield return id;
    }

    private static IReadOnlyList<Gw2PagedImagePage> ReadPages(PackObject table, string field)
    {
        if (table.Optional(field) is not object?[] pages) return [];
        var result = new List<Gw2PagedImagePage>(pages.Length);
        foreach (var pageObject in pages)
        {
            if (pageObject is not PackObject page) continue;
            result.Add(new(
                AsU32(page.Optional("layer")),
                UIntList(page.Optional("coord")),
                Reference(page, "filename"),
                AsU32(page.Optional("flags")),
                AsU32(page.Optional("solidColor"))));
        }
        return result;
    }

    private static IReadOnlyList<Gw2PagedImageEmbeddedPage> ReadEmbedded(PackObject? data)
    {
        if (data is null) return [];
        var result = new List<Gw2PagedImageEmbeddedPage>();
        foreach (var field in new[] { "rawPages", "strippedPages", "pages" })
        {
            if (data.Optional(field) is not object?[] pages) continue;
            foreach (var pageObject in pages)
            {
                if (pageObject is not PackObject page) continue;
                var blob = page.Optional("data") ?? page.Optional("rawData") ?? page.Optional("compressedData");
                result.Add(new(
                    AsU32(page.Optional("layer")),
                    UIntList(page.Optional("coord")),
                    Blob(blob)));
            }
        }
        return result;
    }

    private static IReadOnlyList<uint> UIntList(object? value)
    {
        if (value is uint[] typed) return typed;
        if (value is not object?[] values) return [];
        var result = new uint[values.Length];
        for (var i = 0; i < result.Length; i++) result[i] = AsU32(values[i]);
        return result;
    }

    private static PackFileReference Reference(PackObject obj, string name) =>
        obj.Optional(name) is PackFileReference reference ? reference : default;

    private static byte[] Blob(object? value) => value is ReadOnlyMemory<byte> memory ? memory.ToArray() : [];

    private static uint AsU32(object? value) => value switch
    {
        uint unsigned => unsigned,
        int signed => (uint)signed,
        ulong wide => (uint)wide,
        ushort shortValue => shortValue,
        byte byteValue => byteValue,
        _ => 0,
    };
}
