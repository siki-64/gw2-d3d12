using System.Numerics;
using Gw2.Client.Model;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2CompassViewTests
{
    [TestMethod]
    public void ZoomInterpolatesTheScaleTable()
    {
        var target = (512, 512);
        Assert.AreEqual(256f, Gw2CompassView.HalfWidthInContinentPixels(0f, target));
        Assert.AreEqual(512f, Gw2CompassView.HalfWidthInContinentPixels(0.5f, target));
        Assert.AreEqual(5120f, Gw2CompassView.HalfWidthInContinentPixels(3f, target));
        var visible = Gw2CompassView.VisibleRect(new Vector2(1000, 2000), 1f, (360, 170));
        Assert.AreEqual(1f, Gw2CompassView.ContinentPixelsPerViewportPixelAt(Gw2CompassView.MapZoom(0f, false), target));
        Assert.AreEqual(4.125f, Gw2CompassView.ContinentPixelsPerViewportPixelAt(Gw2CompassView.MapZoom(3f, false), target));
        Assert.AreEqual(new Vector2(1000 - 540, 2000 - 540 * 170f / 360f), visible.Minimum);
    }
}
