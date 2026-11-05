using System;
using Gw2.Client.Scene;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Scene.Tests;

[TestClass]
public sealed class FrameTimingsTests
{
    [TestMethod]
    public void ReportsAverageOnePercentLowWorstFrameAndCpuShare()
    {
        var timings = new Gw2FrameTimings();
        for (var frame = 0; frame < 198; frame++) timings.Record(0.01, 0.004);
        timings.Record(0.05, 0.004);
        timings.Record(0.03, 0.004);

        var report = timings.Report();
        Assert.AreEqual(200, report.FrameCount);
        Assert.AreEqual(200 / (198 * 0.01 + 0.08), report.AverageFramesPerSecond, 1e-9);
        Assert.AreEqual(2 / 0.08, report.OnePercentLowFramesPerSecond, 1e-9);
        Assert.AreEqual(50.0, report.WorstFrameMilliseconds, 1e-9);
        Assert.AreEqual(4.0, report.AverageCpuMilliseconds, 1e-9);
        Assert.AreEqual((198 * 0.01 + 0.08) / 200 * 1000 - 4.0, report.AveragePresentWaitMilliseconds, 1e-9);
    }

    [TestMethod]
    public void AveragesCpuSectionsOverTheFramesThatRecordedThem()
    {
        var timings = new Gw2FrameTimings();
        timings.Record(0.01, 0.004);
        timings.RecordCpuSections([0.001, 0.002]);
        timings.RecordCpuSections([0.003, 0.004]);

        var sections = timings.Report().AverageCpuSectionMilliseconds;
        Assert.AreEqual(2.0, sections[0], 1e-9);
        Assert.AreEqual(3.0, sections[1], 1e-9);
    }

    [TestMethod]
    public void FrameLimitAcceptsOnlyTheLiveCaps()
    {
        Assert.AreEqual(240, Gw2FrameLimit.Parse("unlimited"));
        Assert.AreEqual(144, Gw2FrameLimit.Parse("144"));
        Assert.AreEqual(165, Gw2FrameLimit.Parse("165"));
        Assert.AreEqual(200, Gw2FrameLimit.Parse("200"));
        Assert.AreEqual(360, Gw2FrameLimit.Parse("360"));
        Assert.AreEqual(Gw2FrameLimit.Uncapped, Gw2FrameLimit.Parse("uncapped"));
        Assert.AreEqual(TimeSpan.Zero, Gw2FrameLimit.PeriodOf(Gw2FrameLimit.Uncapped));
        Assert.ThrowsExactly<ArgumentException>(() => Gw2FrameLimit.Parse("90"));
    }
}
