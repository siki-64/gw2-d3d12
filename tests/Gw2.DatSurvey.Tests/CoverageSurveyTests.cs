using Gw2.Formats;
using System.Buffers.Binary;
using System.Text;
using Gw2.Dat.Content;
using Gw2.Dat.Archive;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.DatSurvey.Tests;

[TestClass]
public sealed class CoverageSurveyTests
{
    private const string CatalogJson = """
        {"format":"gw2-native-pack-schemas-v1","executableSha256":"00",
         "registrations":[{"chunk":"test","address":"1","versions":{"0":"A"}}],
         "types":{"A":{"name":"PackTestV0","fields":[{"kind":6,"name":"value","count":0},{"kind":6,"name":"other","count":0}]}}}
        """;

    [TestMethod]
    public void ClassifiesContainersByMagic()
    {
        Assert.AreEqual("pack", Gw2DatFileKind.Classify("PF\x01\x00"u8));
        Assert.AreEqual("texture", Gw2DatFileKind.Classify("ATTXDXT1"u8));
        Assert.AreEqual("dds", Gw2DatFileKind.Classify("DDS |"u8));
        Assert.AreEqual("audio", Gw2DatFileKind.Classify("OggS"u8));
        Assert.AreEqual("audio", Gw2DatFileKind.Classify("RIFF"u8));
        Assert.AreEqual("strings", Gw2DatFileKind.Classify("strs"u8));
        Assert.AreEqual("text", Gw2DatFileKind.Classify("abcd"u8));
        Assert.AreEqual("empty", Gw2DatFileKind.Classify([]));
    }

    [TestMethod]
    public void ClassifiesEmbeddedMediaAndWebFiles()
    {
        Assert.AreEqual("texture", Gw2DatFileKind.Classify("CTEXDXT5"u8));
        Assert.AreEqual("audio", Gw2DatFileKind.Classify("asnd"u8));
        Assert.AreEqual("audio", Gw2DatFileKind.Classify([(byte)'I', (byte)'D', (byte)'3', 0x04]));
        Assert.AreEqual("audio", Gw2DatFileKind.Classify([0xFF, 0xFB, 0x90, 0x00]));
        Assert.AreEqual("image", Gw2DatFileKind.Classify([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A]));
        Assert.AreEqual("image", Gw2DatFileKind.Classify([0xFF, 0xD8, 0xFF, 0xE1]));
        Assert.AreEqual("font", Gw2DatFileKind.Classify([0x00, 0x01, 0x00, 0x00, 0x00, 0x10]));
        Assert.AreEqual("font", Gw2DatFileKind.Classify("wOFF"u8));
        Assert.AreEqual("video", Gw2DatFileKind.Classify("KB2j"u8));
        Assert.AreEqual("flash", Gw2DatFileKind.Classify([(byte)'C', (byte)'W', (byte)'S', 0x0A]));
        Assert.AreEqual("executable", Gw2DatFileKind.Classify([(byte)'M', (byte)'Z', 0x90, 0x00]));
        Assert.AreEqual("icu-data", Gw2DatFileKind.Classify([0x20, 0x00, 0xDA, 0x27]));
        Assert.AreEqual("text", Gw2DatFileKind.Classify([.. "<!DOCTYPE html>"u8, 0x0D, 0x0A]));
        Assert.AreEqual("text", Gw2DatFileKind.Classify([0xEF, 0xBB, 0xBF, (byte)'.', (byte)'a', (byte)'{']));
        Assert.AreEqual("unknown", Gw2DatFileKind.Classify([0x10, 0x02, 0x00, 0x01, 0x07]));
    }

    [TestMethod]
    public void ClassifiesChromiumRuntimeBlobs()
    {
        var pak = new byte[16];
        pak[0] = 5;
        pak[4] = 1;
        Assert.AreEqual("chromium-pak", Gw2DatFileKind.Classify(pak));
        var snapshot = new byte[32];
        snapshot[0] = 1;
        "10.3.174.18"u8.CopyTo(snapshot.AsSpan(12));
        Assert.AreEqual("v8-snapshot", Gw2DatFileKind.Classify(snapshot));
        var eot = new byte[40];
        eot[0] = 0x76;
        "LP"u8.CopyTo(eot.AsSpan(34));
        Assert.AreEqual("font", Gw2DatFileKind.Classify(eot));
    }

    [TestMethod]
    public void RelabelOnlyChangesUnknownEntriesInTheSet()
    {
        SurveyEntry Entry(uint record, string kind) => new(record, [], 8, 0, 0, CoverageSurvey.DecodeOk, 8, kind, null, null, null);
        var relabeled = CoverageSurvey.Relabel([Entry(20, "unknown"), Entry(21, "unknown"), Entry(22, "text")],
            new HashSet<uint> { 20, 22 }, Gw2DatFileKind.GlyphStream);
        CollectionAssert.AreEqual(new[] { Gw2DatFileKind.GlyphStream, "unknown", "text" }, relabeled.Select(entry => entry.Kind).ToArray());
    }

