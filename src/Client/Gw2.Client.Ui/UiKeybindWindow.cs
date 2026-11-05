using System.Collections.Generic;
using System.Numerics;
using Crystal.ImGui;

namespace Gw2.Client.Ui;

public sealed record UiKeybindRow(uint ControlId, string Name, string Primary, string Secondary);

public readonly record struct UiKeybindSlot(uint ControlId, bool Secondary);

public sealed class UiKeybindWindow
{
    private const float WindowWidth = 460f;
    private const float WindowHeight = 520f;
    private const string Unbound = "-";
    private static readonly Vector2 WindowPosition = new(80f, 80f);

    public bool IsOpen { get; set; }

    public IReadOnlyList<UiKeybindRow> Rows { get; set; } = [];

    public UiKeybindSlot? Capturing { get; set; }

    public void Draw()
    {
        if (!IsOpen) return;
        ImGui.SetNextWindowPos(WindowPosition, ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(WindowWidth, WindowHeight), ImGuiCond.FirstUseEver);
        var isOpen = IsOpen;
        if (ImGui.Begin("Key bindings", ref isOpen))
        {
            ImGui.Text(Capturing is null ? "Click a binding, then press a key." : "Press a key, with Shift, Ctrl or Alt held for a modifier. Escape cancels.");
            if (ImGui.BeginTable("bindings", 3))
            {
                foreach (var row in Rows)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text(row.Name);
                    BindButton(row, row.Primary, secondary: false);
                    BindButton(row, row.Secondary, secondary: true);
                }
                ImGui.EndTable();
            }
        }
        ImGui.End();
        IsOpen = isOpen;
        if (!IsOpen) Capturing = null;
    }

    private void BindButton(UiKeybindRow row, string label, bool secondary)
    {
        ImGui.TableNextColumn();
        var slot = new UiKeybindSlot(row.ControlId, secondary);
        var shown = Capturing == slot ? "..." : label.Length == 0 ? Unbound : label;
        if (ImGui.Button($"{shown}##{row.ControlId}{(secondary ? "s" : "p")}")) Capturing = slot;
    }
}
