using Crystal.Graphics;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Graphics.Tests;

[TestClass]
public sealed class GrPipelineStatesTests
{
    private const uint MinifyPoint = 1u << 6;
    private const uint MagnifyAnisotropic = 2u << 8;
    private const uint MipPoint = 1u << 10;
    private const uint AddressUClamp = 2u << 0;

    [TestMethod]
    public void ConstantDepthBiasRoundsAwayFromZeroInDepthUnits()
    {
        var biased = GrDrawStates.WithDepthBias(RasterizerState.CullBack, new Gw2AmatDrawState(Gw2DepthTest.LessEqual, true, Gw2ColorWrite.All, -2.5f, false));
        Assert.AreEqual(-3, biased.DepthBias);
        Assert.AreEqual(0f, biased.SlopeScaledDepthBias);
        Assert.AreEqual(CullMode.Back, biased.CullMode);
    }

    [TestMethod]
    public void SlopeScaledDepthBiasKeepsTheConstantBias()
    {
        var biased = GrDrawStates.WithDepthBias(RasterizerState.CullNone, new Gw2AmatDrawState(Gw2DepthTest.LessEqual, true, Gw2ColorWrite.All, 1.5f, true));
        Assert.AreEqual(0, biased.DepthBias);
        Assert.AreEqual(1.5f, biased.SlopeScaledDepthBias);
        Assert.AreEqual(CullMode.None, biased.CullMode);
    }

    [TestMethod]
    public void SamplerFiltersSplitPerStageAndAnisotropyOnlyWhenRequested()
    {
        var pointMinify = GrSamplerStates.From(new Gw2AmatSamplerState(MinifyPoint | MipPoint | AddressUClamp));
        Assert.AreEqual(FilterMode.Point, pointMinify.MinFilter);
        Assert.AreEqual(FilterMode.Linear, pointMinify.MagFilter);
        Assert.AreEqual(FilterMode.Point, pointMinify.MipFilter);
        Assert.AreEqual(GrSamplerStates.DefaultMaxAnisotropy, pointMinify.MaxAnisotropy);
        Assert.AreEqual(TextureAddressMode.Clamp, pointMinify.AddressU);
        Assert.AreEqual(TextureAddressMode.Wrap, pointMinify.AddressV);

        var anisotropic = GrSamplerStates.From(new Gw2AmatSamplerState(MagnifyAnisotropic));
        Assert.AreEqual(GrSamplerStates.DeviceMaxAnisotropy, anisotropic.MaxAnisotropy);
        Assert.AreEqual(FilterMode.Linear, anisotropic.MinFilter);
    }
}
