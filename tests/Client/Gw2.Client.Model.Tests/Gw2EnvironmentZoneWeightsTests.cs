using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2EnvironmentZoneWeightsTests
{
    private const float InchesPerMeter = 1f / 0.0254f;

    private static readonly Vector2[] Square = [new(-100f, -100f), new(100f, -100f), new(100f, 100f), new(-100f, 100f)];

    [TestMethod]
    public void PolygonIsFullInsideAndFadesTowardTheEdgesAndCaps()
    {
        var shape = new Gw2MapEnvShape(Vector3.Zero, 200f, 50f, 20f, 0f, Square, 2);

        Assert.AreEqual(1f, Gw2EnvironmentZoneWeights.ShapeWeight(shape, new Vector3(0f, 0f, 0f)));
        Assert.AreEqual(0.25f * 0.25f * 4f, Gw2EnvironmentZoneWeights.ShapeWeight(shape, new Vector3(75f, 0f, 0f))!.Value, 1e-5f);
        Assert.AreEqual(0.5f, Gw2EnvironmentZoneWeights.ShapeWeight(shape, new Vector3(0f, 0f, 90f))!.Value, 1e-5f);
        Assert.IsNull(Gw2EnvironmentZoneWeights.ShapeWeight(shape, new Vector3(150f, 0f, 0f)));
        Assert.IsNull(Gw2EnvironmentZoneWeights.ShapeWeight(shape, new Vector3(0f, 0f, 150f)));
    }

    [TestMethod]
    public void PolygonWithoutHorizontalFadeIsFullAwayFromTheCaps()
    {
        var shape = new Gw2MapEnvShape(Vector3.Zero, 200f, 0f, 20f, 0f, Square, 2);

        Assert.AreEqual(1f, Gw2EnvironmentZoneWeights.ShapeWeight(shape, new Vector3(99f, 0f, 0f)));
    }

    [TestMethod]
    public void SphereAndCylinderFadeBetweenInnerAndOuterRadius()
    {
        var sphere = new Gw2MapEnvShape(Vector3.Zero, 0f, 10f, 20f, 0f, [], 0);
        var cylinder = new Gw2MapEnvShape(Vector3.Zero, 5f, 10f, 20f, 0f, [], 1);

        Assert.AreEqual(1f, Gw2EnvironmentZoneWeights.ShapeWeight(sphere, new Vector3(0f, 0f, 5f)));
        Assert.AreEqual((400f - 225f) / 300f, Gw2EnvironmentZoneWeights.ShapeWeight(sphere, new Vector3(0f, 0f, 15f))!.Value, 1e-5f);
        Assert.AreEqual((400f - 225f) / 300f, Gw2EnvironmentZoneWeights.ShapeWeight(cylinder, new Vector3(15f, 0f, 4f))!.Value, 1e-5f);
        Assert.IsNull(Gw2EnvironmentZoneWeights.ShapeWeight(cylinder, new Vector3(0f, 0f, 6f)));
    }

    [TestMethod]
    public void ZoneWithSeveralShapesTakesTheLargestWeight()
    {
        var full = new Gw2MapEnvShape(Vector3.Zero, 0f, 10f, 20f, 0f, [], 0);
        var fading = new Gw2MapEnvShape(new Vector3(10f, 0f, 0f), 0f, 1f, 20f, 0f, [], 0);
        var zone = new Gw2MapEnvZone("", 0, 0, 0, [fading, full], [], null);

        Assert.AreEqual(1f, Gw2EnvironmentZoneWeights.ZoneWeight(zone, new Vector3(5f, 0f, 0f)));
    }

    [TestMethod]
    public void CampOnMap1622SitsInsideOneZoneWithoutItsOwnEffectWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var environment = reader.ReadMap(3776288)!.Environment!;
        var camp = new Vector3(-372.1433f, 415.8766f, -52.08258f) * InchesPerMeter;

        var weights = Gw2EnvironmentZoneWeights.At(environment, camp);

        Assert.AreEqual(1, weights.Count);
        Assert.AreEqual(1f, weights[0].Weight);
        Assert.IsNull(weights[0].Zone.Data?.Effect);
        var night = Gw2EnvironmentBloom.FromZones(environment, weights, dayWeight: 0f)!;
        Assert.AreEqual(0.412f, night.BloomAmountConstant.X, 1e-3f);
        Assert.AreEqual(Gw2EnvironmentGrade.FromEnvironment(environment, 0f), Gw2EnvironmentGrade.FromZones(environment, weights, 0f));
    }

    [TestMethod]
    public void ZoneEffectReplacesTheGlobalEffectAtFullWeightWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var environment = reader.ReadMap(3776288)!.Environment!;
        var zone = environment.Zones.First(zone => zone.Data?.Effect is { Saturation: 1.3f });

        var halfGlobal = Gw2EnvironmentGrade.FromZones(environment, [new(zone, 0.5f)])!;
        var overfull = Gw2EnvironmentGrade.FromZones(environment, [new(zone, 1f), new(zone, 1f)])!;

        Assert.AreEqual((1.3f + Gw2EnvironmentGrade.FromEnvironment(environment)!.Saturation) * 0.5f, halfGlobal.Saturation, 1e-5f);
        Assert.AreEqual(1.3f, overfull.Saturation, 1e-5f);
    }
}
