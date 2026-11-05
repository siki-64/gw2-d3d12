using System;
using System.Collections.Concurrent;
using Crystal.Graphics;
using Gw2.Dat.Content;

namespace Gw2.Client.Graphics;

public static class GrDrawStates
{
    private static readonly ConcurrentDictionary<(Gw2DepthTest, bool), DepthStencilState> DepthStencilByTest = new();

    public static DepthStencilState DepthStencil(Gw2AmatDrawState state) => (state.DepthTest, state.DepthWrite) switch
    {
        (Gw2DepthTest.Disabled, _) => DepthStencilState.None,
        (Gw2DepthTest.LessEqual, true) => DepthStencilState.Default,
        (Gw2DepthTest.LessEqual, false) => DepthStencilState.DepthRead,
        var key => DepthStencilByTest.GetOrAdd(key, _ => new DepthStencilState
        {
            DepthEnable = true,
            DepthWriteEnable = state.DepthWrite,
            DepthFunction = state.DepthTest switch
            {
                Gw2DepthTest.Greater => ComparisonFunction.Greater,
                Gw2DepthTest.Always => ComparisonFunction.Always,
                _ => ComparisonFunction.Equal,
            },
        }),
    };

    public static ColorWriteChannels ColorWriteChannelsOf(Gw2ColorWrite colorWrite) =>
        (ColorWriteChannels)(int)colorWrite;

    public static RasterizerState WithDepthBias(RasterizerState rasterizerState, Gw2AmatDrawState state)
    {
        ArgumentNullException.ThrowIfNull(rasterizerState);
        if (state.DepthBias == 0f) return rasterizerState;
        return state.SlopeScaledDepthBias
            ? rasterizerState with { SlopeScaledDepthBias = state.DepthBias }
            : rasterizerState with { DepthBias = (int)MathF.Round(state.DepthBias, MidpointRounding.AwayFromZero) };
    }
}
