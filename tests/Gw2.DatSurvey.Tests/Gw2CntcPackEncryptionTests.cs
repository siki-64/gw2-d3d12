using System.Buffers.Binary;
using Gw2.Dat.Archive;
using Gw2.Formats;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.DatSurvey.Tests;

[TestClass]
public sealed class Gw2CntcPackEncryptionTests
{
    private static readonly byte[] SyntheticContentKey = [.. Enumerable.Range(1, 32).Select(value => (byte)value)];
    private static readonly byte[] PlainContent = [0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88];

    [TestMethod]
    public void ParsesPlainPackUnchanged()
    {
        var pack = Gw2CntcPack.Parse(CntcPackBuilder.Build(0, PlainContent));
        CollectionAssert.AreEqual(PlainContent, pack.ReadContent(0, PlainContent.Length).ToArray());
    }

    [TestMethod]
    public void DecryptsContentOfEncryptedPackWithTheSuppliedKey()
    {
        var encrypted = PlainContent.ToArray();
        CptRc4.KeySchedule(CptRc4.DeriveKey(SyntheticContentKey)).Process(encrypted);
        var pack = Gw2CntcPack.Parse(CntcPackBuilder.Build(Gw2CntcPack.EncryptedFlag, encrypted), SyntheticContentKey);

        CollectionAssert.AreEqual(PlainContent, pack.ReadContent(0, PlainContent.Length).ToArray());
        Assert.AreEqual(0u, BinaryPrimitives.ReadUInt32LittleEndian(pack.Data.Span[CntcPackBuilder.FlagsOffset..]) & Gw2CntcPack.EncryptedFlag);
        Assert.AreEqual(0x44332211u, pack.ReadContentUInt32(pack.Objects[0], 0));
    }

    [TestMethod]
    public void RejectsEncryptedPackWithoutAKey()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => Gw2CntcPack.Parse(CntcPackBuilder.Build(Gw2CntcPack.EncryptedFlag, PlainContent)));
    }
}
