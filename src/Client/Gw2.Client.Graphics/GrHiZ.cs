using System;
using System.Runtime.InteropServices;
using Crystal.Graphics;

namespace Gw2.Client.Graphics;

public sealed class GrHiZ : IDisposable
{
    private const int ThreadsPerGroupSide = 8;

    [StructLayout(LayoutKind.Sequential)]
    private struct GrHiZReduceConstants
    {
        public uint SourceWidth;
        public uint SourceHeight;
        public uint TargetWidth;
        public uint TargetHeight;
    }

    private readonly GrDevice _device;
    private readonly ShaderProgram _firstLevelProgram;
    private readonly ShaderProgram _reduceProgram;
    private readonly ComputePipeline _firstLevel;
    private readonly ComputePipeline _reduce;
    private Texture2D? _pyramid;

    public GrHiZ(GrDevice device)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _firstLevelProgram = device.LoadShader("GrHiZFirstLevel").GetProgram("HiZFirstLevel");
        _reduceProgram = device.LoadShader("GrHiZReduce").GetProgram("HiZReduce");
        _firstLevel = new ComputePipeline(device.GraphicsDevice, _firstLevelProgram, "HiZFirstLevel");
        _reduce = new ComputePipeline(device.GraphicsDevice, _reduceProgram, "HiZReduce");
    }

    public Texture2D? Pyramid => _pyramid;

    public int DepthWidth { get; private set; }

    public int DepthHeight { get; private set; }

    public void EnsurePyramid(int depthWidth, int depthHeight)
    {
        DepthWidth = depthWidth;
        DepthHeight = depthHeight;
        var width = Math.Max(1, depthWidth / 2);
        var height = Math.Max(1, depthHeight / 2);
        if (_pyramid is not null && _pyramid.Width == width && _pyramid.Height == height) return;
        _pyramid?.Dispose();
        _pyramid = _device.CreateRenderTarget(width, height, SurfaceFormat.R32Float, "HiZPyramid", mipLevels: 0, unorderedAccess: true);
    }

    public void Build(Texture2D depth)
    {
        ArgumentNullException.ThrowIfNull(depth);
        EnsurePyramid(depth.Width, depth.Height);
        var width = _pyramid!.Width;
        var height = _pyramid.Height;

        _firstLevelProgram.ConstantBuffers["Reduce"]!.SetValue(new GrHiZReduceConstants { SourceWidth = (uint)depth.Width, SourceHeight = (uint)depth.Height, TargetWidth = (uint)width, TargetHeight = (uint)height });
        _firstLevelProgram.Parameters["Depth"]!.SetValue(depth);
        _firstLevelProgram.Parameters["Target"]!.SetValue(_pyramid.GetMipView(0));
        Dispatch(_firstLevel, width, height);
        _firstLevelProgram.Parameters["Depth"]!.SetValue((Texture?)null);

        var reduce = _reduceProgram.Parameters;
        for (var level = 1; level < _pyramid.MipLevels; level++)
        {
            var (sourceWidth, sourceHeight) = (width, height);
            (width, height) = (Math.Max(1, width / 2), Math.Max(1, height / 2));
            _reduceProgram.ConstantBuffers["Reduce"]!.SetValue(new GrHiZReduceConstants { SourceWidth = (uint)sourceWidth, SourceHeight = (uint)sourceHeight, TargetWidth = (uint)width, TargetHeight = (uint)height });
            reduce["Source"]!.SetValue(_pyramid.GetMipView(level - 1));
            reduce["Target"]!.SetValue(_pyramid.GetMipView(level));
            Dispatch(_reduce, width, height);
        }
        reduce["Source"]!.SetValue((Texture?)null);
        reduce["Target"]!.SetValue((Texture?)null);
    }

    private void Dispatch(ComputePipeline pipeline, int width, int height) =>
        _device.GraphicsDevice.Dispatch(pipeline, (width + ThreadsPerGroupSide - 1) / ThreadsPerGroupSide, (height + ThreadsPerGroupSide - 1) / ThreadsPerGroupSide);

    public void Dispose()
    {
        _pyramid?.Dispose();
        _firstLevel.Dispose();
        _reduce.Dispose();
    }
}
