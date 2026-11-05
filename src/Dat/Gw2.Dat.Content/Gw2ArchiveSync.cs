using Gw2.Formats;
using Gw2.Dat.Identity;

namespace Gw2.Dat.Content;

public sealed record Gw2ArchiveSizeMismatch(Gw2ManifestEntry Entry, ulong StoredSize);

public sealed record Gw2ArchiveSyncReport(
    uint BuildId,
    int Present,
    IReadOnlyList<Gw2ManifestEntry> Missing,
    IReadOnlyList<Gw2ManifestEntry> Stale,
    IReadOnlyList<Gw2ArchiveSizeMismatch> WrongSize)
{
    public bool IsValid => Missing.Count == 0 && Stale.Count == 0 && WrongSize.Count == 0;
}

public static class Gw2ArchiveSync
{
    public static Gw2AssetRootManifest? FindRootManifest(FileArchive local)
    {
        ArgumentNullException.ThrowIfNull(local);
        Gw2AssetRootManifest? latest = null;
        foreach (var recordIndex in local.FileIndex.Values.Distinct())
        {
            if (!IsAssetManifestPack(local, recordIndex)) continue;
            if (Gw2PackDecoders.Decode<Gw2AssetRootManifest>(ReadDocument(local, recordIndex)) is { } root
                && (latest is null || root.BuildId > latest.BuildId))
                latest = root;
        }
        return latest;
    }

    public static Gw2ManifestIds ReadManifestIds(FileArchive local)
    {
        var root = FindRootManifest(local) ?? throw new InvalidDataException("No root asset manifest in the archive");
        return ReadManifestIds(local, root);
    }

    public static Gw2ManifestIds ReadManifestIds(FileArchive local, Gw2AssetRootManifest root) =>
        ToManifestIds(root.BuildId, ReadManifests(local, root));

    public static IEnumerable<Gw2AssetManifest> ReadManifests(FileArchive local, Gw2AssetRootManifest root)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(root);
        return root.Manifests.Select(file =>
        {
            if (!local.FileIndex.TryGetValue(file.FileId, out var recordIndex))
                throw new InvalidDataException($"Manifest {Gw2ArchiveIds.Format(file.FileId)} ({file.Name}) is missing");
            return Gw2PackDecoders.Decode<Gw2AssetManifest>(ReadDocument(local, recordIndex))
                ?? throw new InvalidDataException($"Manifest {Gw2ArchiveIds.Format(file.FileId)} ({file.Name}) is invalid");
        });
    }

    public static Gw2ManifestIds ToManifestIds(uint buildId, IEnumerable<Gw2AssetManifest> manifests) =>
        new(buildId, manifests.SelectMany(manifest => manifest.Records).Select(record =>
            new Gw2ManifestEntry(new Gw2BaseId(record.BaseId), new Gw2FileId(record.FileId), record.Size)));

    public static Gw2ArchiveSyncReport Check(FileArchive archive, Gw2ManifestIds manifestIds)
    {
        ArgumentNullException.ThrowIfNull(archive);
        return Check(archive.FileIndex, archive.ReadDecodedSize, manifestIds);
    }

    public static Gw2ArchiveSyncReport Check(
        IReadOnlyDictionary<uint, uint> recordByArchiveId,
        Func<uint, ulong> readDecodedSize,
        Gw2ManifestIds manifestIds)
    {
        ArgumentNullException.ThrowIfNull(recordByArchiveId);
        ArgumentNullException.ThrowIfNull(readDecodedSize);
        ArgumentNullException.ThrowIfNull(manifestIds);
        var present = 0;
        var missing = new List<Gw2ManifestEntry>();
        var stale = new List<Gw2ManifestEntry>();
        var wrongSize = new List<Gw2ArchiveSizeMismatch>();
        foreach (var entry in manifestIds.Entries.OrderBy(entry => entry.BaseId.Value))
        {
            if (!recordByArchiveId.TryGetValue(entry.FileId.Value, out var recordIndex))
            {
                (recordByArchiveId.ContainsKey(entry.BaseId.Value) ? stale : missing).Add(entry);
                continue;
            }
            var storedSize = readDecodedSize(recordIndex);
            if (storedSize == entry.Size) present++;
            else wrongSize.Add(new(entry, storedSize));
        }
        return new(manifestIds.BuildId, present, missing, stale, wrongSize);
    }

    public static IReadOnlyList<Gw2CrcManifestRecord> FindCrcMismatches(FileArchive archive, Gw2CrcManifest crcManifest)
    {
        ArgumentNullException.ThrowIfNull(archive);
        return FindCrcMismatches(archive.FileIndex, recordIndex =>
        {
            using var stream = new MemoryStream();
            archive.DecodeEntry(recordIndex, stream);
            return stream.ToArray();
        }, crcManifest);
    }

    public static IReadOnlyList<Gw2CrcManifestRecord> FindCrcMismatches(
        IReadOnlyDictionary<uint, uint> recordByArchiveId,
        Func<uint, byte[]> readDecoded,
        Gw2CrcManifest crcManifest)
    {
        ArgumentNullException.ThrowIfNull(recordByArchiveId);
        ArgumentNullException.ThrowIfNull(readDecoded);
        ArgumentNullException.ThrowIfNull(crcManifest);
        var mismatches = new List<Gw2CrcManifestRecord>();
        foreach (var record in crcManifest.Records)
        {
            if (!recordByArchiveId.TryGetValue(record.FileId, out var recordIndex)) continue;
            if (!ArchiveCrc.Matches(readDecoded(recordIndex), record.Crc)) mismatches.Add(record);
        }
        return mismatches;
    }

    private static bool IsAssetManifestPack(FileArchive archive, uint recordIndex)
    {
        byte[] prefix;
        try { prefix = archive.ReadDecodedPrefix(recordIndex); }
        catch (InvalidDataException) { return false; }
        return prefix.Length >= 12 && prefix[0] == 'P' && prefix[1] == 'F'
            && prefix.AsSpan(8, 4).SequenceEqual(System.Text.Encoding.ASCII.GetBytes(Gw2FourCc.AssetManifest));
    }

    private static Gw2AssetDocument ReadDocument(FileArchive archive, uint recordIndex)
    {
        using var stream = new MemoryStream();
        archive.DecodeEntry(recordIndex, stream);
        return Gw2AssetDocument.Parse(stream.ToArray());
    }
}
