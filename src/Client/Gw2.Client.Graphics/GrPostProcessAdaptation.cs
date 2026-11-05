using System;
using System.Numerics;
using Crystal.Graphics;
using Gw2.Dat.Content;

namespace Gw2.Client.Graphics;

public sealed record GrPostProcessChain(
    Gw2EnvironmentLightAdaptation? LightAdaptation,
    float ElapsedSeconds,
    Gw2EnvironmentDepthBlur? DepthBlur,
    Texture2D? LinearDepth,
    float CameraFar,
    Vector2? LightRayScreenPosition = null)
{
    public const float LightRayScreenMargin = 0.135f;

    public bool RunsLightRays => LightRayScreenPosition is { } position
        && position.X >= -LightRayScreenMargin && position.X <= 1f + LightRayScreenMargin
        && position.Y >= -LightRayScreenMargin && position.Y <= 1f + LightRayScreenMargin;

    public bool RunsAnyPass => LightAdaptation is not null || DepthBlur is not null && LinearDepth is not null || RunsLightRays;
}

public sealed class GrPostProcessAdaptation : IDisposable
{
    private const int QuarterShift = 2;
    private const int SixteenthShift = 4;
    private const float AdaptationRateScale = 0.5f;
    private const float InstantRate = 1e6f;
    private const float MaximumAdaptationStepSeconds = 10f;
    private static readonly Vector4 LightRayParams = new(0.8197670f, 0.91f, 0.3f, 1f);

    private readonly GrDevice _device;
    private readonly ShaderProgram _downsample;
    private readonly ShaderProgram _luminosityAverage;
    private readonly ShaderProgram _toneMap;
    private readonly ShaderProgram _depthBlur;
    private readonly ShaderProgram _lightRayMask;
    private readonly ShaderProgram _lightRays;
    private readonly Texture2D[] _luminance = new Texture2D[2];
    private Texture2D? _quarter;
    private Texture2D? _sixteenth;
    private Texture2D? _chainFirst;
    private Texture2D? _chainSecond;
    private int _luminanceWriteIndex;
    private bool _adapted;

    public GrPostProcessAdaptation(GrDevice device)
    {
        _device = device;
        var library = device.LoadShader("GrPostProcessAdaptation");
        _downsample = library.GetProgram("Downsample");
        _luminosityAverage = library.GetProgram("LuminosityAverage");
        _toneMap = library.GetProgram("ToneMap");
        _depthBlur = library.GetProgram("DepthBlur");
        _lightRayMask = library.GetProgram("LightRayMask");
        _lightRays = library.GetProgram("LightRays");
        for (var index = 0; index < _luminance.Length; index++)
            _luminance[index] = device.CreateRenderTarget(1, 1, SurfaceFormat.Rgba16Float, $"AdaptedLuminance{index}");
    }

    public Texture2D Run(Texture2D scene, GrPostProcessChain chain)
    {
        EnsureTargets(scene.Width, scene.Height);
        var previousTargets = _device.BoundTargets;
        Texture2D source = scene;
        if (chain.LightAdaptation is { } lightAdaptation)
        {
            var target = NextTarget(source);
            ToneMap(source, target, lightAdaptation, chain.ElapsedSeconds);
            source = target;
        }
        else _adapted = false;
        if (chain is { DepthBlur: { } depthBlur, LinearDepth: { } linearDepth })
        {
            var target = NextTarget(source);
            BlurByDepth(source, target, linearDepth, depthBlur, chain.CameraFar);
            source = target;
        }
        if (chain.RunsLightRays)
        {
            var target = NextTarget(source);
            CastLightRays(scene, source, target, chain.LightRayScreenPosition!.Value);
            source = target;
        }
        _device.SetRenderTargets(previousTargets);
        return source;
    }

    private Texture2D NextTarget(Texture2D source) => source == _chainFirst ? _chainSecond! : _chainFirst!;

