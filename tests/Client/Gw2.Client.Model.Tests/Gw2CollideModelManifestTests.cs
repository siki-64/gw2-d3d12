using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2CollideModelManifestTests
{
    private const uint ManifestFileId = 198320;

    [TestMethod]
    public void ResolvesEveryModelAndCollisionFileWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var manifest = Gw2CollideModelManifest.TryParse(reader.ReadDocument(ManifestFileId))!;

        Assert.AreEqual(9188, manifest.Files.Count);
        Assert.IsTrue(manifest.Files.All(file => file.ModelFileStr.EndsWith(".amdl2", StringComparison.Ordinal)));
        Assert.IsTrue(manifest.Files.All(file => reader.Contains(file.ModelFileId) && reader.Contains(file.CollisionFileId)));
        var first = manifest.Files[0];
        Assert.AreEqual("MODL", reader.ReadDocument(first.ModelFileId).Pack.Type);
        Assert.AreEqual("hvkC", reader.ReadDocument(first.CollisionFileId).Pack.Type);
    }
}
