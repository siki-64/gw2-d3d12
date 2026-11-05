using Gw2.Formats;
using System.Buffers.Binary;
using Gw2.Dat.Content;
using Gw2.Dat.Archive;
using Gw2.Dat.Identity;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.DatSurvey.Tests;

[TestClass]
public sealed class ArchiveSyncTests
{
    private const string ReferenceLocalDat = "gw2-research/research/208784/Local-208784.dat";
    private const string ReferenceDat = "gw2-research/research/208784/Gw2-208784.dat";
    private const uint ReferenceBuild = 208784;
    private const int CrcSampleCount = 16;

    private static FileArchive OpenArchive(string reference)
    {
        var path = FindRepoFile(reference);
        if (path is null) Assert.Inconclusive($"Place {reference}.");
        return FileArchive.Open(path, verifyChecksums: false);
    }

    private static byte[] Decode(FileArchive archive, uint fileId)
    {
        using var stream = new MemoryStream();
        archive.DecodeFileId(fileId, stream);
        return stream.ToArray();
    }

    private static Gw2AssetRootManifest ReadRootManifest(FileArchive local) =>
        Gw2ArchiveSync.FindRootManifest(local) ?? throw new AssertFailedException("No root asset manifest");

    private static Gw2AssetManifest ReadManifest()
    {
        using var local = OpenArchive(ReferenceLocalDat);
        var root = ReadRootManifest(local);
        return Gw2ArchiveSync.ReadManifests(local, root).First(manifest => manifest.Records.Count > 0);
    }

    [TestMethod]
    public void RootManifestListsEverySubManifest()
    {
        using var local = OpenArchive(ReferenceLocalDat);
        var root = ReadRootManifest(local);

        Assert.AreEqual(ReferenceBuild, root.BuildId);
        Assert.IsTrue(root.Manifests.Count > 0);
        Assert.IsTrue(root.Manifests.All(file => local.FileIndex.ContainsKey(file.FileId)));
        Assert.IsTrue(root.Manifests.All(file => !string.IsNullOrEmpty(file.Name)));
        Assert.IsTrue(root.ExtraFiles.Count > 0);
    }

    [TestMethod]
    public void CrcManifestRowsMatchTheirFiles()
    {
        using var local = OpenArchive(ReferenceLocalDat);
        using var archive = OpenArchive(ReferenceDat);
        var extra = ReadRootManifest(local).ExtraFiles.Single();
        if (!archive.FileIndex.ContainsKey(extra.FileId)) Assert.Inconclusive($"CRC manifest {extra.FileId} is not in the archive.");
        var crc = Gw2CrcManifest.Parse(Decode(archive, extra.FileId));

        Assert.IsTrue(crc.Records.Count > 0);
        var present = crc.Records.Where(record => archive.FileIndex.ContainsKey(record.FileId)).Take(CrcSampleCount).ToList();
        Assert.AreEqual(CrcSampleCount, present.Count);
        foreach (var record in present)
        {
            var decoded = Decode(archive, record.FileId);
            Assert.AreEqual(record.Size, (uint)decoded.Length, record.FileId.ToString());
            Assert.AreEqual(record.Crc, ArchiveCrc.ComputeCrc32C(decoded), record.FileId.ToString());
        }

        var intact = Decode(archive, present[0].FileId);
        var corrupted = (byte[])intact.Clone();
        corrupted[0] ^= 1;
        var decodedByRecord = new Dictionary<uint, byte[]> { [1] = intact, [2] = corrupted };
        var recordByArchiveId = new Dictionary<uint, uint> { [present[0].FileId] = 1, [present[1].FileId] = 2 };
        var mismatches = Gw2ArchiveSync.FindCrcMismatches(recordByArchiveId, recordIndex => decodedByRecord[recordIndex], crc);
        Assert.AreEqual(present[1].FileId, mismatches.Single().FileId);
    }

    [TestMethod]
    public void CrcCheckFlagsOnlyTheCorruptedFile()
    {
        var intact = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var corrupted = (byte[])intact.Clone();
        corrupted[0] ^= 1;
        var crc = Gw2CrcManifest.Parse(CrcManifestPack([new(8192, 8192, 8, ArchiveCrc.ComputeCrc32C(intact)), new(8193, 8193, 8, ArchiveCrc.ComputeCrc32C(intact))]));
        var decodedByRecord = new Dictionary<uint, byte[]> { [1] = intact, [2] = corrupted };
        var recordByArchiveId = new Dictionary<uint, uint> { [8192] = 1, [8193] = 2 };

        var mismatches = Gw2ArchiveSync.FindCrcMismatches(recordByArchiveId, recordIndex => decodedByRecord[recordIndex], crc);

        Assert.AreEqual(2, crc.Records.Count);
        Assert.AreEqual(8193u, mismatches.Single().FileId);
    }

