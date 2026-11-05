using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2RenderScaleTests
{
    private const int MaximumTextureSize = 16384;

    [TestMethod]
    public void QualityPicksTheScale()
    {
        Assert.AreEqual(0.7f, Gw2RenderScale.OfQuality(0));
        Assert.AreEqual(1f, Gw2RenderScale.OfQuality(1));
        Assert.AreEqual(1.5f, Gw2RenderScale.OfQuality(2));
    }

    [TestMethod]
    public void SupersampleScalesBothSidesAndFloors()
    {
        Assert.AreEqual((2400, 1350), Gw2RenderScale.TargetSize(1.5f, 1600, 900, MaximumTextureSize));
        Assert.AreEqual((1600, 900), Gw2RenderScale.TargetSize(1f, 1600, 900, MaximumTextureSize));
    }

    [TestMethod]
    public void SupersampleStopsAtTheMaximumWidth()
    {
        Assert.AreEqual(2880f / 2560f, Gw2RenderScale.Factor(1.5f, 2560, MaximumTextureSize), 1e-6f);
        Assert.AreEqual(1f, Gw2RenderScale.Factor(1.5f, 3840, MaximumTextureSize));
    }

    [TestMethod]
    public void SubsampleKeepsAtLeastTheMinimumWidth()
    {
        Assert.AreEqual(0.7f, Gw2RenderScale.Factor(0.7f, 1920, MaximumTextureSize));
        Assert.AreEqual(800f / 1000f, Gw2RenderScale.Factor(0.7f, 1000, MaximumTextureSize), 1e-6f);
        Assert.AreEqual(1f, Gw2RenderScale.Factor(0.7f, 640, MaximumTextureSize));
    }
}
