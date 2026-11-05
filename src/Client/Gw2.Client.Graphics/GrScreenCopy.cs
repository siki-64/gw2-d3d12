using System;
using System.Numerics;
using Crystal.Graphics;

namespace Gw2.Client.Graphics;

public sealed class GrScreenCopy : IDisposable
{
    private readonly GrDevice _device;
    private readonly ShaderProgram _copy;
    private readonly ShaderProgram _gammaPresent;
    private readonly ShaderProgram _linearDepth;
    private Texture2D? _target;

    public GrScreenCopy(GrDevice device)
    {
        _device = device;
        var library = device.LoadShader("GrScreenCopy");
        _copy = library.GetProgram("Copy");
        _gammaPresent = library.GetProgram("GammaPresent");
        _linearDepth = library.GetProgram("LinearDepth");
    }

    public Texture2D Copy(Texture2D mainColor)
    {
        var previousTargets = _device.BoundTargets;
        EnsureTarget(mainColor.Width, mainColor.Height, mainColor.Format);
        CopyInto(_target, mainColor, SamplerDescription.PointClamp, _copy);
        _device.SetRenderTargets(previousTargets);
        return _target!;
    }

    public void Present(Texture2D source) => CopyInto(null, source, SamplerDescription.LinearClamp, _copy);

    public void PresentWithGamma(Texture2D source, float presentScale)
    {
        _gammaPresent.Parameters["Gamma"]!.SetValue(new Vector4(presentScale));
        CopyInto(null, source, SamplerDescription.LinearClamp, _gammaPresent);
    }

    public Texture2D LinearDepthTarget(int width, int height)
    {
        EnsureTarget(width, height, SurfaceFormat.R32Float);
        return _target!;
    }

    public Texture2D LinearDepth(Texture2D ndcDepth, float cameraNear, float cameraFar)
    {
        LinearDepthTarget(ndcDepth.Width, ndcDepth.Height);
        _linearDepth.Parameters["DepthToLinearDepth"]!.SetValue(new Vector4(-cameraNear * cameraFar / (cameraFar - cameraNear), -cameraFar / (cameraFar - cameraNear), 1f / cameraFar, 0f));
        CopyInto(_target, ndcDepth, SamplerDescription.PointClamp, _linearDepth);
        return _target!;
    }

    private void EnsureTarget(int width, int height, SurfaceFormat format)
    {
        if (_target is not null && _target.Width == width && _target.Height == height && _target.Format == format) return;
        _target?.Dispose();
        _target = _device.CreateRenderTarget(width, height, format, "ScreenCopy");
    }

    private void CopyInto(Texture2D? destination, Texture2D source, SamplerDescription sampler, ShaderProgram program)
    {
        if (destination is null) _device.SetBackBuffer();
        else _device.SetRenderTarget(destination);
        var sourceParameter = program.Parameters["Source"]!;
        sourceParameter.SetValue(source);
        program.Parameters["SourceSampler"]!.SetValue(_device.Sampler(sampler));
        _device.ApplyPipeline(program, BlendState.Opaque, DepthStencilState.None, RasterizerState.CullNone, GrFullScreenTriangle.Layout);
        GrFullScreenTriangle.Draw(_device.GraphicsDevice);
        sourceParameter.SetValue((Texture?)null);
    }

    public void Dispose() => _target?.Dispose();
}