    private static byte[] CrcManifestPack(Gw2CrcManifestRecord[] records)
    {
        const int FileHeaderSize = 12;
        const int ChunkHeaderSize = 16;
        const int RecordsOffset = 12;
        var chunkSize = ChunkHeaderSize + RecordsOffset + records.Length * 16;
        var pack = new byte[FileHeaderSize + chunkSize];
        var span = pack.AsSpan();
        "PF"u8.CopyTo(span);
        BinaryPrimitives.WriteUInt16LittleEndian(span[2..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(span[6..], FileHeaderSize);
        "CRCM"u8.CopyTo(span[8..]);
        var chunk = span[FileHeaderSize..];
        "CRCM"u8.CopyTo(chunk);
        BinaryPrimitives.WriteUInt32LittleEndian(chunk[4..], (uint)(chunkSize - 8));
        BinaryPrimitives.WriteUInt16LittleEndian(chunk[10..], ChunkHeaderSize);
        var payload = chunk[ChunkHeaderSize..];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, (uint)records.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(payload[4..], RecordsOffset - 4);
        for (var index = 0; index < records.Length; index++)
        {
            var row = payload[(RecordsOffset + index * 16)..];
            BinaryPrimitives.WriteUInt32LittleEndian(row, records[index].BaseId);
            BinaryPrimitives.WriteUInt32LittleEndian(row[4..], records[index].FileId);
            BinaryPrimitives.WriteUInt32LittleEndian(row[8..], records[index].Size);
            BinaryPrimitives.WriteUInt32LittleEndian(row[12..], records[index].Crc);
        }
        return pack;
    }

    [TestMethod]
    public void ManifestDecodesRecordsStreamsAndProperties()
    {
        var manifest = ReadManifest();

        Assert.AreEqual(ReferenceBuild, manifest.BuildId);
        Assert.IsTrue(manifest.Records.Count > 0);
        Assert.IsTrue(manifest.TotalRecordSize > 0);
        Assert.IsTrue(manifest.PropertyTable.All(entry => entry.PropertyIndex < manifest.Properties.Count));
    }

    [TestMethod]
    public void ManifestResolvesBaseIdToFileId()
    {
        var manifest = ReadManifest();
        var ids = Gw2ArchiveSync.ToManifestIds(manifest.BuildId, [manifest]);

        Assert.AreEqual(manifest.Records.Select(record => record.BaseId).Distinct().Count(), ids.Count);
        foreach (var record in manifest.Records)
        {
            Assert.IsTrue(ids.TryGetFileId(new Gw2BaseId(record.BaseId), out var fileId));
            Assert.AreEqual(record.FileId, fileId.Value);
        }
        Assert.IsFalse(ids.TryGetFileId(new Gw2BaseId(1), out _));
    }

    [TestMethod]
    public void CheckReportsMissingStaleAndWrongSizeFiles()
    {
        var ids = new Gw2ManifestIds(1, [
            new(new Gw2BaseId(10), new Gw2FileId(10), 100),
            new(new Gw2BaseId(20), new Gw2FileId(21), 200),
            new(new Gw2BaseId(30), new Gw2FileId(31), 300),
            new(new Gw2BaseId(40), new Gw2FileId(41), 400),
        ]);
        var recordByArchiveId = new Dictionary<uint, uint> { [10] = 0, [20] = 1, [40] = 2, [41] = 2 };
        var decodedSizeByRecord = new ulong[] { 100, 150, 399 };

        var report = Gw2ArchiveSync.Check(recordByArchiveId, record => decodedSizeByRecord[record], ids);

        Assert.AreEqual(1, report.Present);
        Assert.AreEqual(30u, report.Missing.Single().BaseId.Value);
        Assert.AreEqual(20u, report.Stale.Single().BaseId.Value);
        Assert.AreEqual(new Gw2ArchiveSizeMismatch(ids.Entries.Single(entry => entry.BaseId.Value == 40), 399), report.WrongSize.Single());
        Assert.IsFalse(report.IsValid);
    }

    private static string? FindRepoFile(string relative)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, relative);
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }
}
