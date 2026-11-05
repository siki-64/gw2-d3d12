using System;
using System.Linq;
using System.Numerics;
using Gw2.Client.Scene;
using Crystal.ImGui;

namespace Gw2.Client.Ui;

public sealed class UiPerfGraphsWindow(Gw2PerfGraphs graphs, Func<float> smoothedFrameSeconds)
{
    private const float WindowWidth = 650f;
    private const float WindowHeight = 600f;
    private const float PlotMargin = 40f;
    private const ImGuiWindowFlags LockedFlags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoInputs;

    private bool _isLocked;
    private bool _isTransparent;
    private Gw2PerfGraph? _openGraph;

    public bool IsOpen { get; set; }

    public void Draw()
    {
        if (!IsOpen) return;
        var flags = (_isTransparent ? ImGuiWindowFlags.NoBackground : ImGuiWindowFlags.None) | (_isLocked ? LockedFlags : ImGuiWindowFlags.None);
        ImGui.SetNextWindowSize(new Vector2(WindowWidth, WindowHeight), ImGuiCond.FirstUseEver);
        var isOpen = IsOpen;
        if (ImGui.Begin("Perf Graphs", ref isOpen, flags))
        {
            ImGui.Checkbox("Locked", ref _isLocked);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Locks input of window. Close/re-open to reset.");
            ImGui.SameLine();
            var isPaused = graphs.IsPaused;
            if (ImGui.Checkbox("Paused", ref isPaused)) graphs.IsPaused = isPaused;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Pause graph updates. Data recieved while paused is ignored.");
            ImGui.SameLine();
            ImGui.Checkbox("Transparent", ref _isTransparent);
            var frameSeconds = smoothedFrameSeconds();
            ImGui.Text(frameSeconds > 0f ? $"{1f / frameSeconds:F0} fps ({frameSeconds * 1000f:F2} ms)" : "-- fps");
            if (ImGui.BeginChild("Graphs")) DrawGraphs();
            ImGui.EndChild();
        }

        ImGui.End();
        if (!isOpen) _isLocked = false;
        IsOpen = isOpen;
    }

    private void DrawGraphs()
    {
        if (_openGraph is { } openGraph)
        {
            DrawGraph(openGraph);
            if (_openGraph is not null) return;
        }

        foreach (var graph in graphs.Graphs)
        {
            DrawGraph(graph);
            if (_openGraph is not null) return;
        }
    }

    private void DrawGraph(Gw2PerfGraph graph)
    {
        if (!ImGui.CollapsingHeader(graph.Title))
        {
            if (_openGraph == graph) _openGraph = null;
            return;
        }

        _openGraph = graph;
        var widestName = graph.Series.Max(series => ImGui.CalcTextSize(series.Name).X);
        var width = MathF.Max(1f, ImGui.GetContentRegionAvail().X - PlotMargin - widestName);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Vector4.Zero);
        foreach (var series in graph.Series)
        {
            ImGui.PushStyleColor(ImGuiCol.PlotLines, ColorOf(series.Argb));
            var overlay = series.Values[graph.WriteIndex].ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
            ImGui.PlotLines(series.Name, series.Values.AsSpan(0, Gw2PerfGraph.SampleCount), graph.WriteIndex, overlay, graph.ScaleMinimum, graph.ScaleMaximum, new Vector2(width, width / 2f));
            ImGui.PopStyleColor();
        }

        ImGui.PopStyleColor();
    }

    private static Vector4 ColorOf(uint argb) => new(
        ((argb >> 16) & 0xFF) / 255f,
        ((argb >> 8) & 0xFF) / 255f,
        (argb & 0xFF) / 255f,
        (argb >> 24) / 255f);
}
