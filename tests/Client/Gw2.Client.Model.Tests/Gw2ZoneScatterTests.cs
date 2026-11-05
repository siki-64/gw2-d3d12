using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2ZoneScatterTests
{
    private const uint WayfarerFoothillsMapFileId = 187611;
    private const uint GlobalWaterMapFileId = 1839188;

    [TestMethod]
    public void RandomFollowsTheExeRecurrence()
    {
        var random = new Gw2ZoneRandom(1);
        Assert.AreEqual(48271u, random.Next());
        Assert.AreEqual(unchecked(48271u * 48271u - 0x7FFFFFFFu * (48271u / 44488u)), random.Next());
        Assert.AreEqual(new Gw2ZoneRandom(Gw2ZoneRandom.Fallback).Next(), new Gw2ZoneRandom(0).Next());
        var bounded = new Gw2ZoneRandom(19);
        for (var draw = 0; draw < 64; draw++) Assert.IsTrue(bounded.NextInt(1, 3) is >= 1 and <= 3);
    }

    [TestMethod]
    public void QualityModeTrimsTheTilingFill()
    {
        Assert.AreEqual(4, Gw2ZoneScatter.TilingFillCount(4, 2));
        Assert.AreEqual(3, Gw2ZoneScatter.TilingFillCount(4, 1));
        Assert.AreEqual(2, Gw2ZoneScatter.TilingFillCount(4, 0));
        Assert.AreEqual(2, Gw2ZoneScatter.TilingFillCount(3, 0));
        Assert.AreEqual(1, Gw2ZoneScatter.TilingFillCount(1, 0));
    }

    [TestMethod]
    public void GroundCoverReflectsOnlyAtTheAllReflectionsSettingWithoutTheModelFlag()
    {
        var groundCover = new Gw2ZoneScatterInstance(default, default, default, 1f, 0u, 0, 0u, false) with { IsGroundCover = true };
        Assert.IsTrue(Gw2ZoneScatter.DrawsInWaterReflection(groundCover, 2));
        Assert.IsFalse(Gw2ZoneScatter.DrawsInWaterReflection(groundCover, 1));
        Assert.IsFalse(Gw2ZoneScatter.DrawsInWaterReflection(groundCover with { ModelFlags = Gw2ZoneScatter.ModelFlagNoGroundCoverReflection }, 2));
        Assert.IsTrue(Gw2ZoneScatter.DrawsInWaterReflection(groundCover with { IsGroundCover = false }, 0));
    }

    [TestMethod]
    public void PackedModelFlagsRemapToTheLiveRuntimeBits()
    {
        Assert.AreEqual(0x40008u, Gw2MapZoneModelFlags.FromPacked(0x100004));
        Assert.AreEqual(0x40002u, Gw2MapZoneModelFlags.FromPacked(0x100002));
        Assert.AreEqual(Gw2ZoneScatter.ModelFlagHighQualityOnly, Gw2MapZoneModelFlags.FromPacked(0x800000));
        Assert.AreEqual(Gw2ZoneScatter.ModelFlagNoGroundCoverReflection, Gw2MapZoneModelFlags.FromPacked(0x2000000));
    }


    [TestMethod]
    public void FloatingModelsRiseFromTheSeabedToTheGlobalWaterSurface()
    {
        var datPath = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(datPath) || !File.Exists(datPath)) Assert.Inconclusive("Gw2.dat is not installed.");
        using var reader = Gw2ContentReader.Open(datPath, verifyChecksums: false);
        var map = reader.ReadMap(GlobalWaterMapFileId)!;
        var origin = new Vector2(map.Info.Bounds[0], map.Info.Bounds[1]);
        var ground = Gw2TerrainGround.From(map.Terrain!, origin)!;
        var water = Gw2CollisionWater.From(map.Collision!);
        Assert.AreEqual(1, water.VolumeCount);

        var dry = Gw2ZoneScatter.Place(map.Zones!, Gw2ZoneScatterMapBounds.From(map.Info.Bounds), ground, (int)Gw2LodDistance.Medium);
        var wet = Gw2ZoneScatter.Place(map.Zones!, Gw2ZoneScatterMapBounds.From(map.Info.Bounds), ground, (int)Gw2LodDistance.Medium, water);
        var moved = dry.Zip(wet).Where(pair => pair.First.Position != pair.Second.Position).ToList();
        Console.WriteLine($"moved {moved.Count}");
        Assert.IsTrue(moved.Count > 100);
        Assert.IsTrue(moved.All(pair => (pair.Second.ModelFlags & Gw2ZoneScatter.ModelFlagFloatsOnWater) != 0));
        Assert.IsTrue(moved.All(pair => MathF.Abs(pair.Second.Position.Z - map.Collision!.WaterSurfaceZ) <= Gw2ZoneScatter.ZOffsetCeiling));
        Assert.IsTrue(moved.All(pair => pair.First.Position.Z > pair.Second.Position.Z));
    }

    [TestMethod]
    public void WaterVolumesAnswerTheNearestSurfaceAndInvertedVolumesOpenAirPockets()
    {
        var square = new[] { new Vector2(0, 0), new Vector2(100, 0), new Vector2(100, 100), new Vector2(0, 100) };
        var hole = new[] { new Vector2(40, 40), new Vector2(60, 40), new Vector2(60, 60), new Vector2(40, 60) };
        var collision = new Gw2MapCollision(default, default, 0u, 0f, [], [], [], [], [], [], [], 0,
            [new(1, "pond", 0u, [-50f, 200f], square), new(2, "island", Gw2CollisionWater.VolumeFlagInverted, [-50f, 200f], hole)]);
        var water = Gw2CollisionWater.From(collision);
        Assert.AreEqual(-50f, water.SurfaceZAt(new Vector3(10, 10, 120)));
        Assert.AreEqual(-50f, water.SurfaceZAt(new Vector3(10, 10, -80)));
        Assert.AreEqual(Gw2CollisionWater.NoSurface, water.SurfaceZAt(new Vector3(150, 10, 120)));
        Assert.AreEqual(200f, water.SurfaceZAt(new Vector3(50, 50, 120)));
    }

    [TestMethod]
    public void PlacementStaysOneUnitInsideTheMapBounds()
    {
        var bounds = new Gw2ZoneScatterMapBounds(new Vector2(-100, -200), new Vector2(300, 400));
        Assert.AreEqual(new Vector2(-99, 399), bounds.ClampPlacement(new Vector2(-150, 450)));
        Assert.AreEqual(new Vector2(10, 20), bounds.ClampPlacement(new Vector2(10, 20)));
    }

    [TestMethod]
    public void HsvColorFollowsTheExeConversion()
    {
        Assert.AreEqual(0x7F7F7F7Fu, Gw2ZoneScatter.HsvColor(0f, 0f, 0.5f, 0.5f));
        Assert.AreEqual(0xFFFF0000u, Gw2ZoneScatter.HsvColor(0f, 1f, 1f, 1f));
        Assert.AreEqual(0xFF00FF00u, Gw2ZoneScatter.HsvColor(1f / 3f, 1f, 1f, 1f));
        Assert.AreEqual(0xFF0000FFu, Gw2ZoneScatter.HsvColor(2f / 3f, 1f, 1f, 1f));
    }

    [TestMethod]
    public void ScattersTheFoothillsInsideEachZoneAndOnTheGround()
    {
        var datPath = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(datPath) || !File.Exists(datPath)) Assert.Inconclusive("Gw2.dat is not installed.");
        using var reader = Gw2ContentReader.Open(datPath, verifyChecksums: false);
        var map = reader.ReadMap(WayfarerFoothillsMapFileId)!;
        var origin = new Vector2(map.Info.Bounds[0], map.Info.Bounds[1]);
        var ground = Gw2TerrainGround.From(map.Terrain!, origin)!;

        var instances = Gw2ZoneScatter.Place(map.Zones!, Gw2ZoneScatterMapBounds.From(map.Info.Bounds), ground, (int)Gw2LodDistance.Medium);
        var again = Gw2ZoneScatter.Place(map.Zones!, Gw2ZoneScatterMapBounds.From(map.Info.Bounds), ground, (int)Gw2LodDistance.Medium);
        Console.WriteLine($"instances {instances.Count} models {instances.Select(instance => instance.ModelFileId).Distinct().Count()}");
        Assert.IsTrue(instances.Count > 10000);
        CollectionAssert.AreEqual(instances.ToList(), again.ToList());

        var minimumX = map.Zones!.Zones.Min(zone => zone.VertRect[0]) * Gw2ZoneScatter.CellSize + origin.X - 2 * Gw2ZoneScatter.CellSize;
        var maximumX = map.Zones.Zones.Max(zone => zone.VertRect[2]) * Gw2ZoneScatter.CellSize + origin.X + 2 * Gw2ZoneScatter.CellSize;
        Assert.IsTrue(instances.All(instance => instance.Position.X >= minimumX && instance.Position.X <= maximumX));
        Assert.IsTrue(instances.All(instance => instance.ModelFileId != 0));

        Assert.IsTrue(instances.Any(instance => instance.Color != Gw2ZoneScatter.UntintedColor && instance.Color != 0xFF7F7F7Fu));
        Console.WriteLine($"tinted {instances.Count(instance => instance.Color != 0xFF7F7F7Fu)} colours {instances.Select(instance => instance.Color).Distinct().Count()}");

        Assert.IsTrue(instances.All(instance => instance.FadeDistance > 0f));
        var groups = Gw2ZoneScatter.DrawGroups(instances).ToList();
        Console.WriteLine($"draw groups {groups.Count}");
        Assert.IsTrue(groups.All(group => group.All(instance => instance.FadeDistance == group.First().FadeDistance)));
        Assert.IsTrue(groups.Count < instances.Count / 10);

        Assert.IsTrue(instances.Count(instance => instance.IsGroundCover) > instances.Count / 2);

        var grounded = instances.Where(instance => (instance.ModelFlags & Gw2ZoneScatter.ModelFlagRandomHeight) == 0).ToList();
        var offGround = grounded.Count(instance => MathF.Abs(instance.Position.Z - ground.HeightAt(new Vector2(instance.Position.X, instance.Position.Y))) > 400f);
        Console.WriteLine($"off ground {offGround} of {grounded.Count}");
        Assert.IsTrue(offGround < grounded.Count / 20);
    }
}
