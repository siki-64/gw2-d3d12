using System;
using System.Collections.Generic;

namespace Gw2.Client.Scene;

public sealed class Gw2PerfGraphs
{
    private readonly List<Gw2PerfGraph> _graphs = [];

    public IReadOnlyList<Gw2PerfGraph> Graphs => _graphs;

    public bool IsPaused { get; set; }

    public Gw2PerfGraph Register(string title, float scaleMinimum, float scaleMaximum, params Gw2PerfGraphSeries[] series)
    {
        var graph = new Gw2PerfGraph(this, title, scaleMinimum, scaleMaximum, series);
        _graphs.Add(graph);
        return graph;
    }
}

public sealed class Gw2PerfGraph
{
    public const int SampleCount = 250;

    private readonly Gw2PerfGraphs _owner;

    internal Gw2PerfGraph(Gw2PerfGraphs owner, string title, float scaleMinimum, float scaleMaximum, IReadOnlyList<Gw2PerfGraphSeries> series)
    {
        _owner = owner;
        Title = title;
        ScaleMinimum = scaleMinimum;
        ScaleMaximum = scaleMaximum;
        Series = series;
    }

    public string Title { get; }

    public float ScaleMinimum { get; }

    public float ScaleMaximum { get; }

    public IReadOnlyList<Gw2PerfGraphSeries> Series { get; }

    public int WriteIndex { get; private set; }

    public void AddSample(ReadOnlySpan<float> values)
    {
        if (_owner.IsPaused) return;
        for (var series = 0; series < Series.Count && series < values.Length; series++) Series[series].Values[WriteIndex] = values[series];
        WriteIndex = (WriteIndex + 1) % SampleCount;
    }
}

public sealed class Gw2PerfGraphSeries(string name, uint argb)
{
    public string Name { get; } = name;

    public uint Argb { get; } = argb;

    public float[] Values { get; } = new float[Gw2PerfGraph.SampleCount];
}

public sealed class Gw2FrameTimeSmoothing
{
    private const float SpikeSeconds = 0.25f;
    private const float AverageRate = 0.125f;

    private float _averageSeconds;
    private float _carrySeconds;

    public float Smooth(float frameSeconds)
    {
        if (frameSeconds >= SpikeSeconds)
        {
            var spikeSeconds = _carrySeconds + frameSeconds;
            _carrySeconds = 0f;
            return spikeSeconds;
        }

        _averageSeconds += (frameSeconds - _averageSeconds) * AverageRate;
        var smoothSeconds = _carrySeconds * AverageRate + _averageSeconds;
        _carrySeconds += frameSeconds - smoothSeconds;
        return smoothSeconds;
    }
}
