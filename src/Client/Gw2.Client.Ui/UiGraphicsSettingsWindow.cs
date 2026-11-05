using System.Collections.Generic;
using System.Numerics;
using Crystal.ImGui;

namespace Gw2.Client.Ui;

public enum UiGraphicsSettingKind { Choice, Toggle, Amount }

public sealed record UiGraphicsSettingRow(string Section, string Key, string Label, UiGraphicsSettingKind Kind, string[] Choices, float Value, float MinAmount, float MaxAmount, bool AppliesWhileRunning);

public sealed record UiGraphicsSettingChange(string Key, float Value);

public sealed class UiGraphicsSettingsWindow
{
    private const float WindowWidth = 380f;
    private const float WindowHeight = 460f;
    private static readonly Vector2 WindowPosition = new(120f, 100f);

    public bool IsOpen { get; set; }

    public IReadOnlyList<UiGraphicsSettingRow> Rows { get; set; } = [];

    public bool HasUnappliedChanges { get; set; }

    public UiGraphicsSettingChange? Change { get; set; }

    public void Draw()
    {
        if (!IsOpen) return;
        ImGui.SetNextWindowPos(WindowPosition, ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(WindowWidth, WindowHeight), ImGuiCond.FirstUseEver);
        var isOpen = IsOpen;
        if (ImGui.Begin("Graphics Options", ref isOpen))
        {
            string? section = null;
            foreach (var row in Rows)
            {
                if (row.Section != section)
                {
                    section = row.Section;
                    ImGui.SeparatorText(section);
                }
                DrawRow(row);
            }
            if (HasUnappliedChanges)
            {
                ImGui.Separator();
                ImGui.Text("Settings marked * take effect after a restart.");
            }
        }
        ImGui.End();
        IsOpen = isOpen;
    }

    private void DrawRow(UiGraphicsSettingRow row)
    {
        var label = row.AppliesWhileRunning ? $"{row.Label}##{row.Key}" : $"{row.Label} *##{row.Key}";
        switch (row.Kind)
        {
            case UiGraphicsSettingKind.Choice:
                var choiceIndex = (int)row.Value;
                if (ImGui.Combo(label, ref choiceIndex, row.Choices)) Change = new UiGraphicsSettingChange(row.Key, choiceIndex);
                break;
            case UiGraphicsSettingKind.Toggle:
                var isOn = row.Value != 0f;
                if (ImGui.Checkbox(label, ref isOn)) Change = new UiGraphicsSettingChange(row.Key, isOn ? 1f : 0f);
                break;
            case UiGraphicsSettingKind.Amount:
                var amount = row.Value;
                if (ImGui.SliderFloat(label, ref amount, row.MinAmount, row.MaxAmount)) Change = new UiGraphicsSettingChange(row.Key, amount);
                break;
        }
    }
}