    [TestMethod]
    public void MagicReplacesUnprintableBytes()
    {
        Assert.AreEqual("a.b.", CoverageSurvey.Magic([(byte)'a', 0, (byte)'b', 0xFF]));
        Assert.IsNull(CoverageSurvey.Magic([1, 2]));
    }

    [TestMethod]
    public void RecordsRootTypeAndParseResultPerChunk()
    {
        var pack = Pack(("test", 0, new byte[8]), ("test", 0, new byte[2]), ("none", 3, new byte[8]));

        var entry = CoverageSurvey.SurveyEntryBytes(20, [7], new MftEntry(1, 1, 8, 0, 0, 0), pack, Catalog());

        Assert.AreEqual("pack", entry.Kind);
        Assert.AreEqual("abcd", entry.PackType);
        Assert.AreEqual(3, entry.Chunks!.Count);
        Assert.AreEqual("PackTestV0", entry.Chunks[0].Root);
        Assert.AreEqual(CoverageSurvey.ParseOk, entry.Chunks[0].Parse);
        Assert.AreNotEqual(CoverageSurvey.ParseOk, entry.Chunks[1].Parse);
        Assert.IsNull(entry.Chunks[2].Root);
        Assert.AreEqual(CoverageSurvey.NoSchema, entry.Chunks[2].Parse);
    }

    [TestMethod]
    public void ReservedRecordsAreArchiveEntries()
    {
        var entry = CoverageSurvey.SurveyEntryBytes(3, [], new MftEntry(1, 1, 0, 0, 0, 0), Pack(), Catalog());
        Assert.AreEqual("archive", entry.Kind);
        Assert.IsNull(entry.Chunks);
    }

    [TestMethod]
    public void InvalidPackIsReportedNotThrown()
    {
        var entry = CoverageSurvey.SurveyEntryBytes(20, [], new MftEntry(1, 1, 8, 0, 0, 0), "PF\0\0"u8.ToArray(), Catalog());
        Assert.AreEqual("pack-invalid", entry.Kind);
    }

    [TestMethod]
    public void RootTypesAggregateChunksAcrossEntries()
    {
        var catalog = Catalog();
        var mft = new MftEntry(1, 1, 8, 0, 0, 0);
        var entries = new[]
        {
            CoverageSurvey.SurveyEntryBytes(20, [], mft, Pack(("test", 0, new byte[8])), catalog),
            CoverageSurvey.SurveyEntryBytes(21, [], mft, Pack(("test", 0, new byte[2]), ("none", 3, new byte[8])), catalog),
        };

        var roots = CoverageReport.RootTypes(entries);

        var test = roots.Single(root => root.Chunk == "test");
        Assert.AreEqual(2, test.Chunks);
        Assert.AreEqual(1, test.Parsed);
        Assert.IsNotNull(test.FirstFailure);
        var none = roots.Single(root => root.Chunk == "none");
        Assert.AreEqual(CoverageSurvey.NoSchema, none.Root);
        Assert.IsNull(none.FirstFailure);
        StringAssert.Contains(CoverageReport.Summary(entries), "Chunks parsed: 1 of 3");
    }

    [TestMethod]
    public void DecodersRecordFieldReadsAndResults()
    {
        var catalog = Catalog();
        var reads = new List<SurveyFieldRead>();
        Gw2PackDecoder[] decoders =
        [
            new("Reader", "PackTest", null, document => document.Chunks[0].Root!.Fields.GetValueOrDefault("value")),
            new("Rejecter", "PackTest", null, _ => null),
            new("Thrower", null, "test", _ => throw new InvalidDataException("bad")),
            new("Unrouted", "PackOther", "othr", _ => throw new InvalidOperationException("must not run")),
        ];

        var entry = CoverageSurvey.SurveyEntryBytes(20, [], new MftEntry(1, 1, 8, 0, 0, 0),
            Pack(("test", 0, new byte[8])), catalog, decoders,
            (decoder, accessed) => reads.AddRange(accessed.Select(read => new SurveyFieldRead(decoder, read.TypeName, read.Field))));

        CollectionAssert.AreEqual(new[] { new SurveyDecode("Reader", SurveyDecoders.Decoded), new SurveyDecode("Thrower", "InvalidDataException: bad") },
            entry.Decoders!.ToArray());
        CollectionAssert.AreEqual(new[] { new SurveyFieldRead("Reader", "PackTestV0", "value") }, reads);
        var coverage = CoverageReport.FieldCoverage("PackTestV0", catalog, reads);
        CollectionAssert.AreEqual(new[] { "Reader" }, coverage.Decoders.ToArray());
        Assert.AreEqual(1, coverage.FieldsRead);
        Assert.AreEqual(2, coverage.Fields);
        CollectionAssert.AreEqual(new[] { "PackTestV0.other" }, coverage.UnreadFields.ToArray());
    }

