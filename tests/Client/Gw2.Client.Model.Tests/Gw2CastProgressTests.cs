using System.Linq;
using Gw2.Client.Model;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2CastProgressTests
{
    private const uint SkillId = 9137;
    private const double FullyFadedOut = Gw2CastProgress.FadeOutDelaySeconds + Gw2CastProgress.FadeOutSeconds;

    private static Gw2SkillCastTiming Warmup(double seconds) => new(Gw2SkillProgressWidget.Warmup, seconds, seconds, []);

    [TestMethod]
    public void WarmupFillFollowsElapsedCastTime()
    {
        var cast = new Gw2CastProgress();
        cast.Begin(2, SkillId, Warmup(2.0), 10.0);

        var segment = cast.SegmentsAt(11.0).Single();
        Assert.AreEqual(Gw2CastSegmentMode.Casting, segment.Mode);
        Assert.AreEqual(Gw2CastProgress.WarmupStyle, segment.Style);
        Assert.AreEqual(0.5f, segment.Fill, 1e-5f);
        Assert.AreEqual(2, cast.CastingSlot);
    }

    [TestMethod]
    public void FadesInOverAQuarterSecond()
    {
        var cast = new Gw2CastProgress();
        cast.Begin(0, SkillId, Warmup(2.0), 0.0);

        Assert.AreEqual(0f, cast.OpacityAt(0.0));
        Assert.AreEqual(0.5f, cast.OpacityAt(Gw2CastProgress.FadeInSeconds / 2.0), 1e-5f);
        Assert.AreEqual(1f, cast.OpacityAt(1.0));
    }

    [TestMethod]
    public void WarmupCompletesAtTheExecuteAndFadesOutAfterTheFlash()
    {
        var cast = new Gw2CastProgress();
        cast.Begin(0, SkillId, Warmup(1.0), 0.0);
        cast.Update(0.9);
        Assert.AreEqual(Gw2CastSegmentMode.Casting, cast.SegmentsAt(0.9).Single().Mode);

        cast.Update(1.2);
        Assert.IsNull(cast.CastingSlot);
        Assert.AreEqual(Gw2CastSegmentMode.Completed, cast.SegmentsAt(1.2).Single().Mode);
        Assert.AreEqual(1f, cast.OpacityAt(1.0 + Gw2CastProgress.FadeOutDelaySeconds));
        Assert.AreEqual(0.5f, cast.OpacityAt(1.0 + Gw2CastProgress.FadeOutDelaySeconds + Gw2CastProgress.FadeOutSeconds / 2.0), 1e-5f);
        Assert.IsFalse(cast.IsVisibleAt(1.0 + FullyFadedOut));
    }

    [TestMethod]
    public void AStopDuringTheWarmupInterruptsIt()
    {
        var cast = new Gw2CastProgress();
        cast.Begin(1, SkillId, Warmup(4.0), 0.0);
        cast.Stop(1.0);

        var segment = cast.SegmentsAt(1.2).Single();
        Assert.AreEqual(Gw2CastSegmentMode.Interrupted, segment.Mode);
        Assert.AreEqual(0.25f, segment.Fill, 1e-5f);
        cast.Update(10.0);
        Assert.AreEqual(Gw2CastSegmentMode.Interrupted, cast.SegmentsAt(1.2).Single().Mode);
        Assert.IsFalse(cast.IsVisibleAt(1.0 + FullyFadedOut));
    }

    [TestMethod]
    public void ChannelRunsTheWarmupThenTheChannelAndWaitsForTheStop()
    {
        var cast = new Gw2CastProgress();
        cast.Begin(0, SkillId, new Gw2SkillCastTiming(Gw2SkillProgressWidget.Channel, 0.5, 2.0, []), 0.0);

        var segments = cast.SegmentsAt(0.0);
        Assert.AreEqual(2, segments.Count);
        Assert.AreEqual(0.25f, segments[0].LayoutEnd, 1e-5f);
        Assert.AreEqual(Gw2CastProgress.ChannelStyle, segments[1].Style);
        Assert.AreEqual(Gw2CastSegmentMode.Waiting, segments[1].Mode);

        cast.Update(1.25);
        segments = cast.SegmentsAt(1.25);
        Assert.AreEqual(Gw2CastSegmentMode.Completed, segments[0].Mode);
        Assert.AreEqual(Gw2CastSegmentMode.Casting, segments[1].Mode);
        Assert.AreEqual(0.5f, segments[1].Fill, 1e-5f);

        cast.Update(3.0);
        Assert.AreEqual(Gw2CastSegmentMode.Completed, cast.SegmentsAt(3.0)[1].Mode);
        Assert.IsTrue(cast.IsVisibleAt(10.0));
        cast.Stop(10.0);
        Assert.IsFalse(cast.IsVisibleAt(10.0 + FullyFadedOut));
    }

    [TestMethod]
    public void ChargeLevelsAdvanceTheStages()
    {
        var cast = new Gw2CastProgress();
        cast.Begin(0, SkillId, new Gw2SkillCastTiming(Gw2SkillProgressWidget.Charge, 1.0, 1.0, [new(0.5, 1), new(0.5, 2), new(1.0, 3)]), 0.0);

        Assert.AreEqual(Gw2CastSegmentMode.Casting, cast.SegmentsAt(0.0)[0].Mode);
        cast.ReachChargeLevel(2, 1.0);

        var segments = cast.SegmentsAt(1.0);
        Assert.AreEqual(Gw2CastSegmentMode.Completed, segments[0].Mode);
        Assert.AreEqual(Gw2CastSegmentMode.Completed, segments[1].Mode);
        Assert.AreEqual(Gw2CastSegmentMode.Casting, segments[2].Mode);
        Assert.AreEqual(0.5f, segments[2].LayoutStart, 1e-5f);
        Assert.AreEqual(3, segments[2].Style);
    }

    [TestMethod]
    public void StartsHidden()
    {
        Assert.IsFalse(new Gw2CastProgress().IsVisibleAt(0.0));
    }
}
