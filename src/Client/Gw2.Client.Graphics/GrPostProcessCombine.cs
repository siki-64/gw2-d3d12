using System;
using System.Numerics;
using Crystal;
using Crystal.Graphics;
using Gw2.Dat.Content;

namespace Gw2.Client.Graphics;

public sealed class GrPostProcessCombine : IDisposable
{
    private const int BloomTargetShift = 2;
    private const int DistortionGridSide = 40;

    public float BlurStep => _device.RenderScale;

    public float Gamma { get; set; } = GrGamma.Neutral;

    public Vector4? Distortion { get; set; }

    private readonly GrDevice _device;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly ShaderProgram _combine;
    private readonly ShaderProgram _distortedCombine;
    private readonly ShaderProgram _downsampleIntensity;
    private readonly ShaderProgram _blurHorizontal;
    private readonly ShaderProgram _blurVertical;
    private readonly Texture2D _noBloom;
    private readonly GrScreenCopy _screenCopy;
    private Texture2D? _scene;
    private Texture2D? _sceneDepth;
    private Texture2D? _combined;
    private Texture2D? _bloomTarget;
    private Texture2D? _bloomBlurTarget;
    private Texture2D? _colorLookupTexture;
    private Gw2ColorLookup? _uploadedColorLookup;
    private GrPhysicalBloom? _physicalBloom;
    private GrPostProcessAdaptation? _adaptation;
    private GraphicsBuffer? _distortionGridVertices;
    private GraphicsBuffer? _distortionGridIndices;

    public GrPostProcessCombine(GrDevice device)
    {
        _device = device;
        _graphicsDevice = device.GraphicsDevice;
        var combine = device.LoadShader("GrPostProcessCombine");
        _combine = combine.GetProgram("PostProcessCombine");
        _distortedCombine = combine.GetProgram("DistortedPostProcessCombine");
        var bloom = device.LoadShader("GrPostProcessBloom");
        _downsampleIntensity = bloom.GetProgram("DownsampleIntensity");
        _blurHorizontal = bloom.GetProgram("BlurHorizontal");
        _blurVertical = bloom.GetProgram("BlurVertical");
        _screenCopy = new GrScreenCopy(device);
        _noBloom = new Texture2D(_graphicsDevice, new TextureDescription { Width = 1, Height = 1, Format = SurfaceFormat.Rgba8Unorm, Name = "NoBloom" });
        _noBloom.SetData<Color>([Color.Black]);
    }

    public Color SceneClearColor { get; init; }

    public bool PhysicalBloom
    {
        get => _physicalBloom is not null;
        set
        {
            if (value == PhysicalBloom) return;
            if (value) _physicalBloom = new GrPhysicalBloom(_device);
            else
            {
                _physicalBloom!.Dispose();
                _physicalBloom = null;
            }
        }
    }

    public void BeginScene()
    {
        var (width, height) = _device.RenderTargetSize();
        if (_scene is null || _scene.Width != width || _scene.Height != height)
        {
            _scene?.Dispose();
            _sceneDepth?.Dispose();
            _combined?.Dispose();
            _bloomTarget?.Dispose();
            _bloomBlurTarget?.Dispose();
            _combined = null;
            _scene = _device.CreateRenderTarget(width, height, SurfaceFormat.Rgba8Unorm, "Scene", clearColor: SceneClearColor);
            _sceneDepth = _device.CreateDepthTarget(width, height, "SceneTargetDepth");
            var bloomWidth = Math.Max(width >> BloomTargetShift, 1);
            var bloomHeight = Math.Max(height >> BloomTargetShift, 1);
            _bloomTarget = _device.CreateRenderTarget(bloomWidth, bloomHeight, SurfaceFormat.Rgba8Unorm, "Bloom");
            _bloomBlurTarget = _device.CreateRenderTarget(bloomWidth, bloomHeight, SurfaceFormat.Rgba8Unorm, "BloomBlur");
        }
        var presentsDirectly = width == _graphicsDevice.BackBufferWidth && height == _graphicsDevice.BackBufferHeight && Gamma == GrGamma.Neutral;
        if (presentsDirectly)
        {
            _combined?.Dispose();
            _combined = null;
        }
        else _combined ??= _device.CreateRenderTarget(width, height, SurfaceFormat.Rgba8Unorm, "Combined");
        _device.SetRenderTarget(_scene, _sceneDepth);
    }

