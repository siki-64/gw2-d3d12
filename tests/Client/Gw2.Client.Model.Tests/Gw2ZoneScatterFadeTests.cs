using System.Numerics;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2ZoneScatterFadeTests
{
    private const float FadeDistance = 2000f;

    [TestMethod]
    public void FadesInOverOneSecondInsideTheBandAndOutBeyondTheDistance()
    {
        var fade = new Gw2ZoneScatterFade(Vector2.Zero, FadeDistance, cutsHard: false);
        var near = new Vector3(1000f, 0f, -500f);
        fade.Advance(near, 0f);
        Assert.IsFalse(fade.IsVisible);
        fade.Advance(near, 0.25f);
        Assert.AreEqual(Gw2ZoneScatterFade.FadeHidden, fade.Value);
        fade.Advance(near, 0.25f);
        Assert.AreEqual(0.25f, fade.Value, 1e-6f);
        Assert.IsTrue(fade.IsFading);
        fade.Advance(near, 1f);
        Assert.AreEqual(Gw2ZoneScatterFade.FadeOpaque, fade.Value);

        var insideBand = new Vector3(FadeDistance - 100f, 0f, 0f);
        fade.Advance(insideBand, 1f);
        Assert.AreEqual(Gw2ZoneScatterFade.FadeOpaque, fade.Value);

        var beyond = new Vector3(FadeDistance + 1f, 0f, 0f);
        fade.Advance(beyond, 0.5f);
        fade.Advance(beyond, 0.5f);
        Assert.AreEqual(0.5f, fade.Value, 1e-6f);
        fade.Advance(beyond, 1f);
        Assert.IsFalse(fade.IsVisible);
    }

    [TestMethod]
    public void HardCutIgnoresTheBandAndHeight()
    {
        var fade = new Gw2ZoneScatterFade(Vector2.Zero, FadeDistance, cutsHard: true);
        fade.Advance(new Vector3(FadeDistance - 1f, 0f, 100000f), 0f);
        Assert.AreEqual(Gw2ZoneScatterFade.FadeOpaque, fade.Value);
        fade.Advance(new Vector3(FadeDistance + 1f, 0f, 0f), 0f);
        Assert.IsFalse(fade.IsVisible);
    }

    [TestMethod]
    public void BlockCenterIsHalfABlockPastTheCorner()
    {
        var center = Gw2ZoneScatterFade.CenterOf(new Gw2ZoneScatterBlock(0u, 2, -1), new Vector2(100f, 200f));
        Assert.AreEqual(new Vector2(100f + 2 * 1536f + 768f, 200f - 1536f + 768f), center);
    }
}
