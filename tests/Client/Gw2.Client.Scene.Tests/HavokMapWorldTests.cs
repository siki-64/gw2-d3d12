using System;
using System.IO;
using Gw2.Dat.Content;
using Gw2.Formats;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.Intrinsics;
using Gw2.Client.Scene.Havok;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Scene.Tests;

[TestClass]
public sealed class HavokMapWorldTests
{
    private const uint BeachMapFileBaseId = 3719751;

    [TestMethod]
    public void RaysDownOntoTerrainTrianglesHitTheirCentroids()
    {
        using var reader = OpenReader();
        var map = reader.ReadMap(BeachMapFileBaseId)!;
        var world = Gw2HavokMapWorld.Build(map.Collision, map.Terrain, map.Info.Bounds);
        var random = new Random(11);
        var keys = new List<uint>();
        var triangle = new Vector128<float>[3];
        var compared = 0;
        foreach (var body in world.Bodies.Where(body => body.Collection is HavokTerrainCollection).OrderBy(_ => random.Next()).Take(10))
        {
            keys.Clear();
            body.Collection.QueryAabb(Vector128.Create(-1e9f), Vector128.Create(1e9f), keys);
            foreach (var key in keys.OrderBy(_ => random.Next()).Take(30))
            {
                body.Collection.Triangle(key, triangle);
                var centroid = Transform(body.Transform, (triangle[0] + triangle[1] + triangle[2]) / 3f);
                var above = Vector128.Create(centroid.X, centroid.Y, centroid.Z + 0.5f, 0f);
                var below = Vector128.Create(centroid.X, centroid.Y, centroid.Z - 0.5f, 0f);
                var closest = new HavokClosestRayHit();
                Assert.IsTrue(world.CastRay(above, below, closest, out _, Gw2HavokMapWorld.TerrainOnlyMask), $"no hit at {centroid}");
                Assert.AreEqual(0.5f, closest.Fraction, 1e-3f, $"{centroid}");
                Assert.IsTrue(closest.Normal.GetElement(2) > 0f, $"normal {closest.Normal} at {centroid}");
                compared++;
            }
        }
        Assert.AreEqual(300, compared);
    }

    [TestMethod]
    public void GameRayFromAboveFindsGroundBelow()
    {
        using var reader = OpenReader();
        var map = reader.ReadMap(BeachMapFileBaseId)!;
        var world = Gw2HavokMapWorld.Build(map.Collision, map.Terrain, map.Info.Bounds);
        var bounds = map.Info.Bounds;
        var random = new Random(5);
        var hits = 0;
        for (var probe = 0; probe < 200; probe++)
        {
            var x = bounds[0] + random.NextSingle() * (bounds[2] - bounds[0]);
            var y = bounds[1] + random.NextSingle() * (bounds[3] - bounds[1]);
            if (world.CastGameRay(new Vector3(x, y, -40000f), new Vector3(x, y, 40000f)) is not { } hit) continue;
            hits++;
            Assert.IsTrue(hit.Normal.Z < 0f, $"normal {hit.Normal} at {hit.Position}");
        }
        Assert.IsTrue(hits > 150, $"only {hits} of 200 probes found ground");
    }

    [TestMethod]
    public void PlacementBodiesSitAtTheirTranslateInCollisionUnits()
    {
        using var reader = OpenReader();
        var map = reader.ReadMap(BeachMapFileBaseId)!;
        var world = Gw2HavokMapWorld.Build(map.Collision, map.Terrain, map.Info.Bounds);
        var expected = map.Collision!.ObsModels.Concat(map.Collision.PropModels).Concat(map.Collision.ZoneModels)
            .Select(placement => Gw2CharacterStep.CollisionOf(placement.Translate)).ToHashSet();
        var placementBodies = world.Bodies.Where(body => body.Layer != Gw2HavokMapWorld.TerrainLayer).ToList();
        Assert.IsTrue(placementBodies.Count > 0);
        foreach (var body in placementBodies)
            Assert.IsTrue(expected.Contains(body.Transform.Translation), $"body {body.Id} at {body.Transform.Translation}");
    }

    internal static Gw2ContentReader OpenReader()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        return Gw2ContentReader.Open(dat, verifyChecksums: false);
    }

    private static Vector3 Transform(in HavokTransform transform, Vector128<float> point)
    {
        var result = transform.Column0 * point.GetElement(0) + transform.Column1 * point.GetElement(1) + transform.Column2 * point.GetElement(2) + transform.Translation;
        return new Vector3(result.GetElement(0), result.GetElement(1), result.GetElement(2));
    }
}