    public void Combine(Gw2EnvironmentGrade? environmentGrade, Gw2EnvironmentBloom? environmentBloom, Gw2ColorLookup? colorLookup, GrPostProcessChain chain)
    {
        if (!chain.RunsAnyPass)
        {
            Combine(environmentGrade, environmentBloom, colorLookup);
            return;
        }
        _adaptation ??= new GrPostProcessAdaptation(_device);
        Combine(environmentGrade, environmentBloom, colorLookup, _adaptation.Run(_scene!, chain));
    }

    public Texture2D CopyScene() => _screenCopy.Copy(_scene!);

    public void Combine(Gw2EnvironmentGrade? environmentGrade, Gw2EnvironmentBloom? environmentBloom, Gw2ColorLookup? colorLookup = null) => Combine(environmentGrade, environmentBloom, colorLookup, _scene!);

    private void Combine(Gw2EnvironmentGrade? environmentGrade, Gw2EnvironmentBloom? environmentBloom, Gw2ColorLookup? colorLookup, Texture2D chainedScene)
    {
        var grade = environmentGrade is { IsActive: true } ? environmentGrade : Gw2EnvironmentGrade.Neutral;
        var bloom = environmentBloom is { IsActive: true } ? environmentBloom : null;
        Texture2D bloomTexture = _noBloom;
        if (bloom is not null) bloomTexture = _physicalBloom?.Render(_scene!, bloom) ?? DownAndBlur(bloom);

        if (_combined is null) _device.SetBackBuffer();
        else _device.SetRenderTarget(_combined, null);
        var distorted = Distortion is { X: > 0f };
        var program = distorted ? _distortedCombine : _combine;
        var parameters = program.Parameters;
        parameters["UseColorLookup"]?.SetValue(colorLookup is null ? 0f : 1f);
        parameters["ColorLookup"]?.SetValue(colorLookup is null ? _noBloom : UploadColorLookup(colorLookup));
        parameters["SaturationConstant"]?.SetValue(grade.SaturationConstant);
        parameters["TintTargetConstant"]?.SetValue(grade.TintTargetConstant);
        parameters["TintConstant"]?.SetValue(grade.TintConstant);
        parameters["BloomAmountConstant"]?.SetValue(bloom?.BloomAmountConstant ?? Vector4.Zero);
        parameters["Scene"]?.SetValue(chainedScene);
        parameters["SceneSampler"]?.SetValue(_device.Sampler(SamplerDescription.PointClamp));
        parameters["Bloom"]?.SetValue(bloomTexture);
        parameters["BloomSampler"]?.SetValue(_device.Sampler(SamplerDescription.LinearClamp));
        parameters["ColorLookupSampler"]?.SetValue(_device.Sampler(SamplerDescription.LinearClamp));
        if (distorted) DrawDistortedGrid(Distortion!.Value);
        else
        {
            _device.ApplyPipeline(_combine, BlendState.Opaque, DepthStencilState.None, RasterizerState.CullNone, GrFullScreenTriangle.Layout);
            GrFullScreenTriangle.Draw(_graphicsDevice);
        }
        if (_combined is not null) _screenCopy.PresentWithGamma(_combined, GrGamma.PresentScale(Gamma));
    }

    private Texture2D UploadColorLookup(Gw2ColorLookup colorLookup)
    {
        _colorLookupTexture ??= new Texture2D(_graphicsDevice, new TextureDescription { Width = Gw2ColorLookup.Width, Height = Gw2ColorLookup.Height, Format = SurfaceFormat.Rgba8Unorm, Name = "ColorLookup" });
        if (!ReferenceEquals(colorLookup, _uploadedColorLookup)) _colorLookupTexture.SetData<byte>(colorLookup.Rgba);
        _uploadedColorLookup = colorLookup;
        return _colorLookupTexture;
    }

