using System;
using System.Linq;
using System.Numerics;
using Gw2.Client.Scene;
using Gw2.Client.Graphics;
using Crystal;
using Crystal.Graphics;
using Crystal.ImGui;
using Crystal.Input;

namespace Gw2.Client.Ui;

public sealed class UiPerfOverlay : IDisposable
{
    private static readonly uint[] SectionColors =
    [
        0xFFFF4040, 0xFFFFA040, 0xFFFFFF40, 0xFF40FF40, 0xFF40FFFF, 0xFF4080FF, 0xFFA040FF, 0xFFFF40FF, 0xFFC0C0C0, 0xFF808080, 0xFF40C080,
    ];

    private readonly ImGuiRenderer _renderer;
    private readonly Gw2PerfGraphs _graphs = new();
    private readonly Gw2FrameTimeSmoothing _frameTimeSmoothing = new();
    private float _smoothedFrameSeconds;
    private readonly Gw2PerfGraph _frameTimeGraph;
    private readonly Gw2PerfGraph _cpuGpuGraph;
    private readonly Gw2PerfGraph _gpuSectionGraph;
    private readonly Gw2PerfGraph _cpuSectionGraph;
    private readonly UiPerfGraphsWindow _graphsWindow;
    private readonly UiCollisionWindow _collisionWindow = new();
    private readonly UiDebugViewWindow _debugViewWindow = new();
    private readonly UiKeybindWindow _keybindWindow = new();
    private readonly UiGameMenuWindow _menuWindow = new();
    private readonly UiGraphicsSettingsWindow _graphicsSettingsWindow = new();
    private readonly UiGeneralOptionsWindow _generalOptionsWindow = new();
    private readonly float[] _gpuSectionSample;
    private readonly float[] _cpuSectionSample;
    private float _lastGpuMilliseconds;
    private readonly GraphicsDevice _graphicsDevice;

    public UiPerfOverlay(Game game)
    {
        _graphicsDevice = game.GraphicsDevice;
        _renderer = new ImGuiRenderer(game);
        _frameTimeGraph = _graphs.Register("Frame Time (ms)", 0f, 100f, new Gw2PerfGraphSeries("Time", 0xFF4040FF), new Gw2PerfGraphSeries("Smooth time", 0xFFFFFF00));
        _cpuGpuGraph = _graphs.Register("CPU and GPU (ms)", 0f, 33.3f, new Gw2PerfGraphSeries("CPU", 0xFF40FF40), new Gw2PerfGraphSeries("GPU", 0xFFFF8040));
        var gpuSections = Enum.GetValues<GrFrameSection>().Skip(1).ToArray();
        _gpuSectionGraph = _graphs.Register("GPU sections (ms)", 0f, 5f, gpuSections.Select((section, index) => new Gw2PerfGraphSeries(section == GrFrameSection.FrameEnd ? "Rest" : section.ToString(), SectionColors[index % SectionColors.Length])).ToArray());
        _gpuSectionSample = new float[gpuSections.Length];
        var cpuSections = Enum.GetValues<GrFrameSection>();
        _cpuSectionGraph = _graphs.Register("CPU sections (ms)", 0f, 5f, cpuSections.Select((section, index) => new Gw2PerfGraphSeries(section switch { GrFrameSection.FrameStart => "Update", GrFrameSection.FrameEnd => "Rest", _ => section.ToString() }, SectionColors[index % SectionColors.Length])).ToArray());
        _cpuSectionSample = new float[cpuSections.Length];
        _graphsWindow = new UiPerfGraphsWindow(_graphs, () => _smoothedFrameSeconds);
    }

    public bool IsOpen
    {
        get => _graphsWindow.IsOpen;
        init => _graphsWindow.IsOpen = _collisionWindow.IsOpen = value;
    }

    public UiCollisionWindow Collision => _collisionWindow;

    public GrDebugView DebugView
    {
        get => _debugViewWindow.Selected;
        init => _debugViewWindow.Selected = value;
    }

    public bool ShowsColliderCapsules => _debugViewWindow.ShowsColliderCapsules;

    public UiKeybindWindow Keybinds => _keybindWindow;

    public UiGraphicsSettingsWindow GraphicsSettings => _graphicsSettingsWindow;

    public UiGeneralOptionsWindow GeneralOptions => _generalOptionsWindow;

    public bool ExitRequested => _menuWindow.ExitRequested;

    public bool IsMenuAvailable { get; set; }

    public UiDebugHudPanels Hud { get; } = new();

