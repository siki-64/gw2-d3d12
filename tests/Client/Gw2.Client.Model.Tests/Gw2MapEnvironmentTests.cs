using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2MapEnvironmentTests
{
    private const uint MapFileId = 187611;
    private const uint SkyCardMapFileId = 1839188;
    private const uint ColoredLightRingsMapFileId = 1513607;
    private const uint SpawnMapFileId = 3718508;
    private const uint OverrideMapFileId = 3399578;

    [TestMethod]
    public void ReadsSkyCardsWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var cards = reader.ReadMap(SkyCardMapFileId)!.Environment!.SkyCards;
        Assert.AreEqual(24, cards.Count);

        var materialCard = cards[0];
        Assert.AreEqual(56u, materialCard.Flags);
        Assert.AreEqual(new System.Numerics.Vector3(-55300, -7319, -22000), materialCard.Location);
        Assert.AreEqual(-1.4239185f, materialCard.Day!.Azimuth, 1e-6f);
        Assert.AreEqual(0.933333f, materialCard.Day.HazeDensity, 1e-6f);
        Assert.AreEqual(new System.Numerics.Vector2(12, 12), materialCard.Day.Scale);
        Assert.AreEqual(1255376u, materialCard.Day.Texture.FileId);
        var material = materialCard.Material!;
        Assert.AreEqual(1262294u, material.MaterialFile.FileId);
        Assert.AreEqual(12, material.Constants.Count);
        Assert.AreEqual(874311016u, material.Constants[0].Token);
        Assert.AreEqual(5, material.Textures.Count);
        Assert.AreEqual(1839176u, material.Textures[1].File.FileId);
        CollectionAssert.AreEqual(new float[] { 0, 5, 5, 0 }, material.Textures[1].TextureUV);
        Assert.AreEqual(new System.Numerics.Vector2(0.25f, 2), material.TextureAnimation!.ScaleRangeX);
        Assert.AreEqual(6u, material.TextureAnimation.TexCoords);
        Assert.AreEqual(1u, material.Flipbook!.Count);
        Assert.AreEqual(1f, material.Flipbook.Fps);

        var lensFlare = cards[2].Day!.LensFlare!;
        Assert.AreEqual(4, lensFlare.Atoms.Count);
        Assert.AreEqual(2u, lensFlare.Atoms[1].Rows);
        Assert.AreEqual(new System.Numerics.Vector2(40, 40), lensFlare.Atoms[1].Scale);
        Assert.AreEqual(4.704594f, lensFlare.Atoms[1].CameraRotation, 1e-6f);
        Assert.AreEqual(4, lensFlare.Atoms[0].Color.Length);
        Assert.AreEqual(1498572u, lensFlare.Textures[0].FileId);
        Assert.AreEqual(1498571u, lensFlare.MaterialFile.FileId);
        Assert.AreEqual(0.306905f, lensFlare.FadeBand.Y, 1e-6f);
        Assert.AreEqual(1f, lensFlare.OpacityCoeff);
    }

    [TestMethod]
    public void ProjectsLightsIntoTheShBlockTheLiveClientBindsForCharacters()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var lighting = Gw2EnvironmentShLighting.FromEnvironment(reader.ReadMap(MapFileId)!.Environment!);

        AssertNear(new(0.0466f, 0.0892f, 0.2523f, 0.2254f), lighting.ShRed);
        AssertNear(new(0.0872f, 0.1011f, 0.3111f, 0.2933f), lighting.ShGreen);
        AssertNear(new(0.1583f, 0.1197f, 0.3955f, 0.3898f), lighting.ShBlue);
        AssertNear(new(-0.5736f, 0f, -0.8192f, -0.4233f), lighting.ShSunData);
        AssertNear(new(1.1f, 1.0051f, 0.9059f, 1.1f), lighting.ShSunColor);
        AssertNear(new(0.62f, 0.76f, 1f, 0f), lighting.CharLightCurve);
    }

    [TestMethod]
    public void BlendsDayAndNightLightingComponentWiseLikeTheEnvironmentAccumulator()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var environment = reader.ReadMap(MapFileId)!.Environment!;
        Assert.AreNotEqual(0, environment.NightLights.Count);
        Assert.IsNotNull(environment.NightBacklight);

        var day = Gw2EnvironmentLightBlend.DayNight(environment, Gw2EnvironmentLightBlend.FullDay);
        var night = Gw2EnvironmentLightBlend.DayNight(environment, Gw2EnvironmentLightBlend.FullNight);
        var dusk = Gw2EnvironmentLightBlend.DayNight(environment, 0.25f);

        Assert.AreEqual(environment.NightLights.Count, night.Lights.Count);
        Assert.AreEqual(environment.NightBacklight!.ShadowInfluence, night.ShadowInfluence);
        var daySun = day.Lights[0];
        var nightSun = night.Lights[0];
        var duskSun = dusk.Lights[0];
        AssertNear(new(daySun.Color * 0.25f + nightSun.Color * 0.75f, daySun.Intensity * 0.25f + nightSun.Intensity * 0.75f), new(duskSun.Color, duskSun.Intensity));
        AssertNear(new(daySun.Direction * 0.25f + nightSun.Direction * 0.75f, 0f), new(duskSun.Direction, 0f));
        Assert.AreEqual(day.ShadowInfluence * 0.25f + night.ShadowInfluence * 0.75f, dusk.ShadowInfluence, 1e-6f);
    }

    [TestMethod]
    public void PacksTheHazeRecordIntoTheFogConstants()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var environment = reader.ReadMap(MapFileId)!.Environment!;
        var fog = Gw2EnvironmentFog.FromEnvironment(environment)!;

        AssertNear(new(-0.15294f, -0.06667f, 0f, 0f), fog.FogColorNearMinusFar);
        AssertNear(new(0.76078f, 0.86667f, 1f, 1f), fog.FogColorFar);
        AssertNear(new(0.01176f, 0.16863f, 0.15686f, 1f), fog.FogColorHeight);
        AssertNear(new(0.000861f), fog.FogDepthCue);
        AssertNear(new(1f / (4632.4434f - 212.245f), -212.245f / (4632.4434f - 212.245f), 1f / 158.73f, -1f), fog.FogParam0);
        AssertNear(new(fog.FogParam0.X, fog.FogParam0.Y, 0f, 0f), fog.FogParam1);
    }

    [TestMethod]
    public void NightFogMatchesTheLiveRuntimeFogOnMap1622WhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var environment = reader.ReadMap(3776288)!.Environment!;
        var fog = Gw2EnvironmentFog.FromEnvironment(environment, dayWeight: 0f)!;

        AssertNear(new(0.278431f - 0.215686f, 0.054902f - 0.254902f, 0.290196f - 0.411765f, 0f), fog.FogColorNearMinusFar);
        AssertNear(new(0.215686f, 0.254902f, 0.411765f, fog.FogColorFar.W), fog.FogColorFar);
        AssertNear(new(0.000261f), fog.FogDepthCue);
        Assert.AreEqual(1f / (8814.621f - 228.571f), fog.FogParam0.X, 1e-8f);
        Assert.AreEqual(-228.571f / (8814.621f - 228.571f), fog.FogParam0.Y, 1e-5f);
    }

    [TestMethod]
    public void HalfDayFogIsTheMeanOfDayAndNightWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var environment = reader.ReadMap(3776288)!.Environment!;
        var day = Gw2EnvironmentFog.FromEnvironment(environment, 1f)!;
        var night = Gw2EnvironmentFog.FromEnvironment(environment, 0f)!;
        var dusk = Gw2EnvironmentFog.FromEnvironment(environment, 0.5f)!;

        AssertNear((day.FogColorFar + night.FogColorFar) * 0.5f, dusk.FogColorFar);
        AssertNear((day.FogColorHeight + night.FogColorHeight) * 0.5f, dusk.FogColorHeight);
        AssertNear((day.FogDepthCue + night.FogDepthCue) * 0.5f, dusk.FogDepthCue);
    }

    [TestMethod]
    public void GradeMatchesTheLiveCombineConstantsOnMap1622WhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var grade = Gw2EnvironmentGrade.FromEnvironment(reader.ReadMap(3776288)!.Environment!)!;

        Assert.AreEqual(0.926f, grade.Saturation, 1e-3f);
        AssertNear(new(0.306f, 0.796f, 0.886f, 0f), grade.TintConstant);
        Assert.IsTrue(grade.AppliesSaturation);
        Assert.IsFalse(grade.AppliesTint);
        AssertNear(new(0.926f * 0.98f + 0.008f), grade.SaturationConstant);
        AssertNear(new(1f, 1f, 1f, 0f), grade.TintTargetConstant);
    }

    [TestMethod]
    public void BloomMatchesTheLiveCombineConstantsOnMap1622WhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var environment = reader.ReadMap(3776288)!.Environment!;
        var night = Gw2EnvironmentBloom.FromEnvironment(environment, dayWeight: 0f)!;
        var day = Gw2EnvironmentBloom.FromEnvironment(environment)!;
        var dusk = Gw2EnvironmentBloom.FromEnvironment(environment, dayWeight: 0.5f)!;

        Assert.IsTrue(night.IsActive);
        AssertNear(new(0.412f, 0.298f, 0.165f, 0f), night.BloomAmountConstant);
        Assert.AreEqual(1.3f, night.BlurGain, 1e-3f);
        AssertNear(new(0.212f, 0.165f, 0.106f, 0f), day.BloomAmountConstant);
        AssertNear(System.Numerics.Vector4.Zero, day.DownsampleLevelConstant);
        AssertNear((day.Glow + night.Glow) * 0.5f, dusk.Glow);
    }

    [TestMethod]
    public void AmbientOcclusionBlendsDayAndNightWithTheExeClampsWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var occlusion = Gw2EnvironmentAmbientOcclusion.FromEnvironment(reader.ReadMap(MapFileId)!.Environment!);
        Assert.AreEqual(Gw2EnvironmentAmbientOcclusion.WithoutEnvironment, occlusion);
        Assert.IsTrue(occlusion.IsActive);
        Assert.AreEqual(1.5f, occlusion.SunAmount, 1e-6f);

        var environment = reader.ReadMap(3776288)!.Environment!;
        var day = Gw2EnvironmentAmbientOcclusion.FromEffect(environment.Effect!);
        var night = Gw2EnvironmentAmbientOcclusion.FromEffect(environment.NightEffect!);
        var dusk = Gw2EnvironmentAmbientOcclusion.FromEnvironment(environment, dayWeight: 0.5f);
        Assert.AreEqual(Math.Clamp((day.Amount + night.Amount) * 0.5f, 0f, 5f), dusk.Amount, 1e-5f);
        Assert.AreEqual(Math.Clamp((day.Contrast + night.Contrast) * 0.5f, 0.5f, 1.5f), dusk.Contrast, 1e-5f);
    }

    [TestMethod]
    public void DepthBlurAndLightAdaptationReadTheExeRecordsWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var environment = reader.ReadMap(MapFileId)!.Environment!;
        Assert.AreEqual(new Gw2EnvironmentDepthBlur(3000f, 7000f), Gw2EnvironmentDepthBlur.FromZones(environment, []));

        var adaptation = Gw2EnvironmentLightAdaptation.FromZones(environment, []);
        Assert.AreEqual(2.5f, adaptation.BrightTime, 1e-6f);
        Assert.AreEqual(5f, adaptation.DimTime, 1e-6f);
        Assert.AreEqual(1.2f, adaptation.DarkCoeff, 1e-6f);
        Assert.AreEqual(0.8f, adaptation.DarkExp, 1e-6f);
        Assert.AreEqual(0.1f, adaptation.DarkMin, 1e-6f);
        Assert.AreEqual(0.3f, adaptation.DarkMax, 1e-6f);
        Assert.AreEqual(0.3f, adaptation.BrightMin, 1e-6f);
        Assert.AreEqual(0.5f, adaptation.BrightMax, 1e-6f);
        Assert.AreEqual(Gw2EnvironmentLightAdaptation.WithoutExposure, adaptation);
    }

    private static void AssertNear(System.Numerics.Vector4 expected, System.Numerics.Vector4 actual) =>
        Assert.IsTrue(System.Numerics.Vector4.Distance(expected, actual) < 1e-3f, $"expected {expected}, got {actual}");

    [TestMethod]
    public void ReadsPostEffectFieldsWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var effect = reader.ReadMap(MapFileId)!.Environment!.Effect!;
        CollectionAssert.AreEqual(new byte[] { 255, 229, 151, 1 }, effect.TintColor);
        CollectionAssert.AreEqual(new byte[] { 26, 26, 26, 0 }, effect.Glow);
        CollectionAssert.AreEqual(new byte[] { 39, 39, 39, 0 }, effect.GlowLevel);
        Assert.AreEqual(3000f, effect.FocalDepth);
        Assert.AreEqual(7000f, effect.FocalRange);
        Assert.AreEqual(3f, effect.SsaoAmount);
        Assert.AreEqual(0.2f, effect.SsaoBrighten, 1e-6f);
        Assert.AreEqual(1f, effect.SsaoContrast);
        Assert.AreEqual(0.5f, effect.SsaoSunScale);
    }

    [TestMethod]
    public void ReadsSkyDomeWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var sky = reader.ReadMap(MapFileId)!.Environment!.Sky!;
        var dome = sky.Dome!;
        Assert.AreEqual((byte)1, dome.Flags);
        Assert.AreEqual(1f, dome.Day.Brightness);
        Assert.AreEqual(0.596708f, dome.Day.HazeFalloff, 1e-6f);
        Assert.AreEqual(-1f, dome.Day.LightIntensity);
        Assert.AreEqual(0.862262f, dome.Night.Brightness, 1e-6f);
        Assert.AreEqual(0.057377f, dome.Night.HazeBottom, 1e-6f);
        Assert.AreEqual(15f, dome.Night.StarDensity);
        Assert.AreEqual(0f, dome.VerticalOffset);
        Assert.AreEqual(0, sky.ModeCubeTextures.Count);
    }

    [TestMethod]
    public void ReadsSkyModeTexturesAsFiles()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var modes = reader.ReadMap(MapFileId)!.Environment!.Sky!.ModeTextures;
        Assert.AreEqual(4, modes.Count);
        Assert.AreEqual(187554u, modes[0].NorthEast.FileId);
        Assert.AreEqual(187556u, modes[0].SouthWest.FileId);
        Assert.AreEqual(187558u, modes[0].Top.FileId);
        Assert.AreEqual(186323u, modes[1].NorthEast.FileId);
        Assert.AreEqual(187564u, modes[2].Top.FileId);
        Assert.AreEqual(187554u, modes[3].NorthEast.FileId);
    }

    [TestMethod]
    public void ReadsExposureAndGustSpeedWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var environment = reader.ReadMap(MapFileId)!.Environment!;
        Assert.AreEqual((byte)128, environment.Wind!.GustSpeed);
        var exposure = environment.Exposure!;
        Assert.AreEqual(2.5f, exposure.BrightTime);
        Assert.AreEqual(5f, exposure.DimTime);
        Assert.AreEqual(1.2f, exposure.DarkCoeff, 1e-6f);
        Assert.AreEqual(0.8f, exposure.DarkExp, 1e-6f);
        Assert.AreEqual(0.1f, exposure.DarkMin, 1e-6f);
        Assert.AreEqual(0.3f, exposure.DarkMax, 1e-6f);
        Assert.AreEqual(0.3f, exposure.BrightMin, 1e-6f);
        Assert.AreEqual(0.5f, exposure.BrightMax, 1e-6f);
        CollectionAssert.AreEqual(new float[] { 0, 0, 0, 0 }, exposure.WaterReflectionParams);
    }

    [TestMethod]
    public void ReadsParticleFieldsWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var environment = reader.ReadMap(MapFileId)!.Environment!;
        Assert.AreEqual("Snow_0", environment.ParticleFields[0].Name);
        var water = environment.Zones.SelectMany(zone => zone.Data!.ParticleFields).First(field => field.Name == "Water");
        Assert.AreEqual("Water", water.Name);
        Assert.AreEqual((byte)2, water.Type);
        Assert.AreEqual(41u, water.Flags);
        Assert.AreEqual(84521u, water.Seed);
        Assert.AreEqual(3407u, water.ParticleCount);
        Assert.AreEqual(316u, water.Extent);
        Assert.AreEqual(54.594597f, water.Deviation, 1e-5f);
        Assert.AreEqual(new System.Numerics.Vector3(0, 0, 1), water.FieldDirection);
        Assert.AreEqual(4u, water.TextureColumns);
        Assert.AreEqual(4u, water.TextureRows);
        Assert.AreEqual(186298u, water.Texture.FileId);
        Assert.IsTrue(environment.ParticleFieldCutouts.Count > 0);
        Assert.AreEqual(4, environment.ParticleFieldCutouts[0].TransformRowX.Length);
    }

    [TestMethod]
    public void ReadsAmbienceWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var ambience = reader.ReadMap(MapFileId)!.Environment!.Ambience!;
        Assert.AreEqual(1u, ambience.Flags);
        Assert.AreEqual(4, ambience.NightMods.Length);
        var layer = ambience.CloudLayers[0];
        Assert.AreEqual(11946.3f, layer.Altitude, 1e-2f);
        Assert.AreEqual(186345u, layer.Texture.FileId);
        Assert.AreEqual(3, layer.Attributes.Count);
        Assert.AreEqual(new System.Numerics.Vector2(-15.015f, -117.117f), layer.Attributes[0].Velocity);
        Assert.AreEqual(300f, layer.Attributes[0].FadeEnd);

        var rings = reader.ReadMap(ColoredLightRingsMapFileId)!.Environment!.Ambience!.ColoredLightRings!;
        Assert.AreEqual(new System.Numerics.Vector2(1000, 20000), rings.Range);
        Assert.AreEqual(6, rings.Distances.Count);
        Assert.AreEqual(0.3f, rings.Distances[1], 1e-6f);
        Assert.AreEqual(6, rings.LightColors.Count);
        Assert.AreEqual(4, rings.ShadowColors[0].Length);

        var spawn = reader.ReadMap(SpawnMapFileId)!.Environment!.Ambience!.Spawns!.Groups[0][0];
        Assert.AreEqual(5000u, spawn.SpawnRangeMax);
        Assert.AreEqual(4u, spawn.LifeSpanMax);
        Assert.AreEqual(new System.Numerics.Vector2(0, 360), spawn.RotationZRange);
        Assert.AreEqual(112u, spawn.Flags);
        Assert.AreEqual(48029u, spawn.ModelFile.FileId);
        Assert.AreEqual(20u, spawn.MaxConcurrent);
    }

    [TestMethod]
    public void ReadsDataOverridesWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var overrides = reader.ReadMap(OverrideMapFileId)!.Environment!.Overrides;
        Assert.AreEqual(8, overrides.Count);
        Assert.AreEqual(328237639845091528ul, overrides[0].Token);
        Assert.AreEqual(3338259531724744383ul, overrides[0].Guid);
        Assert.AreEqual("MTX Blazing Lands", overrides[0].Data.Name);
        Assert.AreEqual(1f, overrides[0].Data.Sky!.Dome!.Day.Brightness);
        Assert.IsNotNull(overrides[0].Data.Haze);
    }
}
