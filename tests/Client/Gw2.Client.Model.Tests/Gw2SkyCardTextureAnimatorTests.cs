using Gw2.Formats;
using System;
using System.IO;
using System.Numerics;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2SkyCardTextureAnimatorTests
{
    private const uint SkyCardMapFileId = 1839188;

    [TestMethod]
    public void ScrollWrapsAndScaleBouncesInsideItsRange()
    {
        var animation = new Gw2MapEnvSkyCardAnimation(new Vector2(0.3f, 0f), new Vector2(0.5f, 1.2f), new Vector2(1f, 1f), new Vector2(0.4f, 0f), 0f, 1);
        var animator = new Gw2SkyCardTextureAnimator(animation);

        animator.Advance(1f);
        Assert.AreEqual(1.2f, animator.Transform.RowA.X, 1e-5f);
        Assert.AreEqual(-0.3f, animator.Transform.RowA.W, 1e-5f);

        animator.Advance(1f);
        Assert.AreEqual(0.8f, animator.Transform.RowA.X, 1e-5f);
        Assert.AreEqual(0.2f, animator.Transform.RowA.W, 1e-5f);

        animator.Advance(2f);
        Assert.AreEqual(0.5f, animator.Transform.RowA.X, 1e-5f);
        Assert.AreEqual(-0.05f, animator.Transform.RowA.W, 1e-5f);
        Assert.AreEqual(new Vector4(0f, 1f, 0f, -0.5f), animator.Transform.RowB);
    }

    [TestMethod]
    public void RotationTurnsTheTextureCentreOntoTheScrollOffset()
    {
        var animation = new Gw2MapEnvSkyCardAnimation(Vector2.Zero, Vector2.One, Vector2.One, Vector2.Zero, MathF.PI / 2f, 2);
        var animator = new Gw2SkyCardTextureAnimator(animation);

        animator.Advance(1f);
        var (rowA, rowB) = animator.Transform;
        var centre = new Vector4(0.5f, 0.5f, 0f, 1f);
        Assert.AreEqual(0f, Vector4.Dot(rowA, centre), 1e-5f);
        Assert.AreEqual(0f, Vector4.Dot(rowB, centre), 1e-5f);
        Assert.IsFalse(animator.AppliesTo(0));
        Assert.IsTrue(animator.AppliesTo(1));
    }

    [TestMethod]
    public void MapCardWithAnimatedFlagHasATextureAnimation()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var card = reader.ReadMap(SkyCardMapFileId)!.Environment!.SkyCards[0];
        Console.WriteLine(card.Material?.TextureAnimation);
        Assert.IsTrue(Gw2SkyCardTextureAnimator.Animates(card.Flags, card.Material?.TextureAnimation));
    }
}