    public bool WantsMouse => IsAnyWindowOpen && _renderer.WantCaptureMouse;

    public bool IsAnyWindowOpen => _graphsWindow.IsOpen || _collisionWindow.IsOpen || _debugViewWindow.IsOpen || _keybindWindow.IsOpen || _graphicsSettingsWindow.IsOpen || _generalOptionsWindow.IsOpen || _menuWindow.IsOpen;

    public void RecordPresent(double frameSeconds, double cpuSeconds)
    {
        var frameTimeSeconds = (float)frameSeconds;
        _smoothedFrameSeconds = _frameTimeSmoothing.Smooth(frameTimeSeconds);
        _frameTimeGraph.AddSample([frameTimeSeconds * 1000f, _smoothedFrameSeconds * 1000f]);
        _cpuGpuGraph.AddSample([(float)(cpuSeconds * 1000.0), _lastGpuMilliseconds]);
    }

    public void RecordGpu(GrFrameProfiler timer)
    {
        if (!timer.HasCompletedGpuFrame) return;
        _lastGpuMilliseconds = (float)(timer.CompletedGpuFrameSeconds * 1000.0);
        var sectionSeconds = timer.CompletedGpuSectionSeconds;
        for (var section = 0; section < _gpuSectionSample.Length; section++) _gpuSectionSample[section] = (float)(sectionSeconds[section + 1] * 1000.0);
        _gpuSectionGraph.AddSample(_gpuSectionSample);
    }

    public void RecordCpuSections(GrFrameProfiler timer)
    {
        var sectionSeconds = timer.CompletedCpuSectionSeconds;
        for (var section = 0; section < _cpuSectionSample.Length; section++) _cpuSectionSample[section] = (float)(sectionSeconds[section] * 1000.0);
        _cpuSectionGraph.AddSample(_cpuSectionSample);
    }

    public void Update(InputFrame input)
    {
        var shift = input.Keyboard.IsKeyDown(Keys.LeftShift) || input.Keyboard.IsKeyDown(Keys.RightShift);
        if (shift && input.IsKeyPressed(Keys.F10)) _graphsWindow.IsOpen = _collisionWindow.IsOpen = !(_graphsWindow.IsOpen || _collisionWindow.IsOpen);
        if (shift && input.IsKeyPressed(Keys.F9)) _debugViewWindow.IsOpen = !_debugViewWindow.IsOpen;
        if (shift && input.IsKeyPressed(Keys.F11) && _keybindWindow.Capturing is null) _keybindWindow.IsOpen = !_keybindWindow.IsOpen;
        if (IsMenuAvailable && input.IsKeyPressed(Keys.Escape) && _keybindWindow.Capturing is null) CloseTopmostOrOpenMenu();
        if (_menuWindow.KeybindsRequested)
        {
            _menuWindow.KeybindsRequested = false;
            _keybindWindow.IsOpen = true;
        }
        if (_menuWindow.GraphicsSettingsRequested)
        {
            _menuWindow.GraphicsSettingsRequested = false;
            _graphicsSettingsWindow.IsOpen = true;
        }
        if (_menuWindow.GeneralOptionsRequested)
        {
            _menuWindow.GeneralOptionsRequested = false;
            _generalOptionsWindow.IsOpen = true;
        }
    }

    private void CloseTopmostOrOpenMenu()
    {
        if (_menuWindow.IsOpen) _menuWindow.IsOpen = false;
        else if (_keybindWindow.IsOpen) _keybindWindow.IsOpen = false;
        else if (_graphicsSettingsWindow.IsOpen) _graphicsSettingsWindow.IsOpen = false;
        else if (_generalOptionsWindow.IsOpen) _generalOptionsWindow.IsOpen = false;
        else _menuWindow.IsOpen = true;
    }

    public void Draw(GameTime gameTime)
    {
        _graphicsDevice.SetRenderTarget(null);
        _renderer.BeginFrame(gameTime);
        if (_debugViewWindow.ShowsDebugHudPanels) Hud.Draw(new Vector2(_graphicsDevice.BackBufferWidth, _graphicsDevice.BackBufferHeight));
        _graphsWindow.Draw();
        _collisionWindow.Draw();
        _debugViewWindow.Draw();
        _keybindWindow.Draw();
        _graphicsSettingsWindow.Draw();
        _generalOptionsWindow.Draw();
        _menuWindow.Draw(new Vector2(_graphicsDevice.BackBufferWidth, _graphicsDevice.BackBufferHeight));
        _renderer.EndFrame();
    }

    public void Dispose() => _renderer.Dispose();
}
