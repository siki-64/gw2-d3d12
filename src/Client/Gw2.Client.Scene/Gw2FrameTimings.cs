using System;
using System.Collections.Generic;
using System.Linq;

namespace Gw2.Client.Scene;

public readonly record struct Gw2FrameTimingReport(
    int FrameCount,
    double AverageFramesPerSecond,
    double OnePercentLowFramesPerSecond,
    double WorstFrameMilliseconds,
    double AverageCpuMilliseconds,
    double AveragePresentWaitMilliseconds,
    double? AverageGpuMilliseconds,
    IReadOnlyList<double> AverageGpuSectionMilliseconds,
    IReadOnlyList<double> AverageCpuSectionMilliseconds);

public sealed class Gw2FrameTimings
{
    private const double SlowestFrameShare = 0.01;

    private readonly List<double> _frameSeconds = [];
    private readonly List<double> _cpuSeconds = [];
    private double _gpuSeconds;
    private double[] _gpuSectionSeconds = [];
    private int _gpuFrameCount;
    private double[] _cpuSectionSeconds = [];
    private int _cpuSectionFrameCount;

    public int FrameCount => _frameSeconds.Count;

    public double RecordedSeconds { get; private set; }

    public void Record(double frameSeconds, double cpuSeconds)
    {
        _frameSeconds.Add(frameSeconds);
        _cpuSeconds.Add(Math.Min(cpuSeconds, frameSeconds));
        RecordedSeconds += frameSeconds;
    }

    public void RecordGpu(double frameSeconds, ReadOnlySpan<double> sectionSeconds)
    {
        if (_gpuSectionSeconds.Length != sectionSeconds.Length) _gpuSectionSeconds = new double[sectionSeconds.Length];
        for (var section = 0; section < sectionSeconds.Length; section++) _gpuSectionSeconds[section] += sectionSeconds[section];
        _gpuSeconds += frameSeconds;
        _gpuFrameCount++;
    }

    public void RecordCpuSections(ReadOnlySpan<double> sectionSeconds)
    {
        if (_cpuSectionSeconds.Length != sectionSeconds.Length) _cpuSectionSeconds = new double[sectionSeconds.Length];
        for (var section = 0; section < sectionSeconds.Length; section++) _cpuSectionSeconds[section] += sectionSeconds[section];
        _cpuSectionFrameCount++;
    }

    public Gw2FrameTimingReport Report()
    {
        if (_frameSeconds.Count == 0) return new(0, 0, 0, 0, 0, 0, null, [], []);
        var slowestFirst = _frameSeconds.OrderDescending().ToArray();
        var slowestCount = Math.Max(1, (int)Math.Ceiling(slowestFirst.Length * SlowestFrameShare));
        var slowestSeconds = slowestFirst.Take(slowestCount).Sum();
        var averageCpuSeconds = _cpuSeconds.Average();
        var averageFrameSeconds = RecordedSeconds / _frameSeconds.Count;
        return new(
            _frameSeconds.Count,
            _frameSeconds.Count / RecordedSeconds,
            slowestCount / slowestSeconds,
            slowestFirst[0] * 1000.0,
            averageCpuSeconds * 1000.0,
            (averageFrameSeconds - averageCpuSeconds) * 1000.0,
            _gpuFrameCount == 0 ? null : _gpuSeconds / _gpuFrameCount * 1000.0,
            _gpuSectionSeconds.Select(seconds => seconds / Math.Max(1, _gpuFrameCount) * 1000.0).ToArray(),
            _cpuSectionSeconds.Select(seconds => seconds / Math.Max(1, _cpuSectionFrameCount) * 1000.0).ToArray());
    }
}

public static class Gw2FrameLimit
{
    public const int Unlimited = 240;
    public const int Uncapped = 0;

    public static IReadOnlyList<int> Caps { get; } = [30, 60, 120, 144, 165, 200, 240, 360];

    public static TimeSpan PeriodOf(int frameLimit) => frameLimit == Uncapped ? TimeSpan.Zero : TimeSpan.FromTicks(TimeSpan.TicksPerSecond / frameLimit);

    public static int Parse(string value)
    {
        if (string.Equals(value, "unlimited", StringComparison.OrdinalIgnoreCase)) return Unlimited;
        if (string.Equals(value, "uncapped", StringComparison.OrdinalIgnoreCase)) return Uncapped;
        var cap = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        if (!Caps.Contains(cap)) throw new ArgumentException($"Frame limit {value} is not one of {string.Join(", ", Caps)}, unlimited, uncapped.");
        return cap;
    }
}
