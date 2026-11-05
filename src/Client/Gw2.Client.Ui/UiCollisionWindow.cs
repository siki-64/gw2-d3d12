using System.Globalization;
using System.Numerics;
using Gw2.Client.Scene;
using Crystal.ImGui;

namespace Gw2.Client.Ui;

public sealed class UiCollisionWindow
{
    private const float WindowWidth = 360f;
    private const float WindowHeight = 140f;
    private static readonly Vector2 WindowPosition = new(720f, 60f);

    public bool IsOpen { get; set; }

    public Vector3 Position { get; set; }

    public Gw2.Client.Scene.Havok.Gw2SurfaceHit? Ground { get; set; }

    public int BodyCount { get; set; }

    public void Draw()
    {
        if (!IsOpen) return;
        ImGui.SetNextWindowPos(WindowPosition, ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(WindowWidth, WindowHeight), ImGuiCond.FirstUseEver);
        var isOpen = IsOpen;
        if (ImGui.Begin("Collision", ref isOpen))
        {
            ImGui.Text(string.Create(CultureInfo.InvariantCulture, $"Bodies {BodyCount}"));
            ImGui.Text(string.Create(CultureInfo.InvariantCulture, $"Position {Position.X:F1} {Position.Y:F1} {Position.Z:F1}"));
            if (Ground is { } ground)
            {
                ImGui.Text(string.Create(CultureInfo.InvariantCulture, $"Ground {ground.Position.Z:F2} ({ground.Position.Z - Position.Z:F2} below)"));
                ImGui.Text(string.Create(CultureInfo.InvariantCulture, $"{(ground.IsTerrain ? "terrain" : "model")} {ground.BodyId}  normal {ground.Normal.X:F2} {ground.Normal.Y:F2} {ground.Normal.Z:F2}"));
            }
            else
            {
                ImGui.Text("Ground none");
            }
        }

        ImGui.End();
        IsOpen = isOpen;
    }
}
