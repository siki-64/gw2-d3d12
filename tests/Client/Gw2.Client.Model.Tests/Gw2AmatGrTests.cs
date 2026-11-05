using Gw2.Formats;
using System;
using System.IO;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2AmatGrTests
{
    private const uint SortedTrianglesFileId = 35410;
    private const uint WideTextureArrayRangeFileId = 77237;
    private const uint ProceduralAnimationFileId = 74852;

    [TestMethod]
    public void ReadsTextureArrayRangeSortTrianglesAndProceduralAnimationWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var sortedTriangles = Gw2AmatGr.TryParse(reader.ReadDocument(SortedTrianglesFileId))!;
        Assert.AreEqual((byte)2, sortedTriangles.SortTriangles);
        Assert.AreEqual((byte)7, sortedTriangles.SortOrder);

        var wideRange = Gw2AmatGr.TryParse(reader.ReadDocument(WideTextureArrayRangeFileId))!;
        Assert.AreEqual((byte)5, wideRange.TextureArrayRange);
        Assert.AreEqual(10, wideRange.TextureCount);

        Assert.AreEqual((byte)1, Gw2AmatGr.TryParse(reader.ReadDocument(ProceduralAnimationFileId))!.ProceduralAnimation);
    }
}
