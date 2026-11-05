using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2PropLodTests
{
    private const uint WayfarerFoothillsMapFileId = 187611;

    [TestMethod]
    public void OverriddenPlacementsStepThroughTheLevelsAtTheirScaledDistances()
    {
        var datPath = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(datPath) || !File.Exists(datPath)) Assert.Inconclusive("Gw2.dat is not installed.");
        using var reader = Gw2ContentReader.Open(datPath, verifyChecksums: false);
        var placements = reader.ReadMap(WayfarerFoothillsMapFileId)!.Props!.Placements;

        var overridden = placements.Where(placement => (placement.Flags & Gw2MapPropFlags.OverrideLod) != 0 && (placement.Lod1 ?? 0f) > 0f).ToList();
        Console.WriteLine($"placements {placements.Count} overridden {overridden.Count}");
        Assert.IsTrue(overridden.Count > 0);

        var placement = overridden[0];
        var distances = Gw2PropLodDistances.For(placement, null);
        Assert.AreEqual(Math.Min(placement.Lod1!.Value, Gw2PropLodDistances.MaximumDistance), distances.Lod1);
        var near = placement.Scale * distances.Lod1;
        var far = placement.Scale * distances.Lod2;
        Vector3 CameraAt(float distance) => placement.Position + new Vector3(distance, 0f, 0f);

        Assert.AreEqual(0, Gw2PropLod.Select(distances, placement.Scale, 1f, CameraAt(near * 0.5f), placement.Position, 2));
        Assert.AreEqual(0, Gw2PropLod.Select(distances, placement.Scale, 1f, CameraAt(near + Gw2PropLod.Hysteresis * 0.5f), placement.Position, 0));
        if (far > near + Gw2PropLod.Hysteresis)
            Assert.AreEqual(1, Gw2PropLod.Select(distances, placement.Scale, 1f, CameraAt((near + Gw2PropLod.Hysteresis + far) * 0.5f), placement.Position, 0));
        Assert.AreEqual(2, Gw2PropLod.Select(distances, placement.Scale, 1f, CameraAt(far + Gw2PropLod.Hysteresis + 1f), placement.Position, 0));
    }

    [TestMethod]
    public void UnoverriddenPlacementsUseTheEngineDistances()
    {
        var placement = new Gw2MapPropPlacement(1, Vector3.Zero, Vector3.Zero, 1f, Lod1: 500f, Lod2: 900f);
        Assert.AreEqual(new Gw2PropLodDistances(Gw2PropLodDistances.EngineLod1, Gw2PropLodDistances.EngineLod2), Gw2PropLodDistances.For(placement, null));
        var infinite = placement with { Flags = Gw2MapPropFlags.OverrideLod, Lod2 = float.PositiveInfinity };
        Assert.AreEqual(Gw2PropLodDistances.MaximumDistance, Gw2PropLodDistances.For(infinite, null).Lod2);
    }

    [TestMethod]
    public void DropLod2HidesBeyondTheFarDistanceAndDisableLodPinsTheDetailLevel()
    {
        var distances = new Gw2PropLodDistances(100f, 200f);
        var far = new Vector3(1000f, 0f, 0f);
        Assert.AreEqual(Gw2PropLod.Hidden, Gw2PropLod.SelectForPlacement(Gw2MapPropFlags.DropLod2, distances, 2f, 1f, far, Vector3.Zero, 0));
        Assert.AreEqual(0, Gw2PropLod.SelectForPlacement(Gw2MapPropFlags.DisableLod, distances, 2f, 1f, far, Vector3.Zero, 0));
        Assert.AreEqual(2, Gw2PropLod.SelectForPlacement(0, distances, 2f, 1f, far, Vector3.Zero, Gw2PropLod.Hidden));
    }

    [TestMethod]
    public void LodDistanceSettingScalesTheLevelDistancesAndPicksTheScatterQualityMode()
    {
        Assert.AreEqual(1f, Gw2LodDistanceSetting.DistanceFactor(Gw2LodDistance.Low));
        Assert.AreEqual(1f, Gw2LodDistanceSetting.DistanceFactor(Gw2LodDistance.Medium));
        Assert.AreEqual(1.5f, Gw2LodDistanceSetting.DistanceFactor(Gw2LodDistance.High));
        Assert.AreEqual(2f, Gw2LodDistanceSetting.DistanceFactor(Gw2LodDistance.Ultra));
        Assert.AreEqual(2, Gw2LodDistanceSetting.ScatterQualityMode(Gw2LodDistance.High));

        var distances = new Gw2PropLodDistances(Gw2PropLodDistances.EngineLod1, Gw2PropLodDistances.EngineLod2);
        var camera = new Vector3(Gw2PropLodDistances.EngineLod2 + Gw2PropLod.Hysteresis + 1f, 0f, 0f);
        Assert.AreEqual(2, Gw2PropLod.Select(distances, 1f, 1f, camera, Vector3.Zero, 0));
        Assert.AreEqual(1, Gw2PropLod.Select(distances, 1f, Gw2LodDistanceSetting.DistanceFactor(Gw2LodDistance.High), camera, Vector3.Zero, 0));
    }
}
