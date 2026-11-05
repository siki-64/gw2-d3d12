using System.Numerics;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2MapLightRangeTests
{
    [TestMethod]
    public void HalfExtentFollowsTheShaderQualityTable()
    {
        Assert.AreEqual(3072f, Gw2MapLightRange.HalfExtent(Gw2ShaderQuality.Low));
        Assert.AreEqual(9216f, Gw2MapLightRange.HalfExtent(Gw2ShaderQuality.Medium));
        Assert.AreEqual(15360f, Gw2MapLightRange.HalfExtent(Gw2ShaderQuality.High));
        Assert.AreEqual(15360f, Gw2MapLightRange.HalfExtent(Gw2ShaderQuality.Ultra));
    }

    [TestMethod]
    public void KeepsLightsInsideTheSquareAroundTheEyeAtAnyHeight()
    {
        var eye = new Vector3(1000f, 2000f, -50f);
        Assert.IsTrue(Gw2MapLightRange.Contains(eye, new Vector3(1000f + 3072f, 2000f - 3072f, -9000f), 3072f));
        Assert.IsFalse(Gw2MapLightRange.Contains(eye, new Vector3(1000f + 3073f, 2000f, -50f), 3072f));
    }
}
