using Gw2.Formats;
using System.Buffers.Binary;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Formats.Tests;

[TestClass]
public sealed class CmpApiTests
{
    private const uint TextureWithEmptyDistanceTableFileId = 0xDA59;
    private const int EntryHeaderBytes = 8;

    [TestMethod]
    public void StreamAsLongAsItsOutputIsCopiedUnchanged()
    {
        var stored = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var output = new byte[stored.Length];

        Assert.IsTrue(CmpApi.Decompress(output, stored));
        CollectionAssert.AreEqual(stored, output);
    }

    [TestMethod]
    public void StreamLongerThanItsOutputIsRejected() =>
        Assert.IsFalse(CmpApi.Decompress(new byte[4], new byte[8]));

    [TestMethod]
    public void UnknownMethodIsCorrupt()
    {
        var stream = new byte[] { 0, 0, 0, 0x20, 0, 0, 0, 0 };
        Assert.IsFalse(CmpApi.Decompress(new byte[16], stream));
    }

    private static FileArchive OpenOrSkip()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        return FileArchive.Open(dat, verifyChecksums: false);
    }

    private static byte[] Decompress(FileArchive archive, uint fileId)
    {
        var stored = ArchiveEntry.StripCrc32(archive.ReadRawEntry(archive.FileIndex[fileId]));
        var output = new byte[BinaryPrimitives.ReadUInt32LittleEndian(stored.AsSpan(4))];
        Assert.IsTrue(CmpApi.Decompress(output, stored.AsSpan(EntryHeaderBytes)));
        return output;
    }

    [TestMethod]
    public void EmptyDistanceTableDecodesAsItsLastSymbolWhenConfigured()
    {
        using var archive = OpenOrSkip();
        var bytes = Decompress(archive, TextureWithEmptyDistanceTableFileId);

        Assert.AreEqual(50368, bytes.Length);
        CollectionAssert.AreEqual(Convert.FromHexString("7DEF6108D5450A55"), bytes.AsSpan(bytes.Length - 8).ToArray());
    }

    [TestMethod]
    public void PrefixMatchesTheFullDecodeWhenConfigured()
    {
        using var archive = OpenOrSkip();
        var bytes = Decompress(archive, TextureWithEmptyDistanceTableFileId);
        var stored = ArchiveEntry.StripCrc32(archive.ReadRawEntry(archive.FileIndex[TextureWithEmptyDistanceTableFileId]));
        var prefix = new byte[64];

        Assert.IsTrue(CmpApi.DecompressPrefix(prefix, stored.AsSpan(EntryHeaderBytes)));
        CollectionAssert.AreEqual(bytes[..64], prefix);
    }
}
