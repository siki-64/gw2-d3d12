using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Crystal.ImGui;

namespace Gw2.Client.Ui;

public readonly record struct UiHudBar(string Label, float Value, float Maximum)
{
    public float Fraction => Maximum > 0f ? System.Math.Clamp(Value / Maximum, 0f, 1f) : 0f;
}

public enum UiHudFloaterKind
{
    Damage,
    Critical,
    Heal,
    Avoidance,
}

public readonly record struct UiHudFloater(string Text, bool IsIncoming, UiHudFloaterKind Kind, float AgeFraction);

public sealed record UiHudSpecializationLine(string Specialization, IReadOnlyList<string> Traits);

public sealed record UiDebugHudState(
    IReadOnlyList<UiHudBar> OwnBars,
    string? OwnLifeStateLabel,
    string? ProfessionStateLabel,
    string? TargetName,
    string? TargetLifeStateLabel,
    UiHudBar? TargetHealth,
    UiHudBar? TargetBreakBar,
    string? HoveredName,
    IReadOnlyList<UiHudSpecializationLine> SpecializationLines,
    IReadOnlyList<UiHudFloater> Floaters);

public sealed class UiDebugHudPanels
{
    private const float BarWidth = 300f;
    private const float BarHeight = 16f;
    private const float EdgeMargin = 12f;
    private const float FloaterRise = 60f;
    private const float FloaterRowHeight = 18f;
    private const ImGuiWindowFlags FixedWindow =
        ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse |
        ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing;

    public UiDebugHudState? State { get; set; }

    public void Draw(Vector2 screenSize)
    {
        if (State is not { } state) return;
        DrawSelection(state, screenSize);
        DrawOwnVitals(state, screenSize);
        DrawSpecializations(state, screenSize);
        DrawFloaters(state, screenSize);
    }

    private static void DrawFloaters(UiDebugHudState state, Vector2 screenSize)
    {
        if (state.Floaters.Count == 0) return;
        var drawList = ImGui.GetForegroundDrawList();
        var outgoingCount = 0;
        var incomingCount = 0;
        foreach (var floater in state.Floaters)
        {
            var row = floater.IsIncoming ? incomingCount++ : outgoingCount++;
            var column = floater.IsIncoming ? screenSize.X * 0.42f : screenSize.X * 0.58f;
            var rise = floater.AgeFraction * FloaterRise;
            var position = new Vector2(column, screenSize.Y * 0.45f - row * FloaterRowHeight - rise);
            var alpha = 1f - floater.AgeFraction;
            drawList.AddText(position, ImGui.GetColorU32(FloaterColorOf(floater) with { W = alpha }), floater.Text);
        }
    }

    private static Vector4 FloaterColorOf(UiHudFloater floater) => floater.Kind switch
    {
        UiHudFloaterKind.Heal => new Vector4(0.4f, 1f, 0.4f, 1f),
        UiHudFloaterKind.Avoidance => new Vector4(0.8f, 0.8f, 0.8f, 1f),
        UiHudFloaterKind.Critical => new Vector4(1f, 0.85f, 0.2f, 1f),
        _ when floater.IsIncoming => new Vector4(1f, 0.3f, 0.3f, 1f),
        _ => new Vector4(1f, 1f, 1f, 1f),
    };

    private static void DrawSpecializations(UiDebugHudState state, Vector2 screenSize)
    {
        if (state.SpecializationLines.Count == 0) return;
        ImGui.SetNextWindowPos(new Vector2(screenSize.X - EdgeMargin, screenSize.Y * 0.5f), ImGuiCond.Always, new Vector2(1f, 0.5f));
        var open = true;
        if (ImGui.Begin("Specializations", ref open, FixedWindow))
        {
            foreach (var line in state.SpecializationLines)
            {
                ImGui.Text(line.Specialization);
                foreach (var trait in line.Traits) ImGui.BulletText(trait);
            }
        }
        ImGui.End();
    }

    private static void DrawSelection(UiDebugHudState state, Vector2 screenSize)
    {
        ImGui.SetNextWindowPos(new Vector2(screenSize.X * 0.5f, EdgeMargin), ImGuiCond.Always, new Vector2(0.5f, 0f));
        var open = true;
        if (ImGui.Begin("Selection", ref open, FixedWindow))
        {
            ImGui.Text(state.TargetName is { } target ? "Target: " + target : "Target: none");
            if (state.TargetLifeStateLabel is { } targetLifeState) LifeStateText(targetLifeState);
            if (state.TargetHealth is { } health) Bar(health);
            if (state.TargetBreakBar is { } breakBar) Bar(breakBar);
            if (state.HoveredName is { } hovered) ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "Hover: " + hovered);
        }
        ImGui.End();
    }

    private static void DrawOwnVitals(UiDebugHudState state, Vector2 screenSize)
    {
        ImGui.SetNextWindowPos(new Vector2(EdgeMargin, screenSize.Y * 0.5f), ImGuiCond.Always, new Vector2(0f, 0.5f));
        var open = true;
        if (ImGui.Begin("Vitals", ref open, FixedWindow))
        {
            if (state.OwnLifeStateLabel is { } ownLifeState) LifeStateText(ownLifeState);
            foreach (var bar in state.OwnBars) Bar(bar);
            if (state.ProfessionStateLabel is { } professionState) ImGui.Text(professionState);
        }
        ImGui.End();
    }

    private static void LifeStateText(string label) =>
        ImGui.TextColored(label switch
        {
            _ when label.StartsWith("Downed", StringComparison.Ordinal) => new Vector4(1f, 0.6f, 0.1f, 1f),
            _ when label.StartsWith("Defeated", StringComparison.Ordinal) => new Vector4(1f, 0.25f, 0.25f, 1f),
            _ => new Vector4(0.5f, 1f, 0.5f, 1f),
        }, label);

    private static void Bar(UiHudBar bar) =>
        ImGui.ProgressBar(bar.Fraction, new Vector2(BarWidth, BarHeight), string.Create(CultureInfo.InvariantCulture, $"{bar.Label} {bar.Value:F0} / {bar.Maximum:F0}"));
}
