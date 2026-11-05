using Gw2.Formats;
using System;
using System.IO;
using System.Numerics;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2EnvironmentDistortionTests
{
    private const uint UnderwaterMapFileId = 3399578;

    [TestMethod]
    public void UnderwaterAmountIsAQuarterOfTheUnderwaterDistortAmount()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var underwater = reader.ReadMap(UnderwaterMapFileId)!.Environment!.UnderwaterWater!;
        Assert.AreEqual(0.0027662253f, underwater.DistortAmount);
        Assert.AreEqual(0.0027662253f * 0.25f, Gw2EnvironmentDistortion.BaseAmount(underwater.DistortAmount, underwater: true));
        Assert.AreEqual(0f, Gw2EnvironmentDistortion.BaseAmount(underwater.DistortAmount, underwater: false));
    }

    [TestMethod]
    public void EyeBelowTheSurfaceInDownPositiveZIsUnderwater()
    {
        Assert.IsTrue(Gw2EnvironmentDistortion.IsUnderwater(new Vector3(0f, 0f, 10f), 0f));
        Assert.IsFalse(Gw2EnvironmentDistortion.IsUnderwater(new Vector3(0f, 0f, 0f), 0f));
        Assert.IsFalse(Gw2EnvironmentDistortion.IsUnderwater(new Vector3(0f, 0f, 10f), float.PositiveInfinity));
    }

    [TestMethod]
    public void ViewOffsetIsTwiceYawAndPitchInSixtyDegreeSteps()
    {
        Assert.AreEqual(Vector2.Zero, Gw2EnvironmentDistortion.ViewOffset(Vector3.UnitX, underwater: true));
        AssertNear(new Vector2(-3f, 0f), Gw2EnvironmentDistortion.ViewOffset(Vector3.UnitY, underwater: true));
        AssertNear(new Vector2(0f, 3f), Gw2EnvironmentDistortion.ViewOffset(Vector3.UnitZ, underwater: true));
        Assert.AreEqual(Vector2.Zero, Gw2EnvironmentDistortion.ViewOffset(Vector3.UnitY, underwater: false));
    }

    [TestMethod]
    public void TimeAccumulatesAndRestartsPastAnHour()
    {
        var distortion = new Gw2EnvironmentDistortion();
        distortion.Advance(3599.5f);
        Assert.AreEqual(new Vector4(0.5f, 3599.5f, 1f, 2f), distortion.Constant(0.5f, new Vector2(1f, 2f)));
        distortion.Advance(1f);
        Assert.AreEqual(0f, distortion.Seconds);
    }

    private static void AssertNear(Vector2 expected, Vector2 actual)
    {
        Assert.AreEqual(expected.X, actual.X, 1e-5f);
        Assert.AreEqual(expected.Y, actual.Y, 1e-5f);
    }
}
