using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2CollisionAnimationKeyTests
{
    private const uint BeachMapFileBaseId = 3719751;

    [TestMethod]
    public void PlacementsResolveTheAnimationOfTheirKey()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var collision = reader.ReadMap(BeachMapFileBaseId)!.Collision!;

        var resolvedProps = collision.PropModels
            .Count(placement => collision.TryResolveAnimation(placement.GeometryIndex, Gw2MapCollision.PropPlacementSequence(placement), out _));
        var resolvedObstacles = collision.ObsModels
            .Count(placement => collision.TryResolveAnimation(placement.GeometryIndex, Gw2MapCollision.DefaultPlacementSequence, out _));

        Assert.AreEqual(28502, collision.PropModels.Count);
        Assert.AreEqual(28496, resolvedProps);
        Assert.AreEqual(20, resolvedObstacles);
        Assert.IsTrue(collision.PropModels.All(placement =>
            !collision.TryResolveAnimation(placement.GeometryIndex, Gw2MapCollision.PropPlacementSequence(placement), out var animation)
            || animation.Sequence == Gw2MapCollision.PropPlacementSequence(placement)));
    }
}
