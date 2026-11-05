using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Graphics.Tests;

[TestClass]
public sealed class GrGammaTests
{
    [TestMethod]
    public void GammaClampsToTheOptionsSliderRange()
    {
        Assert.AreEqual(0.5f, GrGamma.Clamp(0.1f));
        Assert.AreEqual(3.5f, GrGamma.Clamp(9f));
        Assert.AreEqual(1.2f, GrGamma.Clamp(1.2f));
    }

    [TestMethod]
    public void PresentScaleGoesThroughTheSixteenBitMetric()
    {
        Assert.AreEqual(65535f / 65536f, GrGamma.PresentScale(1f));
        Assert.AreEqual(MathF.Round(2f * 65535f) / 65536f, GrGamma.PresentScale(2f));
        Assert.AreEqual(MathF.Round(3.5f * 65535f) / 65536f, GrGamma.PresentScale(4f));
    }
}
