using System.Numerics;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Scene.Tests;

[TestClass]
public sealed class Gw2EnvironmentWindTests
{
    [TestMethod]
    public void MapWindBytesScaleLikeTheEnvironmentImport()
    {
        var settings = Gw2WindSettings.Of(new Gw2MapEnvWind(64, 32, 128, 2, 4, 8) { GustSpeed = 128 });

        Assert.AreEqual(90f, settings.AzimuthDegrees, 1e-4f);
        Assert.AreEqual(45f, settings.ElevationDegrees, 1e-4f);
        Assert.AreEqual(0.5f, settings.Turbulence, 1e-6f);
        Assert.AreEqual(15.625f, settings.Speed, 1e-4f);
        Assert.AreEqual(31.25f, settings.Gust, 1e-4f);
        Assert.AreEqual(0.3125f, settings.GustFrequency, 1e-6f);
        Assert.AreEqual(1f, settings.GustSpeed, 1e-6f);
    }

    [TestMethod]
    public void CalmWindBlowsAlongTheAzimuthAtItsSpeedPlusTheGustFloor()
    {
        var settings = new Gw2WindSettings(90f, 0f, 100f, 0f, 0f, 0f, 10f);
        var wind = new Gw2EnvironmentWind().Advance(settings, 0f, 1f, 0f);

        Assert.AreEqual(0f, wind.X, 1e-4f);
        Assert.AreEqual(11f, wind.Y, 1e-4f);
        Assert.AreEqual(0f, wind.Z, 1e-4f);
    }

    [TestMethod]
    public void TurbulenceTurnsTheWindTowardsTheWobbleAcrossIt()
    {
        var settings = new Gw2WindSettings(0f, 0f, 0f, 0f, 0f, 1f, 1f);
        var environmentWind = new Gw2EnvironmentWind();
        var wind = environmentWind.Advance(settings, 0f, 1f, 0f);
        var wobble = Gw2EnvironmentWind.WobbleAt(0f);
        var across = Vector3.Normalize(wobble - Vector3.UnitX * wobble.X);

        Assert.AreEqual(1f, wind.Length(), 1e-4f);
        Assert.AreEqual(0f, Vector3.Distance(across, wind), 1e-4f);
    }
}
