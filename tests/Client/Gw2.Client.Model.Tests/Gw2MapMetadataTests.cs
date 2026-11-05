using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2MapMetadataTests
{
    [TestMethod]
    public void ReadsMapIdsAndTypesWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var metadata = Gw2MapMetadata.TryParse(reader.ReadDocument(Gw2MapMetadata.FileId))!;

        var typeByMapId = metadata.Maps.ToDictionary(map => map.MapId, map => map.MapType);
        Assert.AreEqual(1244, metadata.Maps.Count);
        Assert.AreEqual((byte)9, typeByMapId[38]);
        Assert.AreEqual((byte)10, typeByMapId[96]);
        Assert.AreEqual((byte)11, typeByMapId[95]);
        Assert.AreEqual((byte)12, typeByMapId[94]);
        Assert.AreEqual((byte)14, typeByMapId[899]);
        Assert.AreEqual((byte)15, typeByMapId[968]);
        Assert.AreEqual((byte)5, typeByMapId[15]);
    }
}
