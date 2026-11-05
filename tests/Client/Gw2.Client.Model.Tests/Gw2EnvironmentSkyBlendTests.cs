using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2EnvironmentSkyBlendTests
{
    private const uint MapFileId = 187611;
    private const uint BeachMapFileBaseId = 3719751;
    private const uint SkyCardMapFileId = 1839188;
    private const float Dusk = 0.75f;

    private static Gw2ContentReader OpenReader()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        return Gw2ContentReader.Open(dat, verifyChecksums: false);
    }

    [TestMethod]
    public void SkyDomeBlendsLinearlyExceptHazeFalloffOnTheSquareRootCurve()
    {
        using var reader = OpenReader();
        var dome = reader.ReadMap(MapFileId)!.Environment!.Sky!.Dome!;

        var blend = Gw2EnvironmentSkyBlend.SkyDome(dome, Dusk);

        Assert.AreEqual(dome.Night.Brightness + (dome.Day.Brightness - dome.Night.Brightness) * Dusk, blend.Brightness, 1e-4f);
        Assert.AreEqual(dome.Night.HazeBottom + (dome.Day.HazeBottom - dome.Night.HazeBottom) * Dusk, blend.HazeBottom, 1e-4f);
        Assert.AreEqual(dome.Night.HazeDensity + (dome.Day.HazeDensity - dome.Night.HazeDensity) * Dusk, blend.HazeDensity, 1e-4f);
        Assert.AreEqual(dome.Night.LightIntensity + (dome.Day.LightIntensity - dome.Night.LightIntensity) * Dusk, blend.LightIntensity, 1e-4f);
        Assert.AreEqual(dome.Night.HazeFalloff + (dome.Day.HazeFalloff - dome.Night.HazeFalloff) * 0.5f, blend.HazeFalloff, 1e-4f);
        Assert.AreEqual(dome.Day.HazeFalloff, Gw2EnvironmentSkyBlend.SkyDome(dome, Gw2EnvironmentLightBlend.FullDay).HazeFalloff);
        Assert.AreEqual(dome.Night.HazeFalloff, Gw2EnvironmentSkyBlend.SkyDome(dome, Gw2EnvironmentLightBlend.FullNight).HazeFalloff);
    }

    [TestMethod]
    public void CloudLayerBlendsTheFirstTwoAttributeSets()
    {
        using var reader = OpenReader();
        var layer = reader.ReadMap(MapFileId)!.Environment!.Ambience!.CloudLayers[0];
        var day = layer.Attributes[0];
        var night = layer.Attributes[1];

        var blend = Gw2EnvironmentSkyBlend.CloudLayer(layer, Dusk)!.Value;

        Assert.AreEqual(night.Density + (day.Density - night.Density) * Dusk, blend.Density, 1e-4f);
        Assert.AreEqual(night.LightIntensity + (day.LightIntensity - night.LightIntensity) * Dusk, blend.LightIntensity, 1e-4f);
        Assert.AreEqual(day.Velocity, Gw2EnvironmentSkyBlend.CloudLayer(layer, Gw2EnvironmentLightBlend.FullDay)!.Value.Velocity);
        Assert.AreEqual(night.Velocity, Gw2EnvironmentSkyBlend.CloudLayer(layer, Gw2EnvironmentLightBlend.FullNight)!.Value.Velocity);
    }

    [TestMethod]
    public void SkyCardBlendsDayAndNightAttributes()
    {
        using var reader = OpenReader();
        var card = reader.ReadMap(SkyCardMapFileId)!.Environment!.SkyCards.First(card => card.Day is not null && card.Night is not null);

        var blend = Gw2EnvironmentSkyBlend.SkyCard(card, Dusk)!.Value;

        Assert.AreEqual(card.Night!.Brightness + (card.Day!.Brightness - card.Night.Brightness) * Dusk, blend.Brightness, 1e-4f);
        Assert.AreEqual(card.Night.MinHaze + (card.Day.MinHaze - card.Night.MinHaze) * Dusk, blend.MinHaze, 1e-4f);
        Assert.AreEqual(card.Day.Scale, Gw2EnvironmentSkyBlend.SkyCard(card, Gw2EnvironmentLightBlend.FullDay)!.Value.Scale);
        Assert.AreEqual(card.Night.Azimuth + (card.Day.Azimuth - card.Night.Azimuth) * Dusk, blend.Azimuth, 1e-4f);
        Assert.AreEqual((card.Night.Latitude + (card.Day.Latitude - card.Night.Latitude) * Dusk) * 1.570796f, blend.Latitude, 1e-4f);
        Assert.AreEqual(card.Night.Speed + (card.Day.Speed - card.Night.Speed) * Dusk, blend.Speed, 1e-4f);
    }

    [TestMethod]
    public void HerosMootTakesTheSkyOfItsZone()
    {
        using var reader = OpenReader();
        var environment = reader.ReadMap(MapFileId)!.Environment!;
        var herosMoot = new System.Numerics.Vector3(598.2f, -30356.2f, -1566.2f);

        var weights = Gw2EnvironmentZoneWeights.At(environment, herosMoot);
        var dome = Gw2EnvironmentSkyBlend.SkyDomeFromZones(environment, weights)!;

        Assert.AreEqual(environment.Sky!.Dome!.Flags, dome.Flags);
        Assert.AreEqual(1, weights.Count);
        Assert.AreEqual(1f, weights[0].Weight);
        Assert.AreEqual(-1f, environment.Sky.Dome.Day.LightIntensity);
        Assert.AreEqual(1f, dome.Day.LightIntensity, 1e-5f);
        Assert.AreEqual(0.7f, dome.Day.Brightness, 1e-5f);
        Assert.AreEqual(0.223054f, dome.Day.HazeBottom, 1e-5f);
    }

    [TestMethod]
    public void HerosMootDrawsOnlyTheCloudsOfItsZone()
    {
        using var reader = OpenReader();
        var environment = reader.ReadMap(MapFileId)!.Environment!;
        var herosMoot = new System.Numerics.Vector3(598.2f, -30356.2f, -1566.2f);

        var weights = Gw2EnvironmentZoneWeights.At(environment, herosMoot);
        var cloudEnvironments = Gw2EnvironmentSkyBlend.CloudEnvironmentsFromZones(environment, weights);

        Assert.AreEqual(1, cloudEnvironments.Count);
        Assert.AreSame(weights[0].Zone.Data, cloudEnvironments[0].Environment);
        Assert.AreEqual(1f, cloudEnvironments[0].Weight);
    }

    [TestMethod]
    public void CloudsOutsideEveryZoneAreTheGlobalCloudsAtFullDensity()
    {
        using var reader = OpenReader();
        var environment = reader.ReadMap(MapFileId)!.Environment!;

        var cloudEnvironments = Gw2EnvironmentSkyBlend.CloudEnvironmentsFromZones(environment, []);
        var layer = Gw2EnvironmentSkyBlend.CloudLayer(environment.Ambience!.CloudLayers[0], 1f)!.Value;

        Assert.AreEqual(1, cloudEnvironments.Count);
        Assert.AreSame(environment, cloudEnvironments[0].Environment);
        Assert.AreEqual(layer.Density * 0.5f, Gw2EnvironmentSkyBlend.WeightedByEnvironment(layer, 0.5f).Density);
    }

    [TestMethod]
    public void SkyDomeOutsideEveryZoneIsTheGlobalSky()
    {
        using var reader = OpenReader();
        var environment = reader.ReadMap(MapFileId)!.Environment!;

        Assert.AreEqual(environment.Sky!.Dome, Gw2EnvironmentSkyBlend.SkyDomeFromZones(environment, []));
    }

    [TestMethod]
    public void HerosMootTakesTheLightingAndFogOfItsZone()
    {
        using var reader = OpenReader();
        var environment = reader.ReadMap(MapFileId)!.Environment!;
        var weights = Gw2EnvironmentZoneWeights.At(environment, new System.Numerics.Vector3(598.2f, -30356.2f, -1566.2f));
        var zone = weights.Single().Zone.Data!;

        Assert.AreEqual(Gw2EnvironmentFog.FromEnvironment(zone, Dusk), Gw2EnvironmentFog.FromZones(environment, weights, Dusk));
        Assert.AreEqual(Gw2EnvironmentShLighting.FromEnvironment(zone, Dusk), Gw2EnvironmentShLighting.FromZones(environment, weights, Dusk));
        Assert.AreNotEqual(Gw2EnvironmentFog.FromEnvironment(environment, Dusk), Gw2EnvironmentFog.FromZones(environment, weights, Dusk));
    }

    [TestMethod]
    public void LightingAndFogOutsideEveryZoneAreTheGlobalOnes()
    {
        using var reader = OpenReader();
        var environment = reader.ReadMap(MapFileId)!.Environment!;

        Assert.AreEqual(Gw2EnvironmentFog.FromEnvironment(environment, Dusk), Gw2EnvironmentFog.FromZones(environment, [], Dusk));
        Assert.AreEqual(Gw2EnvironmentShLighting.FromEnvironment(environment, Dusk), Gw2EnvironmentShLighting.FromZones(environment, [], Dusk));
    }

    [TestMethod]
    public void SunColourIsLightZeroColourTimesIntensityLikeTheLiveClient()
    {
        using var reader = OpenReader();
        var environment = reader.ReadMap(BeachMapFileBaseId)!.Environment!;

        var sun = Gw2EnvironmentLightBlend.DayNight(environment, 0f).Lights[0];

        Assert.AreEqual(0.645f, sun.ScaledColor.X, 1e-3f);
        Assert.AreEqual(0.626f, sun.ScaledColor.Y, 1e-3f);
        Assert.AreEqual(0.626f, sun.ScaledColor.Z, 1e-3f);
    }
}
