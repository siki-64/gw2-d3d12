using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using Gw2.Dat.Content;
using Gw2.Dat.Archive;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2AnimSequenceTests
{
    private const ulong HundredBladesSequenceToken = 0x19D38B2304E7;

    [TestMethod]
    public void ReadsTheHundredBladesStepAndTriggersWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var data = reader.ReadAnimSequences()!.DataFor(HundredBladesSequenceToken, 0)!;
        Assert.AreEqual(4567u, data.Steps.Single().DurationMilliseconds);
        Assert.AreEqual(28, data.Triggers.Count);
        Assert.AreEqual(7u, data.Triggers[0].Trigger);
        Assert.AreEqual(320u, data.Triggers[0].Time);
    }
}
