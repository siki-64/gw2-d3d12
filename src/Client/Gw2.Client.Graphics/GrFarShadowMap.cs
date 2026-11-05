using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Crystal;
using Crystal.Graphics;
using Gw2.Dat.Content;

namespace Gw2.Client.Graphics;

public sealed class GrFarShadowMap : IDisposable
{
    private readonly GrDevice _device;
    private readonly Gw2MapShadow _shadow;
    private readonly Gw2MapShadowWindow _window;
    private readonly bool _filtersTiles;
    private readonly List<Gw2MapShadowPage> _pagesToLoad = new();
    private readonly Vector2[] _emptyPage = new Vector2[Gw2MapShadow.TileTexels];
    private readonly ShaderProgram? _combineProgram;
    private readonly Texture2D? _clearedDynamicShadow;
    private readonly Texture2D? _combined;

    public GrFarShadowMap(GrDevice device, Gw2MapShadow shadow, int resolution, float depthRange, bool combinesShadowSamples)
    {
        _device = device;
        _shadow = shadow;
        _window = new Gw2MapShadowWindow(shadow, resolution, depthRange, combinesShadowSamples);
        _filtersTiles = !combinesShadowSamples;
        Texture = new Texture2D(device.GraphicsDevice, new TextureDescription { Width = resolution, Height = resolution, Format = SurfaceFormat.Rg32Float, Name = "FarShadowPages" });
        if (combinesShadowSamples)
        {
            _combineProgram = device.LoadShader("GrFarShadowCombine").GetProgram("Combine");
            _clearedDynamicShadow = new Texture2D(device.GraphicsDevice, new TextureDescription { Width = 1, Height = 1, Format = SurfaceFormat.R32Float, Name = "ClearedDynamicShadow" });
            _clearedDynamicShadow.SetData<float>([1f]);
            _combined = device.CreateRenderTarget(resolution, resolution, SurfaceFormat.Rg32Float, "FarShadowCombined");
            var taps = Gw2FarShadowCombine.Taps(resolution);
            var parameters = _combineProgram.Parameters;
            parameters["TapOffsets"]!.SetValue(taps.Select(tap => new Vector4(tap.Offset.X, tap.Offset.Y, 0f, 0f)).ToArray());
            parameters["TapWeights"]!.SetValue(taps.Select(tap => tap.Weight).ToArray());
            parameters["WeightSum"]!.SetValue(Gw2FarShadowCombine.WeightSum);
            parameters["DynamicShadow"]!.SetValue(_clearedDynamicShadow);
            parameters["DynamicShadowSampler"]!.SetValue(device.Sampler(SamplerDescription.PointClamp));
            parameters["BakedShadowSampler"]!.SetValue(device.Sampler(SamplerDescription.PointWrap));
        }
    }

    public Texture2D Texture { get; }

    public Texture2D ShadowTexture => _combined ?? Texture;

    public Gw2MapShadowConstants Constants { get; private set; }

    public void Update(Matrix4x4 viewProjection, Vector3 eye)
    {
        Matrix4x4.Invert(viewProjection, out var clipToWorld);
        Span<Vector3> farCorners = stackalloc Vector3[4];
        var cornerIndex = 0;
        foreach (var clipY in new[] { -1f, 1f })
        foreach (var clipX in new[] { -1f, 1f })
        {
            var corner = Vector4.Transform(new Vector4(clipX, clipY, 1f, 1f), clipToWorld);
            farCorners[cornerIndex++] = new Vector3(corner.X, corner.Y, corner.Z) / corner.W;
        }

        _pagesToLoad.Clear();
        Constants = _window.Update(eye, farCorners, _pagesToLoad);
        foreach (var page in _pagesToLoad)
            Texture.SetData<Vector2>(PageMoments(page), new Rectangle(page.PhysicalColumn * Gw2MapShadowWindow.PageSize, page.PhysicalRow * Gw2MapShadowWindow.PageSize, Gw2MapShadowWindow.PageSize, Gw2MapShadowWindow.PageSize));
        if (_combined is not null) Combine();
    }

    private void Combine()
    {
        var program = _combineProgram!;
        var graphicsDevice = _device.GraphicsDevice;
        var previousTargets = _device.BoundTargets;
        var viewport = graphicsDevice.Viewport;
        _device.SetRenderTarget(_combined!);
        var bakedShadow = program.Parameters["BakedShadow"]!;
        bakedShadow.SetValue(Texture);
        program.Parameters["BakedRingOffset"]!.SetValue(new Vector2(_window.RingOffset.X, _window.RingOffset.Y));
        _device.ApplyPipeline(program, BlendState.Opaque, DepthStencilState.None, RasterizerState.CullNone, GrFullScreenTriangle.Layout);
        GrFullScreenTriangle.Draw(graphicsDevice);
        bakedShadow.SetValue((Texture?)null);
        _device.SetRenderTargets(previousTargets);
        graphicsDevice.Viewport = viewport;
    }

    private Vector2[] PageMoments(Gw2MapShadowPage page) =>
        page.Column < _shadow.TilesAcross && page.Row < _shadow.TilesDown
            ? _shadow.TileMoments(page.Row * _shadow.TilesAcross + page.Column, _filtersTiles)
            : _emptyPage;

    public void Dispose()
    {
        Texture.Dispose();
        _combined?.Dispose();
        _clearedDynamicShadow?.Dispose();
    }
}
