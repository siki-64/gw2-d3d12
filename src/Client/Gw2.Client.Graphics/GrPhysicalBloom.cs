using System;
using System.Collections.Generic;
using System.Numerics;
using Crystal.Graphics;
using Gw2.Dat.Content;

namespace Gw2.Client.Graphics;

public sealed class GrPhysicalBloom : IDisposable
{
    private const int MaximumLevels = 7;
    private const int SmallestLevelHeight = 8;
    private const int ExeBlurPassCount = 4;

    private static readonly BlendState AddToTarget = new(new RenderTargetBlendDescription
    {
        BlendEnable = true,
        SourceBlend = Blend.One,
        DestinationBlend = Blend.One,
        SourceBlendAlpha = Blend.Zero,
        DestinationBlendAlpha = Blend.One,
    });

    private readonly GrDevice _device;
    private readonly ShaderProgram _prefilter;
    private readonly ShaderProgram _downsample;
    private readonly ShaderProgram _upsample;
    private readonly ShaderProgram _resolve;
    private readonly List<Texture2D> _levels = [];
    private Texture2D? _resolved;

    public GrPhysicalBloom(GrDevice device)
    {
        _device = device;
        var library = device.LoadShader("GrPhysicalBloom");
        _prefilter = library.GetProgram("Prefilter");
        _downsample = library.GetProgram("Downsample");
        _upsample = library.GetProgram("Upsample");
        _resolve = library.GetProgram("Resolve");
    }

    public static float EnergyScale(Gw2EnvironmentBloom bloom, int levelCount) => MathF.Pow(bloom.BlurGain, ExeBlurPassCount) / levelCount;

    public Texture2D Render(Texture2D scene, Gw2EnvironmentBloom bloom)
    {
        EnsureTargets(scene.Width, scene.Height);
        var previousTargets = _device.BoundTargets;
        var downsampleLevel = new Vector4(bloom.DownsampleLevelConstant.X, bloom.DownsampleLevelConstant.Y, bloom.DownsampleLevelConstant.Z, bloom.DownsampleLevelConstant.W);
        _prefilter.Parameters["DownsampleLevel"]?.SetValue(downsampleLevel);
        _downsample.Parameters["DownsampleLevel"]?.SetValue(downsampleLevel);
        Draw(_prefilter, BlendState.Opaque, scene, _levels[0]);
        for (var level = 1; level < _levels.Count; level++) Draw(_downsample, BlendState.Opaque, _levels[level - 1], _levels[level]);

        for (var level = _levels.Count - 1; level > 0; level--) Draw(_upsample, AddToTarget, _levels[level], _levels[level - 1]);

        _resolve.Parameters["BloomResolve"]?.SetValue(new Vector4(EnergyScale(bloom, _levels.Count), 0f, 0f, 0f));
        Draw(_resolve, BlendState.Opaque, _levels[0], _resolved!);
        _device.SetRenderTargets(previousTargets);
        return _resolved!;
    }

    private void Draw(ShaderProgram program, BlendState blendState, Texture2D source, Texture2D target)
    {
        _device.SetRenderTarget(target, null);
        var parameters = program.Parameters;
        parameters["Source"]?.SetValue(source);
        parameters["SourceSampler"]?.SetValue(_device.Sampler(SamplerDescription.LinearClamp));
        parameters["SourceTexelSize"]?.SetValue(new Vector4(1f / source.Width, 1f / source.Height, 0f, 0f));
        _device.ApplyPipeline(program, blendState, DepthStencilState.None, RasterizerState.CullNone, GrFullScreenTriangle.Layout);
        GrFullScreenTriangle.Draw(_device.GraphicsDevice);
    }

    private void EnsureTargets(int sceneWidth, int sceneHeight)
    {
        var width = Math.Max(sceneWidth >> 1, 1);
        var height = Math.Max(sceneHeight >> 1, 1);
        if (_resolved is { } existing && existing.Width == width && existing.Height == height) return;
        DisposeTargets();
        _resolved = _device.CreateRenderTarget(width, height, SurfaceFormat.Rgba16Float, "BloomResolved");
        while (_levels.Count < MaximumLevels && (_levels.Count == 0 || height >= SmallestLevelHeight))
        {
            _levels.Add(_device.CreateRenderTarget(width, height, SurfaceFormat.Rgba16Float, $"BloomLevel{_levels.Count}"));
            width = Math.Max(width >> 1, 1);
            height = Math.Max(height >> 1, 1);
        }
    }

    private void DisposeTargets()
    {
        foreach (var level in _levels) level.Dispose();
        _levels.Clear();
        _resolved?.Dispose();
        _resolved = null;
    }

    public void Dispose() => DisposeTargets();
}
