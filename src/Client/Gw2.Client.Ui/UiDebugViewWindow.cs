using System;
using System.Numerics;
using Gw2.Client.Graphics;
using Crystal.ImGui;

namespace Gw2.Client.Ui;

public sealed class UiDebugViewWindow
{
    private const float WindowWidth = 220f;
    private const float WindowHeight = 280f;
    private static readonly Vector2 WindowPosition = new(1100f, 60f);
    private static readonly GrDebugView[] Views = Enum.GetValues<GrDebugView>();

    public bool IsOpen { get; set; }

    public GrDebugView Selected { get; set; }

    public bool ShowsColliderCapsules { get; set; }

    public bool ShowsDebugHudPanels { get; set; }

    public void Draw()
    {
        if (!IsOpen) return;
        ImGui.SetNextWindowPos(WindowPosition, ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(WindowWidth, WindowHeight), ImGuiCond.FirstUseEver);
        var isOpen = IsOpen;
        if (ImGui.Begin("Debug view", ref isOpen))
        {
            var showsColliderCapsules = ShowsColliderCapsules;
            if (ImGui.Checkbox("Collider capsules", ref showsColliderCapsules)) ShowsColliderCapsules = showsColliderCapsules;
            var showsDebugHudPanels = ShowsDebugHudPanels;
            if (ImGui.Checkbox("Debug HUD panels", ref showsDebugHudPanels)) ShowsDebugHudPanels = showsDebugHudPanels;
            ImGui.Separator();
            foreach (var view in Views)
                if (ImGui.Selectable(view.ToString(), view == Selected)) Selected = view;
        }

        ImGui.End();
        IsOpen = isOpen;
    }
}
