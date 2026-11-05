using Gw2.Formats;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Gw2.Dat.Archive;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.DatSurvey.Tests;

[TestClass]
public sealed class Gw2DatTypeIndexTests
{
    private const uint LeafMaterialFileId = 0xE106;
    private const uint TerrainMaterialFileId = 0x2D29C;
    private const int FrozenBuildMaterialCount = 2854;

    [TestMethod]
    public void IndexListsEveryMaterialAndIsReusedOnTheNextLoad()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        using var archive = FileArchive.Open(dat, verifyChecksums: false);

        var first = Gw2DatTypeIndex.Load(archive, dat);
        var watch = Stopwatch.StartNew();
        var second = Gw2DatTypeIndex.Load(archive, dat);
        watch.Stop();
        var materialRecords = second.FileIdsOfType("AMAT").Select(fileId => archive.FileIndex[fileId]).Distinct().Count();

        Assert.AreEqual(first.RecordCount, second.RecordCount);
        Assert.AreEqual(0, second.RefreshedRecords);
        Assert.AreEqual(FrozenBuildMaterialCount, materialRecords);
        CollectionAssert.IsSubsetOf(new[] { LeafMaterialFileId, TerrainMaterialFileId }, second.FileIdsOfType("AMAT").ToList());
        Assert.AreEqual(Gw2DatTypeIndex.TagOf("AMAT"), second.TagOfFile(LeafMaterialFileId));
        Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(10), $"cached load took {watch.Elapsed}");
    }

    [TestMethod]
    public void TagOfPrefixReadsThePackTypeOrTheLeadingMagic()
    {
        Assert.AreEqual(Gw2DatTypeIndex.TagOf("AMAT"), Gw2DatTypeIndex.TagOfPrefix("PF\u0001\u0000\u000c\u0000\u0000\u0000AMAT"u8));
        Assert.AreEqual(Gw2DatTypeIndex.TagOf("ATEX"), Gw2DatTypeIndex.TagOfPrefix("ATEX\u0000\u0000\u0000\u0000\u0000\u0000\u0000\u0000"u8));
    }
}
