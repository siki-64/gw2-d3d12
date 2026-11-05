using Gw2.Formats;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Text;
using Gw2.Dat.Content;
using Gw2.Dat.Archive;

namespace Gw2.DatSurvey;

public sealed record SurveyChunk(string Type, ushort Version, string? Root, string Parse);

public sealed record SurveyEntry(
    uint Record,
    IReadOnlyList<uint> FileIds,
    uint Size,
    ushort Compression,
    ushort Flags,
    string Decode,
    long DecodedSize,
    string Kind,
    string? Magic,
    string? PackType,
    IReadOnlyList<SurveyChunk>? Chunks,
    IReadOnlyList<SurveyDecode>? Decoders = null);

public sealed record SurveyFieldRead(string Decoder, string TypeName, string Field);

public sealed record SurveyRun(IReadOnlyList<SurveyEntry> Entries, IReadOnlyCollection<SurveyFieldRead> FieldReads,
    IReadOnlyCollection<SurveyFieldValues>? FieldValues = null);

public static class CoverageSurvey
{
    public const string ParseOk = "ok";
    public const string NoSchema = "no-schema";
    public const string DecodeOk = "ok";
    private const uint FirstContentRecord = 15;

    public static string? Magic(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 4) return null;
        var builder = new StringBuilder(4);
        foreach (var value in bytes[..4]) builder.Append(value is >= 0x20 and < 0x7F ? (char)value : '.');
        return builder.ToString();
    }

    public static IReadOnlyList<SurveyChunk> SurveyPack(Gw2PackFile pack, Gw2PackSchemaCatalog catalog) =>
        SurveyPack(pack, catalog, [], null);

    private static IReadOnlyList<SurveyChunk> SurveyPack(Gw2PackFile pack, Gw2PackSchemaCatalog catalog,
        List<Gw2ParsedChunk> parsed, SurveyFieldCensus? census)
    {
        var reader = new Gw2PackReader(catalog);
        var chunks = new List<SurveyChunk>(pack.Chunks.Count);
        foreach (var chunk in pack.Chunks)
        {
            var roots = catalog.FindRoots(chunk.Type, chunk.Version);
            if (roots.Count == 0)
            {
                parsed.Add(new(chunk, null, NoSchema));
                chunks.Add(new(chunk.Type, chunk.Version, null, NoSchema));
                continue;
            }
            string parse;
            string root = string.Join("|", roots.Select(r => r.Name));
            try
            {
                var result = reader.Read(pack, chunk, Gw2PackReadLimits.Composite);
                root = result.TypeName;
                parse = ParseOk;
                parsed.Add(new(chunk, result, null));
                census?.Add(result);
            }
            catch (Exception exception)
            {
                parse = $"{exception.GetType().Name}: {exception.Message}";
                parsed.Add(new(chunk, null, parse));
            }
            chunks.Add(new(chunk.Type, chunk.Version, root, parse));
        }
        return chunks;
    }

    public static SurveyEntry SurveyEntryBytes(uint record, IReadOnlyList<uint> fileIds, MftEntry entry,
        byte[] decoded, Gw2PackSchemaCatalog catalog) =>
        SurveyEntryBytes(record, fileIds, entry, decoded, catalog, [], static (_, _) => { });

    public static SurveyEntry SurveyEntryBytes(uint record, IReadOnlyList<uint> fileIds, MftEntry entry,
        byte[] decoded, Gw2PackSchemaCatalog catalog, IReadOnlyList<Gw2PackDecoder> decoders,
        Action<string, IReadOnlyCollection<(string TypeName, string Field)>> reads, SurveyFieldCensus? census = null)
    {
        var kind = record < FirstContentRecord ? "archive" : Gw2DatFileKind.Classify(decoded);
        string? packType = null;
        IReadOnlyList<SurveyChunk>? chunks = null;
        IReadOnlyList<SurveyDecode>? decodes = null;
        if (kind == "pack")
        {
            try
            {
                var pack = Gw2PackFile.Parse(decoded);
                packType = pack.Type;
                var parsed = new List<Gw2ParsedChunk>(pack.Chunks.Count);
                chunks = SurveyPack(pack, catalog, parsed, census);
                if (decoders.Count > 0)
                    decodes = SurveyDecoders.Run(new Gw2AssetDocument(pack, parsed), decoders, reads) is { Count: > 0 } found ? found : null;
            }
            catch (Exception exception)
            {
                kind = "pack-invalid";
                packType = $"{exception.GetType().Name}: {exception.Message}";
            }
        }
        return new(record, fileIds, entry.Size, entry.Compression, entry.Flags, DecodeOk, decoded.LongLength, kind,
            Magic(decoded), packType, chunks, decodes);
    }

    public static SurveyRun Run(string datPath, bool verify, Gw2PackSchemaCatalog catalog,
        IReadOnlyList<Gw2PackDecoder> decoders, int parallelism, TextWriter progress)
    {
        var fieldReads = new ConcurrentDictionary<SurveyFieldRead, byte>();
        var census = new SurveyFieldCensus();
        void Reads(string decoder, IReadOnlyCollection<(string TypeName, string Field)> accessed)
        {
            foreach (var (typeName, field) in accessed) fieldReads.TryAdd(new(decoder, typeName, field), 0);
        }
        IReadOnlyDictionary<uint, uint> fileIndex;
        uint recordCount;
        using (var archive = FileArchive.Open(datPath, verify))
        {
            fileIndex = archive.FileIndex;
            recordCount = archive.MftEntryCount;
        }
        var fileIdsByRecord = fileIndex.GroupBy(pair => pair.Value)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<uint>)group.Select(pair => pair.Key).Order().ToArray());
        var results = new ConcurrentBag<SurveyEntry>();
        var archives = new ThreadLocal<FileArchive>(() => FileArchive.Open(datPath, verify), trackAllValues: true);
        var completed = 0;
        try
        {
            Parallel.For(1L, recordCount, new ParallelOptions { MaxDegreeOfParallelism = parallelism }, index =>
            {
                var record = (uint)index;
                var archive = archives.Value!;
                var fileIds = fileIdsByRecord.GetValueOrDefault(record) ?? [];
                MftEntry entry;
                try { entry = archive.ReadMftEntry(record); }
                catch (Exception exception)
                {
                    results.Add(new(record, fileIds, 0, 0, 0, $"mft: {exception.Message}", 0, "invalid", null, null, null));
                    return;
                }
                if (entry.Size == 0 || entry.Offset == 0)
                {
                    results.Add(new(record, fileIds, entry.Size, entry.Compression, entry.Flags, DecodeOk, 0, "unused", null, null, null));
                    return;
                }
                byte[] decoded;
                try
                {
                    using var buffer = new MemoryStream();
                    archive.DecodeEntry(record, buffer);
                    decoded = buffer.ToArray();
                }
                catch (Exception exception)
                {
                    results.Add(new(record, fileIds, entry.Size, entry.Compression, entry.Flags,
                        $"{exception.GetType().Name}: {exception.Message}", 0, "decode-failed", null, null, null));
                    return;
                }
                results.Add(SurveyEntryBytes(record, fileIds, entry, decoded, catalog, decoders, Reads, census));
                var done = Interlocked.Increment(ref completed);
                if (done % 50000 == 0) progress.WriteLine($"survey: {done} of {recordCount} records");
            });
        }
        finally
        {
            foreach (var archive in archives.Values) archive.Dispose();
            archives.Dispose();
        }
        var entries = results.OrderBy(entry => entry.Record).ToArray();
        using (var archive = FileArchive.Open(datPath, verify))
            entries = Relabel(entries, GlyphStreamRecords(archive, entries, fileIndex), Gw2DatFileKind.GlyphStream);
        return new(entries, fieldReads.Keys.ToArray(), census.Values);
    }

    public static IReadOnlySet<uint> GlyphStreamRecords(FileArchive archive, IEnumerable<SurveyEntry> entries,
        IReadOnlyDictionary<uint, uint> fileIndex)
    {
        var records = new HashSet<uint>();
        foreach (var manifest in entries.Where(entry => entry.PackType == Gw2FontManifest.PackType))
        {
            using var buffer = new MemoryStream();
            archive.DecodeEntry(manifest.Record, buffer);
            foreach (var fileId in Gw2FontManifest.Parse(buffer.ToArray()).SelectMany(font => font.RangeFileIds))
                if (fileId is { } id && fileIndex.TryGetValue(id, out var record)) records.Add(record);
        }
        return records;
    }

    public static SurveyEntry[] Relabel(IEnumerable<SurveyEntry> entries, IReadOnlySet<uint> records, string kind) =>
        entries.Select(entry => entry.Kind == Gw2DatFileKind.Unknown && records.Contains(entry.Record) ? entry with { Kind = kind } : entry)
            .ToArray();
}
