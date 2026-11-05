using System.Numerics;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2ScreenOverlayTests
{
    private const float FrameSeconds = 1f / 60f;

    private static void Run(Gw2ScreenOverlay overlay, int frames)
    {
        for (var frame = 0; frame < frames; frame++) overlay.Advance(FrameSeconds);
    }

    [TestMethod]
    public void InactiveOverlayPassesTheBaseValuesThrough()
    {
        var overlay = new Gw2ScreenOverlay();
        overlay.Advance(FrameSeconds);

        Assert.AreSame(Gw2EnvironmentGrade.Neutral, overlay.Apply(Gw2EnvironmentGrade.Neutral));
        Assert.AreEqual(new Vector4(0.1f), overlay.ApplyGlow(new Vector4(0.1f)));
        Assert.AreEqual(0.3f, overlay.ApplyDistortion(0.3f));
        Assert.IsFalse(overlay.IsActive);
    }

    [TestMethod]
    public void DownedOverlayFadesInToItsPresetAndBackOut()
    {
        var overlay = new Gw2ScreenOverlay();
        overlay.SetGrade(Gw2ScreenOverlay.Downed, Gw2EnvironmentGrade.Neutral);
        overlay.FadeIn(0.5f);

        overlay.Advance(0.1f);
        var halfway = overlay.Apply(Gw2EnvironmentGrade.Neutral);
        Assert.IsTrue(halfway.Saturation < 1f && halfway.Saturation > 0.75f);

        Run(overlay, 600);
        var grade = overlay.Apply(Gw2EnvironmentGrade.Neutral);
        Assert.AreEqual(0.75f, grade.Saturation, 1e-3f);
        Assert.AreEqual(0.9f, grade.TintAmount, 1e-3f);
        Assert.AreEqual(0xE7 / 255f, grade.TintColor.X, 1e-3f);
        Assert.AreEqual(1f, grade.TintColor.Z, 1e-3f);

        overlay.FadeOut(0.5f);
        Run(overlay, 600);
        Assert.AreSame(Gw2EnvironmentGrade.Neutral, overlay.Apply(Gw2EnvironmentGrade.Neutral));
        Assert.IsFalse(overlay.IsActive);
    }

    [TestMethod]
    public void InstantFadeInAppliesTheFullOverlay()
    {
        var overlay = new Gw2ScreenOverlay();
        overlay.SetGrade(Gw2ScreenOverlay.Downed, Gw2EnvironmentGrade.Neutral);
        overlay.FadeIn(0f);

        Assert.AreEqual(1f, overlay.Amount);
        Assert.AreEqual(Vector3.One, overlay.Apply(Gw2EnvironmentGrade.Neutral).TintTargetColor);
    }

    [TestMethod]
    public void GlowOverlayEasesFromTheBaseGlowToItsSplattedTarget()
    {
        var overlay = new Gw2ScreenOverlay();
        var baseGlow = new Vector4(0.1f, 0.2f, 0.3f, 1f);
        overlay.SetGlow(0.8f, baseGlow);
        overlay.FadeIn(0f);

        Assert.AreEqual(baseGlow, overlay.ApplyGlow(baseGlow));
        Run(overlay, 600);
        var glow = overlay.ApplyGlow(baseGlow);
        Assert.AreEqual(0.8f, glow.X, 1e-3f);
        Assert.AreEqual(0.8f, glow.W, 1e-3f);
    }

    [TestMethod]
    public void DistortionOverlayRisesFromZeroAndLerpsOverTheBase()
    {
        var overlay = new Gw2ScreenOverlay();
        overlay.SetDistortion(0.5f);
        overlay.FadeIn(0f);

        Assert.AreEqual(0f, overlay.ApplyDistortion(0.2f), 1e-6f);
        Run(overlay, 600);
        Assert.AreEqual(0.5f, overlay.ApplyDistortion(0.2f), 1e-3f);
    }

    [TestMethod]
    public void SnapsInWhileEffectsAreActiveAndFadesOutAfter()
    {
        var overlay = new Gw2ScreenOverlay();
        var grade = Gw2ScreenEffects.GradeOf(0.9f, 0xE7F4FF, 0xE7F4FF, 0f, 0.75f);
        Assert.AreEqual(Gw2ScreenOverlay.Downed, grade);

        overlay.ApplyEffects(new Gw2ScreenEffects(null, null, grade), Gw2EnvironmentGrade.Neutral, Vector4.Zero);
        Assert.AreEqual(1f, overlay.Amount);

        overlay.ApplyEffects(Gw2ScreenEffects.None, Gw2EnvironmentGrade.Neutral, Vector4.Zero);
        overlay.Advance(0.1f);
        Assert.IsTrue(overlay.Amount is > 0f and < 1f, overlay.Amount.ToString());
        for (var step = 0; step < 60; step++)
        {
            overlay.ApplyEffects(Gw2ScreenEffects.None, Gw2EnvironmentGrade.Neutral, Vector4.Zero);
            overlay.Advance(1f / 60f);
        }
        Assert.IsTrue(overlay.Amount < 0.01f, overlay.Amount.ToString());
    }
}
