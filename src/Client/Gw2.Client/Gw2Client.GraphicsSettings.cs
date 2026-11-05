using System;
using System.Linq;
using Gw2.Client.Scene;
using Gw2.Client.Settings;
using Gw2.Client.Ui;

namespace Gw2.Client;

public sealed partial class Gw2Client
{

    private bool _graphicsSettingsNeedRestart;
    private Gw2GraphicsState? _savedGraphicsState;

    private Gw2GraphicsState GraphicsState => new(_options.Graphics, _options.Benchmark.FrameLimit);

    private void UpdateGraphicsSettingsWindow()
    {
        if (_perfOverlay?.GraphicsSettings is not { IsOpen: true } window) return;
        if (window.Rows.Count == 0) window.Rows = GraphicsSettingRows();
        if (window.Change is not { } change) return;
        window.Change = null;
        if (Gw2GraphicsSettings.All.FirstOrDefault(setting => setting.Key == change.Key) is not { } changed) return;
        _savedGraphicsState = changed.WithValue(_savedGraphicsState ??= Gw2GraphicsSettings.LoadOrSeed(), change.Value);
        Gw2GraphicsSettings.Save(_savedGraphicsState);
        if (changed.AppliesWhileRunning) UseGraphicsState(changed.WithValue(GraphicsState, change.Value));
        else _graphicsSettingsNeedRestart = true;
        window.Rows = GraphicsSettingRows();
        window.HasUnappliedChanges = _graphicsSettingsNeedRestart;
    }

    private void UseGraphicsState(Gw2GraphicsState state)
    {
        var graphics = state.Graphics;
        _options = _options with { Graphics = graphics, Benchmark = _options.Benchmark with { FrameLimit = state.FrameLimit } };
        _framePeriod = Gw2FrameLimit.PeriodOf(state.FrameLimit);
        if (_postProcessCombine is not null)
        {
            _postProcessCombine.Gamma = graphics.Gamma;
            _postProcessCombine.PhysicalBloom = graphics.PhysicalBloom;
        }
        if (_characterPass is null) return;
        _characterPass.Reflections = graphics.Reflections;
        _characterPass.AnisotropicFiltering = graphics.AnisotropicFiltering;
        _characterPass.VolumetricFog = graphics.VolumetricFog;
        _characterPass.HiZ = graphics.HiZ;
        _characterPass.ParallelRecording = graphics.ParallelRecording;
        _characterPass.AmbientOcclusion = graphics.PostProcessing.HasFlag(Gw2PostProcessingOptions.AmbientOcclusion);
    }

    private UiGraphicsSettingRow[] GraphicsSettingRows()
    {
        var state = _savedGraphicsState ??= Gw2GraphicsSettings.LoadOrSeed();
        return Gw2GraphicsSettings.All
            .Select(setting => new UiGraphicsSettingRow(
                setting.Section,
                setting.Key,
                setting.Label,
                setting.Kind switch
                {
                    Gw2GraphicsSettingKind.Choice => UiGraphicsSettingKind.Choice,
                    Gw2GraphicsSettingKind.Toggle => UiGraphicsSettingKind.Toggle,
                    _ => UiGraphicsSettingKind.Amount,
                },
                setting.Choices,
                setting.ValueOf(state),
                setting.MinAmount,
                setting.MaxAmount,
                setting.AppliesWhileRunning))
            .ToArray();
    }
}
