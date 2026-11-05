using System;
using System.Numerics;
using Crystal.Graphics;
using Gw2EnvironmentAmbientOcclusion = Gw2.Dat.Content.Gw2EnvironmentAmbientOcclusion;

namespace Gw2.Client.Graphics;

public sealed class GrAmbientOcclusion : IDisposable
{
    private readonly GrDevice _device;
    private readonly ShaderProgram _downsample;
    private readonly ShaderProgram _occlusionProgram;
    private readonly ShaderProgram _blurHorizontal;
    private readonly ShaderProgram _blurVertical;
    private readonly ShaderProgram _upsample;
    private Texture2D? _halfDepth;
    private Texture2D? _halfOcclusion;
    private Texture2D? _halfBlurred;
    private Texture2D? _occlusion;

    public GrAmbientOcclusion(GrDevice device)
    {
        _device = device;
        var library = device.LoadShader("GrAmbientOcclusion");
        _downsample = library.GetProgram("Downsample");
        _occlusionProgram = library.GetProgram("Occlusion");
        _blurHorizontal = library.GetProgram("BlurHorizontal");
        _blurVertical = library.GetProgram("BlurVertical");
        _upsample = library.GetProgram("Upsample");
    }

    public Texture2D? Occlusion => _occlusion;

    public static Vector4 PostLightConstantsOf(Gw2EnvironmentAmbientOcclusion values) => new(values.Amount, values.SunAmount, values.Brighten, 0f);

    public Texture2D Render(Texture2D linearDepth, Texture2D normalSpec, Matrix4x4 view, float cameraFar, Gw2EnvironmentAmbientOcclusion values)
    {
        EnsureTargets(linearDepth.Width, linearDepth.Height);
        var previousTargets = _device.BoundTargets;
        var worldToView = GrMatrix.ToRegisterRows(view);
        var linearClamp = _device.Sampler(SamplerDescription.LinearClamp);
        var pointClamp = _device.Sampler(SamplerDescription.PointClamp);

        var downsample = _downsample.Parameters;
        downsample["ssSource"]?.SetValue(linearDepth);
        downsample["ssSourceSampler"]?.SetValue(linearClamp);
        Draw(_downsample, _halfDepth!, cameraFar);

        var occlusion = _occlusionProgram.Parameters;
        occlusion["SsaoParams"]?.SetValue(new Vector4(0f, 0f, 0f, values.Contrast));
        occlusion["SsaoWorldToViewA"]?.SetValue(worldToView[0]);
        occlusion["SsaoWorldToViewB"]?.SetValue(worldToView[1]);
        occlusion["SsaoWorldToViewC"]?.SetValue(worldToView[2]);
        occlusion["ssDepthTex"]?.SetValue(_halfDepth);
        occlusion["ssDepthTexSampler"]?.SetValue(linearClamp);
        occlusion["ssNormalTex"]?.SetValue(normalSpec);
        occlusion["ssNormalTexSampler"]?.SetValue(pointClamp);
        Draw(_occlusionProgram, _halfOcclusion!, cameraFar);

        _blurHorizontal.Parameters["ssSource"]?.SetValue(_halfOcclusion);
        _blurHorizontal.Parameters["ssSourceSampler"]?.SetValue(linearClamp);
        Draw(_blurHorizontal, _halfBlurred!, cameraFar);

        _blurVertical.Parameters["ssSource"]?.SetValue(_halfBlurred);
        _blurVertical.Parameters["ssSourceSampler"]?.SetValue(linearClamp);
        Draw(_blurVertical, _halfOcclusion!, cameraFar);

        var upsample = _upsample.Parameters;
        upsample["ssDepthTex"]?.SetValue(linearDepth);
        upsample["ssDepthTexSampler"]?.SetValue(pointClamp);
        upsample["ssOcclusion"]?.SetValue(_halfOcclusion);
        upsample["ssOcclusionSampler"]?.SetValue(pointClamp);
        Draw(_upsample, _occlusion!, cameraFar);

        _device.SetRenderTargets(previousTargets);
        return _occlusion!;
    }

    private void Draw(ShaderProgram program, Texture2D target, float cameraFar)
    {
        _device.SetRenderTarget(target, null);
        program.Parameters["TexelOffset"]?.SetValue(GrEngineConstants.TexelOffset);
        program.Parameters["ScreenDimsInv"]?.SetValue(new Vector4(1f / target.Width, 1f / target.Height, 0f, cameraFar));
        _device.ApplyPipeline(program, BlendState.Opaque, DepthStencilState.None, RasterizerState.CullNone, GrFullScreenTriangle.Layout);
        GrFullScreenTriangle.Draw(_device.GraphicsDevice);
    }

    private void EnsureTargets(int width, int height)
    {
        if (_occlusion is { } existing && existing.Width == width && existing.Height == height) return;
        DisposeTargets();
        var halfWidth = Math.Max(width >> 1, 1);
        var halfHeight = Math.Max(height >> 1, 1);
        _halfDepth = _device.CreateRenderTarget(halfWidth, halfHeight, SurfaceFormat.R32Float, "AmbientOcclusionHalfDepth");
        _halfOcclusion = _device.CreateRenderTarget(halfWidth, halfHeight, SurfaceFormat.Rg16Unorm, "AmbientOcclusionHalf");
        _halfBlurred = _device.CreateRenderTarget(halfWidth, halfHeight, SurfaceFormat.Rg16Unorm, "AmbientOcclusionHalfBlurred");
        _occlusion = _device.CreateRenderTarget(width, height, SurfaceFormat.R16Float, "AmbientOcclusion");
    }

    private void DisposeTargets()
    {
        _halfDepth?.Dispose();
        _halfOcclusion?.Dispose();
        _halfBlurred?.Dispose();
        _occlusion?.Dispose();
    }

    public void Dispose() => DisposeTargets();
}
