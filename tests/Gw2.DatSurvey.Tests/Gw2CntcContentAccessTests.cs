using Gw2.Dat.Archive;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.DatSurvey.Tests;

[TestClass]
public sealed class Gw2CntcContentAccessTests
{
    [TestMethod]
    public void RowReadsRecordOnlyTheBytesTheyRead()
    {
        var pack = Gw2CntcPack.Parse(CntcPackBuilder.Build(0, new byte[16]));
        var obj = pack.Objects[0];
        var access = Gw2CntcContentAccess.Record(() =>
        {
            var row = pack.ReadRow(obj.Offset, 16);
            row.U32(4);
            row.U8(12);
        });
        CollectionAssert.AreEquivalent(new uint[] { 4, 5, 6, 7, 12 }, access.BytesByContentType[obj.Type].ToArray());
    }

    [TestMethod]
    public void ReadsOutsideARecordingAreNotKept()
    {
        var pack = Gw2CntcPack.Parse(CntcPackBuilder.Build(0, new byte[16]));
        pack.ReadRow(0, 16).U32(0);
        var access = Gw2CntcContentAccess.Record(() => { });
        Assert.AreEqual(0, access.BytesByContentType.Count);
    }
}