    private const string VersionedCatalogJson = """
        {"format":"gw2-native-pack-schemas-v1","executableSha256":"00",
         "registrations":[{"chunk":"test","address":"1","versions":{"0":"A","1":"B"}}],
         "types":{"A":{"name":"PackTestV0","fields":[{"kind":6,"name":"value","count":0},{"kind":6,"name":"other","count":0}]},
                  "B":{"name":"PackTestV1","fields":[{"kind":6,"name":"value","count":0},{"kind":6,"name":"other","count":0},{"kind":6,"name":"extra","count":0}]}}}
        """;

    [TestMethod]
    public void FamilyCoverageCountsPresentVersionsAndSeparatesNeverPopulatedFields()
    {
        var catalog = Gw2PackSchemaCatalog.Load(new MemoryStream(Encoding.UTF8.GetBytes(VersionedCatalogJson)));
        var census = new SurveyFieldCensus();
        var reads = new List<SurveyFieldRead>();
        Gw2PackDecoder[] decoders = [new("Reader", "PackTest", null, document => document.Chunks[0].Root!.Fields.GetValueOrDefault("value"))];
        var payload = new byte[8];
        payload[0] = 5;

        var entry = CoverageSurvey.SurveyEntryBytes(20, [], new MftEntry(1, 1, 8, 0, 0, 0),
            Pack(("test", 0, payload)), catalog, decoders,
            (decoder, accessed) => reads.AddRange(accessed.Select(read => new SurveyFieldRead(decoder, read.TypeName, read.Field))),
            census);

        var family = CoverageReport.FamilyCoverage([entry], catalog, reads, census.Values).Single();
        Assert.AreEqual("PackTest", family.Family);
        Assert.AreEqual(2, family.Fields);
        Assert.AreEqual(1, family.FieldsRead);
        CollectionAssert.AreEqual(new[] { "PackTest.other" }, family.UnreadEmpty.ToArray());
        Assert.AreEqual(0, family.UnreadPopulated.Count);
    }

    [TestMethod]
    public void AbsentReadsNameTheVersionsThatHaveTheField()
    {
        var catalog = Gw2PackSchemaCatalog.Load(new MemoryStream(Encoding.UTF8.GetBytes(VersionedCatalogJson)));
        SurveyFieldRead[] reads =
        [
            new("Reader", "PackTestV0", "value"),
            new("Reader", "PackTestV0", "extra"),
            new("Reader", "PackTestV1", "missing"),
        ];

        var absent = CoverageReport.AbsentReads(catalog, reads);

        Assert.AreEqual(2, absent.Count);
        Assert.AreEqual("extra", absent[0].Field);
        CollectionAssert.AreEqual(new[] { "PackTestV1" }, absent[0].TypesWithField.ToArray());
        Assert.AreEqual("missing", absent[1].Field);
        Assert.AreEqual(0, absent[1].TypesWithField.Count);
    }

    [TestMethod]
    public void FieldReadsOutsideRecordingAreIgnored()
    {
        var pack = Gw2PackFile.Parse(Pack(("test", 0, new byte[8])));
        var root = new Gw2PackReader(Catalog()).Read(pack, pack.Chunks[0]);
        _ = root.Fields["value"];
        var access = PackFieldAccess.Record(() => root.Fields.TryGetValue("other", out _));
        CollectionAssert.AreEqual(new[] { ("PackTestV0", "other") }, access.Reads.ToArray());
    }

    private static Gw2PackSchemaCatalog Catalog() => Gw2PackSchemaCatalog.Load(new MemoryStream(Encoding.UTF8.GetBytes(CatalogJson)));

    private static byte[] Pack(params (string Type, ushort Version, byte[] Payload)[] chunks)
    {
        var output = new MemoryStream();
        var header = new byte[12];
        "PF"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(2), 4);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6), 12);
        "abcd"u8.CopyTo(header.AsSpan(8));
        output.Write(header);
        foreach (var (type, version, payload) in chunks)
        {
            var chunk = new byte[16 + payload.Length];
            Encoding.ASCII.GetBytes(type).CopyTo(chunk, 0);
            BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4), (uint)(chunk.Length - 8));
            BinaryPrimitives.WriteUInt16LittleEndian(chunk.AsSpan(8), version);
            BinaryPrimitives.WriteUInt16LittleEndian(chunk.AsSpan(10), 16);
            payload.CopyTo(chunk, 16);
            output.Write(chunk);
        }
        return output.ToArray();
    }
}
