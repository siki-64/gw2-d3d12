using System;
using System.Numerics;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Graphics.Tests;

[TestClass]
public sealed class GrTextureTransformAnimationTests
{
    private const uint TexTransform0A = 0x3674487D;
    private const uint TexTransform0B = 0x3674487E;
    private const uint TexTransform1A = 0x3F4721AE;

    private static Gw2ModelTextureTransform Scrolling(Vector2 scroll, byte uvIndex = 0) =>
        new(0, uvIndex, 1, 1, 1, 0f, scroll, Vector2.Zero, Vector2.Zero, Vector2.Zero, 0f);

    [TestMethod]
    public void ConstantScrollMovesOffsetLinearlyWithTime()
    {
        var animation = GrTextureTransformAnimation.For([Scrolling(new(0.25f, -0.5f))])!;

        Assert.IsTrue(animation.TryGetInput(TexTransform0A, 2f, out var rowA));
        Assert.IsTrue(animation.TryGetInput(TexTransform0B, 2f, out var rowB));
        Assert.AreEqual(new Vector4(1f, 0f, 0f, 0.5f), rowA);
        Assert.AreEqual(new Vector4(0f, 1f, 0f, -1f), rowB);
    }

    [TestMethod]
    public void TransformLandsOnItsTextureInput()
    {
        var animation = GrTextureTransformAnimation.For([Scrolling(new(1f, 0f), uvIndex: 1)])!;

        Assert.IsTrue(animation.TryGetInput(TexTransform1A, 1f, out _));
        Assert.IsFalse(animation.TryGetInput(TexTransform0A, 1f, out _));
    }

    [TestMethod]
    public void FlipbookSelectsCellFromFramesPerSecond()
    {
        var flipbook = new Gw2ModelTextureTransform(0, 0, 4, 2, 8, 2f, Vector2.Zero, Vector2.Zero, Vector2.Zero, Vector2.Zero, 0f);
        var animation = GrTextureTransformAnimation.For([flipbook])!;

        animation.TryGetInput(TexTransform0A, 2.75f, out var rowA);
        animation.TryGetInput(TexTransform0B, 2.75f, out var rowB);

        Assert.AreEqual(0.25f, rowA.W, 1e-6f);
        Assert.AreEqual(0.5f, rowB.W, 1e-6f);
    }

    [TestMethod]
    public void StaticTransformsAndMissingColumnsDoNotAnimate()
    {
        var still = new Gw2ModelTextureTransform(0, 0, 1, 1, 1, 0f, Vector2.Zero, Vector2.Zero, Vector2.Zero, Vector2.Zero, 0f);
        var noColumns = Scrolling(new(1f, 0f)) with { Columns = 0 };

        Assert.IsNull(GrTextureTransformAnimation.For([still, noColumns]));
    }

    [TestMethod]
    public void RotationTurnsAboutTextureCentre()
    {
        var spinning = new Gw2ModelTextureTransform(0, 0, 1, 1, 1, 0f, Vector2.Zero, Vector2.Zero, Vector2.Zero, Vector2.Zero, MathF.PI / 2f);
        var animation = GrTextureTransformAnimation.For([spinning])!;

        animation.TryGetInput(TexTransform0A, 1f, out var rowA);
        animation.TryGetInput(TexTransform0B, 1f, out var rowB);
        var centre = new Vector4(0.5f, 0.5f, 0f, 1f);

        Assert.AreEqual(0.5f, Vector4.Dot(rowA, centre), 1e-5f);
        Assert.AreEqual(0.5f, Vector4.Dot(rowB, centre), 1e-5f);
    }
}
