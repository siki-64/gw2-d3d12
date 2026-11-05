using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2CollisionPropSequenceTests
{
    [TestMethod]
    public void PropPlacementsCarrySequenceWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var collision = Gw2MapCollision.TryParse(reader.ReadDocument(1839188))!;

        var animationSequences = collision.Animations.Select(animation => animation.Sequence).ToHashSet();
        Assert.AreEqual(32558, collision.PropModels.Count);
        Assert.AreEqual(15, collision.PropModels.Select(placement => placement.Sequence).Distinct().Count());
        Assert.IsTrue(collision.PropModels.All(placement => animationSequences.Contains(placement.Sequence!.Value)));
        Assert.IsTrue(collision.ZoneModels.All(placement => placement.Sequence is null));
    }
}
