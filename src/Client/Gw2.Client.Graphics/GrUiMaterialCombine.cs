using Gw2.Dat.Content;
using Gw2.Formats;

namespace Gw2.Client.Graphics;

public readonly record struct GrUiMaterialCombine(int UiShader, ulong RenderStateFlags)
{
    public const int TextureSlotCount = 4;
    public const int PlainUiShader = 3;
    public const int CompassCellShader = 256;

    public const ulong AlphaBlendedRenderState = 0x0000_0100_0656_5000;
    public const ulong InverseSourceColorRenderState = 0x0000_0000_0424_2000;
    public const ulong ModulateTwiceRenderState = 0x0000_0000_0393_9000;
    public const ulong MultiplyRenderState = 0x0000_0000_0191_9000;

    public static readonly uint ControlToken = Token.Encode("control");
    public static readonly uint[] TexTransformRowTokens = Gw2.Dat.Content.Gw2ModelUvAnimationSampler.TexTransformRowTokens;

    public Gw2AmatRenderState RenderState => new(RenderStateFlags);

    public static GrUiMaterialCombine ForUiShader(int uiShader) => new(uiShader, uiShader switch
    {
        0 or 32 or 42 => InverseSourceColorRenderState,
        43 or 47 or 48 => ModulateTwiceRenderState,
        50 => MultiplyRenderState,
        _ => AlphaBlendedRenderState,
    });
}
