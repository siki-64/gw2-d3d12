using Gw2.Client.Scene;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Scene.Tests;

[TestClass]
public sealed class EyeColorConstantsTests
{
    [TestMethod]
    public void LiveCapturedEyeShiftPacksIntoMatrixColumns()
    {
        var (red, green, blue) = new Gw2ColorShift(0f, 1f, 0.6109f, 0.25f, 0.71875f).ToColumnConstants();

        Assert.AreEqual(0.37611f, red.X, 1e-4f);
        Assert.AreEqual(0.08853f, red.Y, 1e-4f);
        Assert.AreEqual(0.25411f, red.Z, 1e-4f);
        Assert.AreEqual(0.25411f, green.X, 1e-4f);
        Assert.AreEqual(0.37611f, green.Y, 1e-4f);
        Assert.AreEqual(0.08853f, green.Z, 1e-4f);
        Assert.AreEqual(0.08853f, blue.X, 1e-4f);
        Assert.AreEqual(0.25411f, blue.Y, 1e-4f);
        Assert.AreEqual(0.37611f, blue.Z, 1e-4f);
    }
}
