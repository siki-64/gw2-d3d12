using System.Numerics;
using Crystal.ImGui;

namespace Gw2.Client.Ui;

public sealed class UiGameMenuWindow
{
    private const float WindowWidth = 220f;
    private static readonly Vector2 Pivot = new(0.5f, 0.5f);

    public bool IsOpen { get; set; }

    public bool ExitRequested { get; private set; }

    public bool KeybindsRequested { get; set; }

    public bool GraphicsSettingsRequested { get; set; }

    public bool GeneralOptionsRequested { get; set; }

    public void Draw(Vector2 screenSize)
    {
        if (!IsOpen) return;
        ImGui.SetNextWindowPos(screenSize * 0.5f, ImGuiCond.Always, Pivot);
        ImGui.SetNextWindowSize(new Vector2(WindowWidth, 0f), ImGuiCond.Always);
        var isOpen = IsOpen;
        if (ImGui.Begin("Menu", ref isOpen, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove))
        {
            var buttonSize = new Vector2(-1f, 0f);
            if (ImGui.Button("Key Bindings", buttonSize))
            {
                KeybindsRequested = true;
                isOpen = false;
            }
            if (ImGui.Button("General Options", buttonSize))
            {
                GeneralOptionsRequested = true;
                isOpen = false;
            }
            if (ImGui.Button("Graphics Options", buttonSize))
            {
                GraphicsSettingsRequested = true;
                isOpen = false;
            }
            if (ImGui.Button("Resume", buttonSize)) isOpen = false;
            ImGui.Separator();
            if (ImGui.Button("Exit Game", buttonSize)) ExitRequested = true;
        }
        ImGui.End();
        IsOpen = isOpen;
    }
}
