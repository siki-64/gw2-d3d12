using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Graphics.Tests;

[TestClass]
public sealed class GrPostProcessChainTests
{
    private static GrPostProcessChain WithSunAt(Vector2? screenPosition) => new(null, 0f, null, null, 0f, screenPosition);

    [TestMethod]
    public void LightRaysRunWhileTheSunIsWithinTheScreenMargin()
    {
        Assert.IsTrue(WithSunAt(new Vector2(0.5f, 0.5f)).RunsLightRays);
        Assert.IsTrue(WithSunAt(new Vector2(-0.135f, 1.135f)).RunsLightRays);
        Assert.IsTrue(WithSunAt(new Vector2(1.1f, -0.1f)).RunsAnyPass);
    }

    [TestMethod]
    public void LightRaysStopWithoutASunOrBeyondTheMargin()
    {
        Assert.IsFalse(WithSunAt(null).RunsLightRays);
        Assert.IsFalse(WithSunAt(new Vector2(-1f, -1f)).RunsLightRays);
        Assert.IsFalse(WithSunAt(new Vector2(0.5f, 1.2f)).RunsLightRays);
        Assert.IsFalse(WithSunAt(new Vector2(-1f, -1f)).RunsAnyPass);
    }
}
