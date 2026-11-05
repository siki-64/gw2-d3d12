using Gw2.Dat.Content;
using Gw2.Dat.Archive;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.DatSurvey.Tests;

[TestClass]
public sealed class Gw2RenderStateTests
{
    private const ulong Opaque = 0x0000000000000000;
    private const ulong AlphaBlend = 0x0000010006565000;
    private const ulong NormalBlend = 0x0000000006262000;
    private const ulong ScreenBlend = 0x0000000004242000;
    private const ulong Additive = 0x0000010002525000;
    private const ulong Modulate = 0x0000000003939000;

    [TestMethod]
    public void DecodesBlendFactorsFromTheBgfxStateLayout()
    {
        Assert.AreEqual((5, 6, 5, 6),
            (Gw2RenderState.SrcRgb(AlphaBlend), Gw2RenderState.DstRgb(AlphaBlend),
             Gw2RenderState.SrcAlpha(AlphaBlend), Gw2RenderState.DstAlpha(AlphaBlend)));
        Assert.AreEqual(1, Gw2RenderState.AlphaRef(AlphaBlend));
        Assert.IsTrue(Gw2RenderState.Blends(AlphaBlend));
        Assert.IsFalse(Gw2RenderState.Blends(Opaque));
    }

    [TestMethod]
    public void ClassifiesTheRecoveredBlendModes()
    {
        Assert.AreEqual(Gw2BlendMode.Opaque, Gw2RenderState.BlendMode(Opaque));
        Assert.AreEqual(Gw2BlendMode.Alpha, Gw2RenderState.BlendMode(AlphaBlend));
        Assert.AreEqual(Gw2BlendMode.Normal, Gw2RenderState.BlendMode(NormalBlend));
        Assert.AreEqual(Gw2BlendMode.Screen, Gw2RenderState.BlendMode(ScreenBlend));
        Assert.AreEqual(Gw2BlendMode.Additive, Gw2RenderState.BlendMode(Additive));
        Assert.AreEqual(Gw2BlendMode.Modulate, Gw2RenderState.BlendMode(Modulate));
    }

    [TestMethod]
    public void BlendedEffectsAreTransparentAndUnblendedCutoutsAreAlphaTest()
    {
        Assert.AreEqual("Opaque", Gw2RenderState.SurfaceClass(Opaque, alphaTested: false));
        Assert.AreEqual("AlphaTest", Gw2RenderState.SurfaceClass(Opaque, alphaTested: true));
        Assert.AreEqual("Transparent", Gw2RenderState.SurfaceClass(AlphaBlend, alphaTested: false));
        Assert.AreEqual("Transparent", Gw2RenderState.SurfaceClass(AlphaBlend, alphaTested: true));
    }
}
