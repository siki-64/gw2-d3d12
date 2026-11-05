using System;
using System.Diagnostics;
using Crystal.Graphics;

namespace Gw2.Client.Graphics;

public enum GrFrameSection
{
    FrameStart,
    SceneUpdate,
    NormalSpec,
    Shadows,
    Lighting,
    Sky,
    Reflection,
    Opaque,
    SceneRecording,
    Water,
    Translucent,
    FrameEnd,
}

public static class GrFrameSections
{
    public static readonly int Count = Enum.GetValues<GrFrameSection>().Length;

    public static GrFrameProfiler CreateProfiler(GraphicsDevice graphicsDevice) => new(graphicsDevice.Profiler);
}

public sealed class GrFrameProfiler
{
    private static readonly string[] ScopeNames = Enum.GetNames<GrFrameSection>();
    private static readonly string[] RecordedScopeNames = Array.ConvertAll(ScopeNames, name => "Recorded" + name);

    private readonly GraphicsProfiler _profiler;
    private readonly double[] _gpuSectionSeconds = new double[GrFrameSections.Count];
    private readonly double[] _cpuSectionSeconds = new double[GrFrameSections.Count];
    private readonly double[] _gpuSecondsToFrameEnd = new double[GrFrameSections.Count];
    private readonly bool[] _gpuSectionMarked = new bool[GrFrameSections.Count];
    private long _previousMarkTimestamp;
    private int _openScopes;
    private ulong _collectedFrameNumber;

    public GrFrameProfiler(GraphicsProfiler profiler)
    {
        _profiler = profiler;
        _profiler.IsEnabled = true;
    }

    public bool HasCompletedGpuFrame { get; private set; }

    public ReadOnlySpan<double> CompletedGpuSectionSeconds => _gpuSectionSeconds;

    public double CompletedGpuFrameSeconds { get; private set; }

    public double? CompletedGpuSecondsSincePreviousFrame { get; private set; }

    public bool HasCompletedCpuFrame { get; private set; }

    public ReadOnlySpan<double> CompletedCpuSectionSeconds => _cpuSectionSeconds;

    public void BeginFrame(long cpuWorkStartTimestamp)
    {
        HasCompletedGpuFrame = TryCollectGpu();
        HasCompletedCpuFrame = false;
        Array.Clear(_cpuSectionSeconds);
        _previousMarkTimestamp = cpuWorkStartTimestamp;
        _openScopes = 0;
        Mark(GrFrameSection.FrameStart);
    }

    public void Mark(GrFrameSection section)
    {
        var timestamp = Stopwatch.GetTimestamp();
        _cpuSectionSeconds[(int)section] = Stopwatch.GetElapsedTime(_previousMarkTimestamp, timestamp).TotalSeconds;
        _previousMarkTimestamp = timestamp;
        _profiler.Begin(ScopeNames[(int)section]);
        _openScopes++;
        if (section != GrFrameSection.FrameEnd) return;
        for (; _openScopes > 0; _openScopes--) _profiler.End();
        HasCompletedCpuFrame = true;
    }

    public void BeginRecorded(GrFrameSection section) => _profiler.Begin(RecordedScopeNames[(int)section]);

    public void EndRecorded(GrFrameSection section, double cpuSeconds)
    {
        _profiler.End();
        _cpuSectionSeconds[(int)section] = cpuSeconds;
    }

    private bool TryCollectGpu()
    {
        if (_profiler.SamplesFrameNumber == _collectedFrameNumber || _profiler.Samples.Count == 0) return false;
        _collectedFrameNumber = _profiler.SamplesFrameNumber;
        Array.Clear(_gpuSecondsToFrameEnd);
        Array.Clear(_gpuSectionMarked);
        Array.Clear(_gpuSectionSeconds);
        foreach (var sample in _profiler.Samples)
        {
            var section = Array.IndexOf(ScopeNames, sample.Name);
            if (section >= 0)
            {
                _gpuSecondsToFrameEnd[section] = sample.GpuMilliseconds / 1000.0;
                _gpuSectionMarked[section] = true;
                continue;
            }
            section = Array.IndexOf(RecordedScopeNames, sample.Name);
            if (section >= 0) _gpuSectionSeconds[section] = sample.GpuMilliseconds / 1000.0;
        }

        var previousSecondsToFrameEnd = _gpuSecondsToFrameEnd[(int)GrFrameSection.FrameStart];
        CompletedGpuFrameSeconds = previousSecondsToFrameEnd;
        CompletedGpuSecondsSincePreviousFrame = _profiler.GpuMillisecondsSincePreviousFrame / 1000.0;
        for (var section = (int)GrFrameSection.SceneUpdate; section < GrFrameSections.Count; section++)
        {
            if (!_gpuSectionMarked[section]) continue;
            _gpuSectionSeconds[section] = Math.Max(0.0, previousSecondsToFrameEnd - _gpuSecondsToFrameEnd[section]);
            previousSecondsToFrameEnd = _gpuSecondsToFrameEnd[section];
        }

        return true;
    }
}
