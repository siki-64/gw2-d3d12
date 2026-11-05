using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2AnimConfigRaycastTests
{
    private const uint AnimConfigFileId = 184788;

    [TestMethod]
    public void ReadsIkRaycastTargetsWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var config = Gw2AnimConfig.TryParse(reader.ReadDocument(AnimConfigFileId))!;
        var chains = config.ChainGroups.SelectMany(group => group.Chains).ToArray();
        Assert.AreEqual(26, chains.Length);
        Assert.IsTrue(chains.All(chain => chain.TargetMode == 1));
        Assert.IsTrue(chains.All(chain => chain.TargetRaycast is { Flags: 1 } target && target.Direction == System.Numerics.Vector3.UnitZ));
    }
}
