using Crystal.Graphics;

namespace Gw2.Client.Graphics;

public sealed class GrDepthCoverageAlpha
{
    private static readonly BlendState AlphaWriteOnly = new(new RenderTargetBlendDescription { WriteMask = ColorWriteChannels.Alpha });
    private static readonly DepthStencilState InFrontOfFarPlane = new() { DepthFunction = ComparisonFunction.Greater, DepthWriteEnable = false };
    private readonly GrDevice _device;
    private readonly ShaderProgram _cover;

    public GrDepthCoverageAlpha(GrDevice device)
    {
        _device = device;
        _cover = device.LoadShader("GrDepthCoverageAlpha").GetProgram("Cover");
    }

    public void MarkCoveredOpaque()
    {
        _device.ApplyPipeline(_cover, AlphaWriteOnly, InFrontOfFarPlane, RasterizerState.CullNone, GrFullScreenTriangle.Layout);
        GrFullScreenTriangle.Draw(_device.GraphicsDevice);
    }
}
