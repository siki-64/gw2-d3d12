using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2MapLightsTests
{
    private const uint LightsV19FileId = 187611;

    [TestMethod]
    public void ReadsCurvesSpotLodsAndPortalLightsWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var lights = Gw2MapLights.TryParse(reader.ReadDocument(LightsV19FileId))!;

        Assert.AreEqual(619, lights.PointLights.Count);
        Assert.AreEqual(641, lights.PointLights.Sum(group => group.Curves.Count) + lights.SpotLights.Sum(group => group.Curves.Count));
        var spots = lights.SpotLights.SelectMany(group => group.Lights).ToArray();
        Assert.AreEqual(44, spots.Length);
        Assert.IsTrue(spots.All(spot => spot.Lods.Count == 2));
        Assert.IsTrue(spots.All(spot => spot.ShadowFileId == 0));
        Assert.AreEqual(27, lights.PortalLights.Count);
        Assert.IsTrue(lights.PortalLights.All(portal => portal.Points.Count == 4));

        var volume = lights.VolumeLights.Single();
        Assert.AreEqual("PuzzleCave", volume.Name);
        Assert.AreEqual(new System.Numerics.Vector3(4096, 3840, 1536), volume.Extents);
        Assert.AreEqual(1, volume.FloodPoints.Count);
        Assert.AreEqual(669u, volume.BroadId);
        Assert.AreEqual(4, volume.Images.Count);
        Assert.AreEqual(2324673u, volume.Images[0].FileId);
        Assert.AreEqual(0x35545844u, volume.Images[0].Format);
        Assert.AreEqual(670u, lights.Broadphase!.MaxBroadId);
        Assert.AreEqual(21444, lights.Broadphase.Data.Length);
    }

    [TestMethod]
    public void ReadsBroadIdsLightConstantsAndAreaPolygonsWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var document = reader.ReadDocument(LightsV19FileId);
        var lights = Gw2MapLights.TryParse(document)!;

        Assert.AreEqual(16u, lights.MaxLightVsConstants);
        Assert.AreEqual(1u, lights.PointLights[0].BroadId);
        Assert.AreEqual(647u, lights.SpotLights[0].BroadId);
        Assert.AreEqual(620u, lights.PortalLights[0].BroadId);

        var areas = Gw2MapAreas.TryParse(document)!.Areas;
        Assert.AreEqual(52, areas.Count);
        Assert.IsTrue(areas.All(area => area.Polygon!.Points.Count > 0 && area.Volume is not null));
        Assert.AreEqual(5, areas[0].Polygon!.Points.Count);
        Assert.AreEqual(500f, areas[0].Polygon!.Height);
    }

    [TestMethod]
    public void CampPointLightMatchesTheLiveRuntimeLightAtFullDayWeightWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var lights = reader.ReadMap(3776288)!.Lights!;
        var camp = new System.Numerics.Vector3(-14608.908f, 17226.795f, -2381.3792f);
        var group = lights.PointLights.Single(candidate => candidate.Lights[0].Position == camp);

        var frame = new Gw2MapPointLightAnimation(group).Advance(dayWeight: 1f, mapLightSeconds: 10f, elapsedSeconds: 0.016f);

        Assert.AreEqual(0.94871807f, frame.ScaledColor.X, 1e-5f);
        Assert.AreEqual(0.44273517f, frame.ScaledColor.Y, 1e-5f);
        Assert.AreEqual(0f, frame.ScaledColor.Z);
        Assert.AreEqual(541.90967f, frame.NearDistance, 1e-3f);
        Assert.AreEqual(1083.8193f, frame.FarDistance, 1e-3f);
        Assert.IsTrue(frame.Enabled);
    }

    [TestMethod]
    public void SpotLightBlendsDayAndNightLightsAndKeepsTheDayPositionWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var lights = reader.ReadMap(3776288)!.Lights!;
        Assert.AreEqual(6, lights.SpotLights.Count);
        var lamp = new System.Numerics.Vector3(10079.348f, 25834.63f, -9847.692f);
        var group = lights.SpotLights.Single(candidate => Vector3Near(candidate.Lights[0].Position, lamp));

        var frame = new Gw2MapSpotLightAnimation(group).Advance(dayWeight: 0.5f, mapLightSeconds: 10f, elapsedSeconds: 0.016f);

        Assert.AreEqual(group.Lights[0].Position, frame.Position);
        Assert.AreEqual(3.1491709f, frame.Intensity, 1e-4f);
        Assert.AreEqual(99.5f / 255f, frame.Color.X, 1e-5f);
        Assert.AreEqual(124f / 255f, frame.Color.Y, 1e-5f);
        Assert.AreEqual(1f, frame.Color.Z, 1e-5f);
        Assert.AreEqual(1f, frame.Direction.Length(), 1e-5f);
        Assert.AreEqual(0.39670065f, frame.InnerAngle, 1e-6f);
        Assert.AreEqual(0.9236743f, frame.OuterAngle, 1e-6f);
        Assert.IsTrue(frame.Enabled);
    }

    [TestMethod]
    public void SpotLightMatchesTheLiveRuntimeSpotLightAtNightWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var lights = reader.ReadMap(3776288)!.Lights!;
        var spot = new System.Numerics.Vector3(1769.0054f, 203.02069f, -3345.374f);
        var group = lights.SpotLights.Single(candidate => Vector3Near(candidate.Lights[0].Position, spot));

        var frame = new Gw2MapSpotLightAnimation(group).Advance(dayWeight: 0f, mapLightSeconds: 10f, elapsedSeconds: 0.016f);

        Assert.AreEqual(0.79168f, frame.ScaledColor.X, 1e-5f);
        Assert.AreEqual(0.609468f, frame.ScaledColor.Y, 1e-5f);
        Assert.AreEqual(0.326725f, frame.ScaledColor.Z, 1e-5f);
        Assert.AreEqual(0.513537f, frame.Direction.X, 1e-5f);
        Assert.AreEqual(0.979315f, MathF.Cos(frame.InnerAngle), 1e-5f);
        Assert.AreEqual(0.922341f, MathF.Cos(frame.OuterAngle), 1e-5f);
        Assert.AreEqual(3702.2871f, frame.NearDistance, 1e-3f);
        Assert.AreEqual(5691.0391f, frame.FarDistance, 1e-3f);
    }

    private static bool Vector3Near(System.Numerics.Vector3 left, System.Numerics.Vector3 right) =>
        System.Numerics.Vector3.Distance(left, right) < 0.01f;

    [TestMethod]
    public void CurveTypeMaskSelectsTheHighestSetBit()
    {
        Assert.AreEqual(0, Gw2MapLightIntensity.CurveShapeOf(0x40001));
        Assert.AreEqual(4, Gw2MapLightIntensity.CurveShapeOf(0x10));
        Assert.AreEqual(0, Gw2MapLightIntensity.CurveShapeOf(0));
    }
}
