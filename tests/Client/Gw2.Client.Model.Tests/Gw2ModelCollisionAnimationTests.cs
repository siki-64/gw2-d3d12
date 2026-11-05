using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

using Gw2ModelCollision = Gw2.Dat.Content.Gw2ModelCollision;

[TestClass]
public sealed class Gw2ModelCollisionAnimationTests
{
    private const uint ManyAnimationsModelFileId = 2602882;
    private const uint KeyFrameModelFileId = 2084247;

    [TestMethod]
    public void ReadsCollisionAnimationsKeyFramesAndSurfaceTokensWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);

        var many = Gw2ModelCollision.TryParse(reader.ReadDocument(ManyAnimationsModelFileId))!;
        Assert.AreEqual(50, many.Animations.Count);
        Assert.AreEqual(9045156ul, many.Animations[0].Animation);
        CollectionAssert.AreEqual(new ulong[] { 1152926225299291639 }, many.SurfaceTokens.Single().ToArray());

        var keyed = Gw2ModelCollision.TryParse(reader.ReadDocument(KeyFrameModelFileId))!;
        Assert.AreEqual(7, keyed.Animations.Count);
        Assert.AreEqual(6, keyed.Animations.Count(animation => animation.Objects.Count > 0));
        var animation = keyed.Animations[1];
        Assert.AreEqual(2, animation.Objects.Count);
        var animated = animation.Objects[0];
        CollectionAssert.AreEqual(new uint[] { 1 }, animated.ShapeIndices.ToArray());
        Assert.AreEqual(242, animated.KeyFrames.Count);
        Assert.AreEqual(0f, animated.KeyFrames[0].Time);
        Assert.AreEqual(-2.8011322f, animated.KeyFrames[0].Position.X, 1e-5f);
        Assert.AreEqual(0.99999976f, animated.KeyFrames[0].Orientation.W, 1e-6f);
        Assert.AreEqual(8f, animated.KeyFrames[^1].Time, 1e-4f);
        foreach (var shapeIndex in animated.ShapeIndices)
            Assert.IsTrue(shapeIndex < keyed.Meshes.Count + keyed.Boxes.Count + keyed.Spheres.Count + keyed.Capsules.Count);
    }
}