    private void ToneMap(Texture2D scene, Texture2D target, Gw2EnvironmentLightAdaptation adaptation, float elapsedSeconds)
    {
        var linearClamp = _device.Sampler(SamplerDescription.LinearClamp);
        _downsample.Parameters["DownsampleLevel"]?.SetValue(Vector4.Zero);
        Draw(_downsample, scene, linearClamp, null, linearClamp, _quarter!, InverseSize(scene));
        Draw(_downsample, _quarter!, linearClamp, null, linearClamp, _sixteenth!, InverseSize(_quarter!));

        var instant = !_adapted || elapsedSeconds > MaximumAdaptationStepSeconds;
        var average = _luminosityAverage.Parameters;
        average["Dim"]?.SetValue(new Vector4(AdaptationRate(elapsedSeconds, instant ? 0f : adaptation.DimTime)));
        average["Bright"]?.SetValue(new Vector4(AdaptationRate(elapsedSeconds, instant ? 0f : adaptation.BrightTime)));
        average["DarkMin"]?.SetValue(new Vector4(adaptation.DarkMin));
        average["DarkMax"]?.SetValue(new Vector4(adaptation.DarkMax));
        average["BrightMin"]?.SetValue(new Vector4(adaptation.BrightMin));
        average["BrightMax"]?.SetValue(new Vector4(adaptation.BrightMax));
        var previous = _luminance[_luminanceWriteIndex ^ 1];
        var current = _luminance[_luminanceWriteIndex];
        Draw(_luminosityAverage, _sixteenth!, linearClamp, previous, linearClamp, current, InverseSize(_sixteenth!));
        _luminanceWriteIndex ^= 1;
        _adapted = true;

        _toneMap.Parameters["DarkCurveCoeff"]?.SetValue(new Vector4(adaptation.DarkCoeff));
        _toneMap.Parameters["DarkCurveExponent"]?.SetValue(new Vector4(adaptation.DarkExp));
        Draw(_toneMap, scene, linearClamp, current, linearClamp, target, InverseSize(scene));
    }

    private static float AdaptationRate(float elapsedSeconds, float adaptationSeconds) =>
        adaptationSeconds <= 0f ? InstantRate : AdaptationRateScale * elapsedSeconds / adaptationSeconds;

    private void BlurByDepth(Texture2D source, Texture2D target, Texture2D linearDepth, Gw2EnvironmentDepthBlur depthBlur, float cameraFar)
    {
        _depthBlur.Parameters["DofParams"]?.SetValue(new Vector4(depthBlur.FocalDepth, depthBlur.FocalRange, 0f, 0f));
        var inverse = InverseSize(source);
        Draw(_depthBlur, source, _device.Sampler(SamplerDescription.LinearClamp), linearDepth, _device.Sampler(SamplerDescription.PointClamp), target, new Vector4(inverse.X, inverse.Y, 0f, cameraFar));
    }

    private void CastLightRays(Texture2D scene, Texture2D source, Texture2D target, Vector2 sunScreenPosition)
    {
        var linearClamp = _device.Sampler(SamplerDescription.LinearClamp);
        Draw(_lightRayMask, scene, _device.Sampler(SamplerDescription.PointClamp), null, linearClamp, _quarter!, InverseSize(scene));
        _lightRays.Parameters["LightRayParams"]?.SetValue(LightRayParams);
        _lightRays.Parameters["LightRayScreenPos"]?.SetValue(new Vector4(sunScreenPosition, 0f, 0f));
        Draw(_lightRays, source, linearClamp, _quarter!, linearClamp, target, InverseSize(source));
    }

    private static Vector4 InverseSize(Texture2D texture) => new(1f / texture.Width, 1f / texture.Height, 0f, 0f);

    private void Draw(ShaderProgram program, Texture2D source, SamplerState sourceSampler, Texture2D? second, SamplerState secondSampler, Texture2D target, Vector4 screenDimsInv)
    {
        _device.SetRenderTarget(target, null);
        var parameters = program.Parameters;
        parameters["ScreenDimsInv"]?.SetValue(screenDimsInv);
        parameters["Source"]?.SetValue(source);
        parameters["SourceSampler"]?.SetValue(sourceSampler);
        parameters["Second"]?.SetValue(second);
        parameters["SecondSampler"]?.SetValue(secondSampler);
        _device.ApplyPipeline(program, BlendState.Opaque, DepthStencilState.None, RasterizerState.CullNone, GrFullScreenTriangle.Layout);
        GrFullScreenTriangle.Draw(_device.GraphicsDevice);
    }

    private void EnsureTargets(int width, int height)
    {
        if (_chainFirst is not null && _chainFirst.Width == width && _chainFirst.Height == height) return;
        DisposeSizedTargets();
        _chainFirst = _device.CreateRenderTarget(width, height, SurfaceFormat.Rgba8Unorm, "PostProcessChainFirst");
        _chainSecond = _device.CreateRenderTarget(width, height, SurfaceFormat.Rgba8Unorm, "PostProcessChainSecond");
        _quarter = _device.CreateRenderTarget(Math.Max(width >> QuarterShift, 1), Math.Max(height >> QuarterShift, 1), SurfaceFormat.Rgba8Unorm, "PostProcessQuarter");
        _sixteenth = _device.CreateRenderTarget(Math.Max(width >> SixteenthShift, 1), Math.Max(height >> SixteenthShift, 1), SurfaceFormat.Rgba8Unorm, "PostProcessSixteenth");
        _adapted = false;
    }

    private void DisposeSizedTargets()
    {
        _chainFirst?.Dispose();
        _chainSecond?.Dispose();
        _quarter?.Dispose();
        _sixteenth?.Dispose();
    }

    public void Dispose()
    {
        DisposeSizedTargets();
        foreach (var luminance in _luminance) luminance.Dispose();
    }
}
