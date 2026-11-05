using System.Buffers.Binary;
using Gw2.Dat.Archive;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.DatSurvey.Tests;

[TestClass]
public sealed class Gw2TextKeyCoverageTests
{
    private const uint EnglishFileId = 7;
    private const uint StringsPerFile = 4;

    [TestMethod]
    public void CountsPlainEncryptedAndKeyedStringsPerLanguage()
    {
        var file = Concat(Entry(0, 16, [0x41, 0x00]), Entry(0x20, 8, [0x01]), Entry(0x20, 8, [0x02]), Entry(0, 16, []));
        var manifest = new Gw2TextManifest(StringsPerFile, [new Gw2TextLanguage([0, EnglishFileId]), new Gw2TextLanguage([])]);
        var keys = new Gw2KeyStore("test", new() { [Gw2KeyStore.TextKind] = new() { [StringsPerFile + 1] = 0x1234, [99] = 0x5678 } });

        var coverage = Gw2TextKeyCoverage.Measure(manifest, fileId => fileId == EnglishFileId ? file : null, keys);

        Assert.AreEqual(new Gw2TextKeyCoverageLanguage(Gw2Language.English, 1, 3, 1, 2, 1), coverage.Languages[0]);
        Assert.AreEqual(new Gw2TextKeyCoverageLanguage(Gw2Language.Korean, 0, 0, 0, 0, 0), coverage.Languages[1]);
        Assert.AreEqual(2, coverage.Keys);
        Assert.AreEqual(1, coverage.KeysWithoutEncryptedString);
        Assert.AreEqual((StringsPerFile, 2 * StringsPerFile - 1, 2, 1), coverage.EnglishFiles.Single());
    }

    private static byte[] Entry(ushort baseCharacter, byte bitsPerCharacter, byte[] payload)
    {
        var entry = new byte[6 + payload.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(entry, (ushort)entry.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(entry.AsSpan(2), baseCharacter);
        entry[4] = bitsPerCharacter;
        payload.CopyTo(entry.AsSpan(6));
        return entry;
    }

    private static byte[] Concat(params byte[][] parts) => parts.SelectMany(part => part).ToArray();
}
