using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2CompositeArchiveTests
{
    private const uint CompositeFileId = 154681;

    [TestMethod]
    public void DecodesRacesSkinsAndAnimOverridesWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var document = reader.ReadDocument(CompositeFileId);
        Assert.IsTrue(document.Chunks.All(chunk => chunk.UnsupportedReason is null));

        var composite = Gw2Composite.TryParse(document)!;
        Assert.AreEqual(29, composite.Races.Count);
        Assert.AreEqual(15, composite.ConfigVersion);
        var human = composite.Races.Single(race => race.Name == "HumanFemale");
        Assert.AreEqual(4311, human.FileData.Count);
        Assert.AreEqual(1315, human.Variants.Count);
        Assert.AreEqual(70, human.AnimOverrides.Count);
        Assert.AreEqual(29193u, human.SkeletonFile.FileId);
        Assert.IsTrue(composite.Races.SelectMany(race => race.FileData)
            .All(file => file.MeshBase.TryGetFileId(out var id) && reader.Contains(id)));
    }
}
