using Gw2.Client.Model;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2TelegraphTimelineTests
{
    [TestMethod]
    public void FadesInDuringTheStartClip()
    {
        var timeline = new Gw2TelegraphTimeline(0.4, 1.0);
        timeline.Show(10.0);

        Assert.AreEqual(Gw2TelegraphClip.Start, timeline.ClipAt(10.2));
        Assert.AreEqual(0.5f, timeline.DepthFadeAt(10.2), 1e-5f);
        Assert.AreEqual(Gw2TelegraphClip.Loop, timeline.ClipAt(11.0));
        Assert.AreEqual(1f, timeline.DepthFadeAt(11.0));
    }

    [TestMethod]
    public void PlaysTheEndClipThenHides()
    {
        var timeline = new Gw2TelegraphTimeline(0.4, 1.0);
        timeline.Show(0.0);
        timeline.End(2.0);

        Assert.AreEqual(Gw2TelegraphClip.End, timeline.ClipAt(2.5));
        Assert.AreEqual(Gw2TelegraphClip.Hidden, timeline.ClipAt(3.0));
        Assert.AreEqual(0f, timeline.DepthFadeAt(3.0));
    }

    [TestMethod]
    public void EndingDuringTheStartClipWaitsForItToFinish()
    {
        var timeline = new Gw2TelegraphTimeline(0.4, 1.0);
        timeline.Show(0.0);
        timeline.End(0.1);

        Assert.AreEqual(Gw2TelegraphClip.Start, timeline.ClipAt(0.3));
        Assert.AreEqual(Gw2TelegraphClip.End, timeline.ClipAt(0.5));
        Assert.AreEqual(Gw2TelegraphClip.Hidden, timeline.ClipAt(1.45));
    }

    [TestMethod]
    public void StartsHidden()
    {
        Assert.AreEqual(Gw2TelegraphClip.Hidden, new Gw2TelegraphTimeline(0.4, 1.0).ClipAt(0.0));
    }
}
