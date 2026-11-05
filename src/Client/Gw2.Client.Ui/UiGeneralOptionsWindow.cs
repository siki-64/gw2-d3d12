using System.Numerics;
using Crystal.ImGui;

namespace Gw2.Client.Ui;

public sealed record UiCameraOptions(
    bool Smoothing,
    bool SnapsCloseToTheCharacter,
    int CollisionSensitivity,
    int ZoomSpeedOption,
    int FieldOfViewDegrees,
    float RotationSpeedPercent);

public sealed record UiCameraOptionLimits(
    int MaximumCollisionSensitivity,
    int MaximumZoomSpeedOption,
    int MinimumFieldOfViewDegrees,
    int MaximumFieldOfViewDegrees,
    float MinimumRotationSpeedPercent,
    float MaximumRotationSpeedPercent);

public sealed class UiGeneralOptionsWindow
{
    private const float WindowWidth = 400f;
    private const float WindowHeight = 260f;
    private static readonly Vector2 WindowPosition = new(140f, 120f);

    public bool IsOpen { get; set; }

    public UiCameraOptions? Camera { get; set; }

    public UiCameraOptionLimits? Limits { get; set; }

    public UiCameraOptions? Edited { get; set; }

    public bool EditFinished { get; set; }

    public void Draw()
    {
        if (!IsOpen) return;
        ImGui.SetNextWindowPos(WindowPosition, ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(WindowWidth, WindowHeight), ImGuiCond.FirstUseEver);
        var isOpen = IsOpen;
        if (ImGui.Begin("General Options", ref isOpen) && Camera is { } camera && Limits is { } limits)
        {
            ImGui.SeparatorText("Camera");
            var smoothing = camera.Smoothing;
            if (ImGui.Checkbox("Camera smoothing", ref smoothing)) Finish(camera with { Smoothing = smoothing });
            var snaps = camera.SnapsCloseToTheCharacter;
            if (ImGui.Checkbox("Snap camera close to the character", ref snaps)) Finish(camera with { SnapsCloseToTheCharacter = snaps });
            var collision = camera.CollisionSensitivity;
            if (ImGui.SliderInt("Collision sensitivity", ref collision, 0, limits.MaximumCollisionSensitivity)) Edit(camera with { CollisionSensitivity = collision });
            FinishWhenReleased();
            var zoomSpeed = camera.ZoomSpeedOption;
            if (ImGui.SliderInt("Zoom speed", ref zoomSpeed, 0, limits.MaximumZoomSpeedOption)) Edit(camera with { ZoomSpeedOption = zoomSpeed });
            FinishWhenReleased();
            var fieldOfView = camera.FieldOfViewDegrees;
            if (ImGui.SliderInt("Field of view", ref fieldOfView, limits.MinimumFieldOfViewDegrees, limits.MaximumFieldOfViewDegrees, "%d°")) Edit(camera with { FieldOfViewDegrees = fieldOfView });
            FinishWhenReleased();
            var rotation = camera.RotationSpeedPercent;
            if (ImGui.SliderFloat("Rotation speed", ref rotation, limits.MinimumRotationSpeedPercent, limits.MaximumRotationSpeedPercent, "%.0f%%")) Edit(camera with { RotationSpeedPercent = rotation });
            FinishWhenReleased();
        }
        ImGui.End();
        IsOpen = isOpen;
    }

    private void Edit(UiCameraOptions camera)
    {
        Camera = camera;
        Edited = camera;
    }

    private void Finish(UiCameraOptions camera)
    {
        Edit(camera);
        EditFinished = true;
    }

    private void FinishWhenReleased()
    {
        if (ImGui.IsItemDeactivatedAfterEdit()) EditFinished = true;
    }
}