    private Texture2D DownAndBlur(Gw2EnvironmentBloom bloom)
    {
        var previousTargets = _device.BoundTargets;
        var downsample = _downsampleIntensity.Parameters;
        downsample["ScreenDimsInv"]?.SetValue(new Vector4(1f / _scene!.Width, 1f / _scene.Height, 0f, 0f));
        downsample["DownsampleLevel"]?.SetValue(bloom.DownsampleLevelConstant);
        Draw(_downsampleIntensity, _scene, _bloomTarget!);
        Blur(bloom, BlurStep);
        Blur(bloom, BlurStep + BlurStep);
        _device.SetRenderTargets(previousTargets);
        return _bloomTarget!;
    }

    private void Blur(Gw2EnvironmentBloom bloom, float step)
    {
        var blurParams = new Vector4(1f / _bloomTarget!.Width, 1f / _bloomTarget.Height, step, bloom.BlurGain);
        _blurHorizontal.Parameters["BlurParams"]?.SetValue(blurParams);
        _blurVertical.Parameters["BlurParams"]?.SetValue(blurParams);
        Draw(_blurHorizontal, _bloomTarget, _bloomBlurTarget!);
        Draw(_blurVertical, _bloomBlurTarget!, _bloomTarget);
    }

    private void Draw(ShaderProgram program, Texture2D source, Texture2D target)
    {
        _device.SetRenderTarget(target, null);
        program.Parameters["Source"]?.SetValue(source);
        program.Parameters["SourceSampler"]?.SetValue(_device.Sampler(SamplerDescription.LinearClamp));
        _device.ApplyPipeline(program, BlendState.Opaque, DepthStencilState.None, RasterizerState.CullNone, GrFullScreenTriangle.Layout);
        GrFullScreenTriangle.Draw(_graphicsDevice);
    }

    private void DrawDistortedGrid(Vector4 distortion)
    {
        _distortedCombine.Parameters["DistortionConstant"]?.SetValue(distortion);
        _distortionGridVertices ??= GrBuffers.Vertex<Vector3>(_graphicsDevice, DistortionGridPoints(), "DistortionGridVertices");
        _distortionGridIndices ??= GrBuffers.Index(_graphicsDevice, DistortionGridTriangles(), "DistortionGridIndices");
        _device.ApplyPipeline(_distortedCombine, BlendState.Opaque, DepthStencilState.None, RasterizerState.CullNone, GrFullScreenTriangle.Layout);
        _graphicsDevice.SetVertexBuffer(_distortionGridVertices);
        _graphicsDevice.SetIndexBuffer(_distortionGridIndices);
        _graphicsDevice.DrawIndexed(6 * (DistortionGridSide - 1) * (DistortionGridSide - 1));
    }

    private static Vector3[] DistortionGridPoints()
    {
        var points = new Vector3[DistortionGridSide * DistortionGridSide];
        var lastIndex = DistortionGridSide - 1f;
        for (var row = 0; row < DistortionGridSide; row++)
            for (var column = 0; column < DistortionGridSide; column++)
                points[row * DistortionGridSide + column] = new Vector3(column / lastIndex, row / lastIndex, 0f);
        return points;
    }

    private static ushort[] DistortionGridTriangles()
    {
        var indices = new ushort[6 * (DistortionGridSide - 1) * (DistortionGridSide - 1)];
        var next = 0;
        for (var row = 0; row < DistortionGridSide - 1; row++)
            for (var column = 0; column < DistortionGridSide - 1; column++)
            {
                var corner = (ushort)(row * DistortionGridSide + column);
                indices[next++] = corner;
                indices[next++] = (ushort)(corner + 1);
                indices[next++] = (ushort)(corner + DistortionGridSide);
                indices[next++] = (ushort)(corner + DistortionGridSide);
                indices[next++] = (ushort)(corner + 1);
                indices[next++] = (ushort)(corner + DistortionGridSide + 1);
            }
        return indices;
    }

    public void Dispose()
    {
        _scene?.Dispose();
        _sceneDepth?.Dispose();
        _combined?.Dispose();
        _bloomTarget?.Dispose();
        _bloomBlurTarget?.Dispose();
        _noBloom.Dispose();
        _physicalBloom?.Dispose();
        _adaptation?.Dispose();
        _colorLookupTexture?.Dispose();
        _distortionGridVertices?.Dispose();
        _distortionGridIndices?.Dispose();
        _screenCopy.Dispose();
    }
}
