using System;
using System.Collections.Generic;
using System.Numerics;
using Crystal;
using Crystal.Graphics;

namespace Gw2.Client.Graphics;

public sealed class GrShadowMap : IDisposable
{
    private readonly GrDevice _device;
    private readonly Texture2D _target;
    private readonly Texture2D _depth;

    public GrShadowMap(GrDevice device, GrShadowSettings settings)
    {
        _device = device;
        Settings = settings;
        var width = settings.CascadeResolution * settings.CascadeCount;
        _target = device.CreateRenderTarget(width, settings.CascadeResolution, SurfaceFormat.R32Float, "ShadowCascades", clearColor: Color.White);
        _depth = device.CreateDepthTarget(width, settings.CascadeResolution, "ShadowCascadesDepth");
    }

    public GrShadowSettings Settings { get; }

    public Texture2D Texture => _target;

    private readonly List<GrDepthCaster> _visibleCasters = [];

    public void Render(IReadOnlyList<GrShadowCascade> cascades, GrSceneDepth sceneDepth, GrConstants constants, IReadOnlyList<GrDepthCaster> casters, GrDepthCasterBatcher batcher, Func<int, GrCullFrustum, IReadOnlyList<GrDepthCaster>>? gpuCasters = null)
    {
        var graphicsDevice = _device.GraphicsDevice;
        _device.SetRenderTarget(_target, _depth);
        graphicsDevice.Clear(Color.White);
        graphicsDevice.ClearDepthStencil();
        sceneDepth.SetSlopeScaledDepthBias(Settings.SlopeBias);
        for (var index = 0; index < cascades.Count; index++)
        {
            graphicsDevice.Viewport = new Viewport(index * Settings.CascadeResolution, 0, Settings.CascadeResolution, Settings.CascadeResolution);
            var casterFrustum = GrCullFrustum.ShadowCasters(cascades[index].LightViewProjection);
            _visibleCasters.Clear();
            foreach (var caster in casters)
                if (caster.WorldBounds is not { } worldBounds || casterFrustum.Intersects(worldBounds))
                    _visibleCasters.Add(caster);
            DrawCasters(batcher.Batch(_visibleCasters), sceneDepth, constants, cascades[index].LightViewProjection);
            if (gpuCasters is not null) DrawCasters(gpuCasters(index, casterFrustum), sceneDepth, constants, cascades[index].LightViewProjection);
        }

        sceneDepth.SetSlopeScaledDepthBias(0f);
        _device.SetBackBuffer();
    }

    private static void DrawCasters(IReadOnlyList<GrDepthCaster> casters, GrSceneDepth sceneDepth, GrConstants constants, Matrix4x4 lightViewProjection)
    {
        foreach (var caster in casters) sceneDepth.Draw(caster, constants, lightViewProjection, caster.Rasterizer ?? RasterizerState.CullFront);
    }

    public void Dispose()
    {
        _target.Dispose();
        _depth.Dispose();
    }
}
