using Gw2.Client.Settings;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using Gw2.Client.Graphics;
using Gw2.Client.Scene;

namespace Gw2.Client.Debug;

public sealed class BenchmarkRecorder
{
    private const double SlowFrameMilliseconds = 33.0;
    private const double MillisecondsPerSecond = 1000.0;
    private const double Megabyte = 1024d * 1024d;
    private const float FlightSampleIntervalSeconds = 1f;

    private readonly Gw2BenchmarkOptions _options;
    private readonly Gw2FrameTimings _timings = new();
    private long _drawCalls;
    private float _flightSeconds;
    private float _nextFlightSampleSeconds;
    private TimeSpan _gcPauseAtFrameStart;
    private int _gen0AtFrameStart;
    private int _gen2AtFrameStart;
    private int _materialReadsAtFrameStart;

    public BenchmarkRecorder(Gw2BenchmarkOptions options) => _options = options;

    public bool IsRecording { get; private set; }

    public bool IsComplete => _options.RecordSeconds is { } recordSeconds && _timings.RecordedSeconds >= recordSeconds;

    public void MarkFrameWorkStart(int materialReads)
    {
        _gcPauseAtFrameStart = GC.GetTotalPauseDuration();
        _gen0AtFrameStart = GC.CollectionCount(0);
        _gen2AtFrameStart = GC.CollectionCount(2);
        _materialReadsAtFrameStart = materialReads;
    }

    public void StartWhenSettled(bool characterReady, float secondsSinceSceneSettled)
    {
        if (IsRecording || (_options.RecordSeconds is null && !_options.LogsSlowFrames) || !characterReady || secondsSinceSceneSettled < _options.WarmupSeconds) return;
        IsRecording = true;
    }

    public void AdvanceFlight(float elapsedSeconds, Action<Vector3, Vector3> lookFrom, Action writeSample)
    {
        if (_options.Flight is not { } flight || !IsRecording) return;
        lookFrom(flight.Eye(_flightSeconds), flight.Front(_flightSeconds));
        if (_flightSeconds >= _nextFlightSampleSeconds)
        {
            writeSample();
            _nextFlightSampleSeconds += FlightSampleIntervalSeconds;
        }

        _flightSeconds += elapsedSeconds;
    }

    public void WriteFlightSample(Vector3 eye, long gpuLocalBytes, string streaming, int cachedContentFiles)
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"FLIGHT {_flightSeconds:F0}s | eye {eye.X:F0},{eye.Y:F0},{eye.Z:F0} | private {process.PrivateMemorySize64 / Megabyte:F0} MB | managed {GC.GetTotalMemory(false) / Megabyte:F0} MB | gpu {gpuLocalBytes / Megabyte:F0} MB{streaming} | content {cachedContentFiles}"));
    }

    public void RecordDrawCalls(long drawCalls)
    {
        if (IsRecording) _drawCalls += drawCalls;
    }

    public void RecordCpuSections(GrFrameProfiler cpuFrame)
    {
        if (IsRecording) _timings.RecordCpuSections(cpuFrame.CompletedCpuSectionSeconds);
    }

    public void RecordGpuFrame(GrFrameProfiler completed)
    {
        if (!IsRecording) return;
        _timings.RecordGpu(completed.CompletedGpuFrameSeconds, completed.CompletedGpuSectionSeconds);
        var frameMilliseconds = completed.CompletedGpuFrameSeconds * MillisecondsPerSecond;
        var sincePreviousMilliseconds = (completed.CompletedGpuSecondsSincePreviousFrame ?? 0.0) * MillisecondsPerSecond;
        if (frameMilliseconds <= SlowFrameMilliseconds && sincePreviousMilliseconds <= SlowFrameMilliseconds) return;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"SLOW GPU {_flightSeconds:F1}s | gpu {frameMilliseconds:F1} | since previous frame {sincePreviousMilliseconds:F1} | {SectionsOf(completed.CompletedGpuSectionSeconds)}"));
    }

    public void RecordFrame(double frameSeconds, double cpuSeconds, GrFrameProfiler? frameTimer, int materialReads, Func<string> describeStreaming)
    {
        if (!IsRecording) return;
        _timings.Record(frameSeconds, cpuSeconds);
        if (frameSeconds * MillisecondsPerSecond <= SlowFrameMilliseconds) return;
        var sections = frameTimer is { HasCompletedCpuFrame: true } cpuFrame ? SectionsOf(cpuFrame.CompletedCpuSectionSeconds) : "";
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"SLOW {_flightSeconds:F1}s | frame {frameSeconds * MillisecondsPerSecond:F1} | cpu {cpuSeconds * MillisecondsPerSecond:F1}{describeStreaming()} | material reads {materialReads - _materialReadsAtFrameStart} | gc {(GC.GetTotalPauseDuration() - _gcPauseAtFrameStart).TotalMilliseconds:F1} (gen0 {GC.CollectionCount(0) - _gen0AtFrameStart}, gen2 {GC.CollectionCount(2) - _gen2AtFrameStart}) | {sections}"));
    }

    public void WriteReport()
    {
        var report = _timings.Report();
        var line = string.Create(CultureInfo.InvariantCulture,
            $"| {DateTime.Now:yyyy-MM-dd} | Gw2.Client (DX12) | {_options.Name ?? "-"} | {_options.BackBufferWidth}x{_options.BackBufferHeight} | {_options.FrameLimit} | {report.AverageFramesPerSecond:F1} | {report.OnePercentLowFramesPerSecond:F1} | {report.WorstFrameMilliseconds:F1} | {report.AverageCpuMilliseconds:F2} | {report.AveragePresentWaitMilliseconds:F2} | {(report.AverageGpuMilliseconds is { } gpuMilliseconds ? gpuMilliseconds.ToString("F2", CultureInfo.InvariantCulture) : "")} | {report.FrameCount} | {(report.FrameCount == 0 ? 0 : _drawCalls / report.FrameCount)} |");
        Console.WriteLine(line);
        if (_options.ReportPath is { } reportPath) File.AppendAllText(reportPath, line + Environment.NewLine);
        if (report.AverageGpuMilliseconds is not null)
            Console.WriteLine("GPU " + string.Join(" | ", Enum.GetValues<GrFrameSection>().Skip(1).Select(section => string.Create(CultureInfo.InvariantCulture, $"{section} {report.AverageGpuSectionMilliseconds[(int)section]:F2}"))));
        if (report.AverageCpuSectionMilliseconds.Count != 0)
            Console.WriteLine("CPU " + string.Join(" | ", Enum.GetValues<GrFrameSection>().Select(section => string.Create(CultureInfo.InvariantCulture, $"{(section == GrFrameSection.FrameStart ? "Update" : section.ToString())} {report.AverageCpuSectionMilliseconds[(int)section]:F2}"))));
    }

    private static string SectionsOf(ReadOnlySpan<double> sectionSeconds)
    {
        var sections = new List<string>();
        foreach (var section in Enum.GetValues<GrFrameSection>())
            sections.Add(string.Create(CultureInfo.InvariantCulture, $"{section}={sectionSeconds[(int)section] * MillisecondsPerSecond:F1}"));
        return string.Join(" ", sections);
    }
}
