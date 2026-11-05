using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2PortalManifestTests
{
    private const uint RedirectorManifestFileId = 77862;
    private const uint PermutationManifestFileId = 233636;

    [TestMethod]
    public void ReadsRedirectorsAndStartModelSettingsWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);

        var redirectors = Gw2PortalManifest.TryParse(reader.ReadDocument(RedirectorManifestFileId))!
            .Maps.SelectMany(map => map.Redirectors).ToList();
        Assert.AreEqual(2, redirectors.Count);
        Assert.AreEqual(2517942812u, redirectors[0].Token);
        Assert.AreEqual(-13643.25f, redirectors[0].Position.X, 1e-3f);
        Assert.AreEqual(-2909f, redirectors[0].Position.Z, 1e-3f);
        Assert.AreEqual(492.73776f, redirectors[0].Radius, 1e-4f);

        var models = Gw2PortalManifest.TryParse(reader.ReadDocument(PermutationManifestFileId))!
            .Maps.SelectMany(map => map.Starts).SelectMany(start => start.Models).ToList();
        var permuted = models.Where(model => model.Permutation != 0).ToList();
        Assert.AreEqual(8, permuted.Count);
        Assert.AreEqual(80031u, permuted[0].FileId);
        Assert.AreEqual(609892663ul, permuted[0].Permutation);
        Assert.IsTrue(models.Any(model => model.Flags == 1));
    }
}
