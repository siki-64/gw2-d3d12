using System.Linq;
using Gw2.Client.Scene.Havok;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Scene.Tests;

[TestClass]
public sealed class PlacementBlockerTests
{
    private const uint ForestMapFileBaseId = 3797412;

    [TestMethod]
    public void BlockersAreScaledByThePlacementScale()
    {
        using var reader = HavokMapWorldTests.OpenReader();
        var collision = reader.ReadMap(ForestMapFileBaseId)!.Collision!;
        var withBlockers = collision.PropModels
            .Where(placement => collision.ScaledBlockers(placement.GeometryIndex, Gw2MapCollision.PropPlacementSequence(placement), 1f).Count > 0)
            .ToList();
        Assert.AreEqual(273, withBlockers.Count);
        Assert.AreEqual(370, withBlockers.Sum(placement => collision.ScaledBlockers(placement.GeometryIndex, Gw2MapCollision.PropPlacementSequence(placement), 1f).Count));
        foreach (var placement in withBlockers.Take(20))
        {
            var sequence = Gw2MapCollision.PropPlacementSequence(placement);
            collision.TryResolveAnimation(placement.GeometryIndex, sequence, out var animation);
            var scaled = collision.ScaledBlockers(placement.GeometryIndex, sequence, placement.Scale);
            for (var blocker = 0; blocker < scaled.Count; blocker++)
            {
                var raw = collision.Blockers[(int)animation.BlockerIndices[blocker]].Vertices;
                for (var point = 0; point < raw.Count; point++)
                    Assert.AreEqual(raw[point] * placement.Scale, scaled[blocker][point]);
            }
        }
    }

    [TestMethod]
    public void MapPlacementsBuildNoBlockersWithTheirOwnKey()
    {
        foreach (var layer in new[] { Gw2HavokMapWorld.PropModelLayer, Gw2HavokMapWorld.TerrainLayer, Gw2HavokMapWorld.ZoneModelLayer, Gw2HavokMapWorld.ObsModelLayer })
        {
            Assert.IsFalse(Gw2HavokMapWorld.BuildsBlockers(layer, usesOwnKey: true));
            Assert.IsTrue(Gw2HavokMapWorld.BuildsBlockers(layer, usesOwnKey: false));
        }
        Assert.IsTrue(Gw2HavokMapWorld.BuildsBlockers(4, usesOwnKey: true));
        Assert.IsTrue(Gw2HavokMapWorld.BuildsBlockers(5, usesOwnKey: true));
        Assert.IsFalse(Gw2HavokMapWorld.BuildsBlockers(15, usesOwnKey: false));
    }
}
