using System;
using Gw2.Client.Scene;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Scene.Tests;

[TestClass]
public sealed class PerfGraphsTests
{
    [TestMethod]
    public void SamplesWrapAtTheExeRingLengthAndSkipWhilePaused()
    {
        var graphs = new Gw2PerfGraphs();
        var graph = graphs.Register("Frame Time (ms)", 0f, 100f, new Gw2PerfGraphSeries("Time", 0xFF4040FF), new Gw2PerfGraphSeries("Smooth time", 0xFFFFFF00));
        for (var sample = 0; sample < Gw2PerfGraph.SampleCount + 2; sample++) graph.AddSample([sample, -sample]);

        Assert.AreEqual(2, graph.WriteIndex);
        Assert.AreEqual(251f, graph.Series[0].Values[1]);
        Assert.AreEqual(-2f, graph.Series[1].Values[2]);

        graphs.IsPaused = true;
        graph.AddSample([1000f, 1000f]);
        Assert.AreEqual(2, graph.WriteIndex);
        Assert.AreEqual(2f, graph.Series[0].Values[2]);
    }

    [TestMethod]
    public void FrameTimeSmoothingFollowsGrPresent()
    {
        var smoothing = new Gw2FrameTimeSmoothing();
        Assert.AreEqual(0.002f, smoothing.Smooth(0.016f), 1e-7f);
        Assert.AreEqual(0.0055f, smoothing.Smooth(0.016f), 1e-7f);
        Assert.AreEqual(0.0245f + 0.5f, smoothing.Smooth(0.5f), 1e-6f);
        Assert.AreEqual(0.00375f * 0.875f + 0.016f * 0.125f, smoothing.Smooth(0.016f), 1e-6f);
    }

    [TestMethod]
    public void FrameTimeSmoothingSettlesOnASteadyFrameTime()
    {
        var smoothing = new Gw2FrameTimeSmoothing();
        var smooth = 0f;
        for (var frame = 0; frame < 400; frame++) smooth = smoothing.Smooth(0.010f);
        Assert.AreEqual(0.010f, smooth, 1e-5f);
    }
}
