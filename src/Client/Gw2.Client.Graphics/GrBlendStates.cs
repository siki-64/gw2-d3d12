using System.Collections.Concurrent;
using Crystal.Graphics;
using Gw2.Dat.Content;

namespace Gw2.Client.Graphics;

public static class GrBlendStates
{
    private static readonly ConcurrentDictionary<(ulong, Gw2ColorWrite), BlendState> StateByFlags = new();

    public static readonly BlendState AdditiveOneOne = new(new RenderTargetBlendDescription
    {
        BlendEnable = true,
        SourceBlend = Blend.One,
        DestinationBlend = Blend.One,
        SourceBlendAlpha = Blend.One,
        DestinationBlendAlpha = Blend.One,
    });

    public static readonly BlendState ReverseSubtractOneOne = new(new RenderTargetBlendDescription
    {
        BlendEnable = true,
        SourceBlend = Blend.One,
        DestinationBlend = Blend.One,
        BlendOperation = BlendOperation.ReverseSubtract,
        SourceBlendAlpha = Blend.One,
        DestinationBlendAlpha = Blend.One,
        BlendOperationAlpha = BlendOperation.ReverseSubtract,
    });

    public static BlendState From(Gw2AmatRenderState state) => From(state, Gw2ColorWrite.All);

    public static BlendState From(Gw2AmatRenderState state, Gw2ColorWrite colorWrite) => !state.IsBlended && colorWrite == Gw2ColorWrite.All
        ? BlendState.Opaque
        : StateByFlags.GetOrAdd((state.IsBlended ? state.Flags : 0, colorWrite), _ => new BlendState(state.IsBlended
            ? new RenderTargetBlendDescription
            {
                BlendEnable = true,
                SourceBlend = BlendOf(state.ColorSource),
                DestinationBlend = BlendOf(state.ColorDestination),
                BlendOperation = OperationOf(state.ColorEquation),
                SourceBlendAlpha = AlphaBlendOf(state.AlphaSource),
                DestinationBlendAlpha = AlphaBlendOf(state.AlphaDestination),
                BlendOperationAlpha = OperationOf(state.AlphaEquation),
                WriteMask = GrDrawStates.ColorWriteChannelsOf(colorWrite),
            }
            : new RenderTargetBlendDescription { WriteMask = GrDrawStates.ColorWriteChannelsOf(colorWrite) }));

    private static Blend BlendOf(Gw2BlendFactor factor) => factor switch
    {
        Gw2BlendFactor.Zero => Blend.Zero,
        Gw2BlendFactor.SourceColor => Blend.SourceColor,
        Gw2BlendFactor.InverseSourceColor => Blend.InverseSourceColor,
        Gw2BlendFactor.SourceAlpha => Blend.SourceAlpha,
        Gw2BlendFactor.InverseSourceAlpha => Blend.InverseSourceAlpha,
        Gw2BlendFactor.DestinationAlpha => Blend.DestinationAlpha,
        Gw2BlendFactor.InverseDestinationAlpha => Blend.InverseDestinationAlpha,
        Gw2BlendFactor.DestinationColor => Blend.DestinationColor,
        Gw2BlendFactor.InverseDestinationColor => Blend.InverseDestinationColor,
        Gw2BlendFactor.SourceAlphaSaturate => Blend.SourceAlphaSaturate,
        Gw2BlendFactor.BlendFactor => Blend.BlendFactor,
        Gw2BlendFactor.InverseBlendFactor => Blend.InverseBlendFactor,
        _ => Blend.One,
    };

    private static Blend AlphaBlendOf(Gw2BlendFactor factor) => BlendOf(factor) switch
    {
        Blend.SourceColor => Blend.SourceAlpha,
        Blend.InverseSourceColor => Blend.InverseSourceAlpha,
        Blend.DestinationColor => Blend.DestinationAlpha,
        Blend.InverseDestinationColor => Blend.InverseDestinationAlpha,
        var blend => blend,
    };

    private static BlendOperation OperationOf(Gw2BlendEquation equation) => equation switch
    {
        Gw2BlendEquation.Subtract => BlendOperation.Subtract,
        Gw2BlendEquation.ReverseSubtract => BlendOperation.ReverseSubtract,
        Gw2BlendEquation.Min => BlendOperation.Min,
        Gw2BlendEquation.Max => BlendOperation.Max,
        _ => BlendOperation.Add,
    };
}
