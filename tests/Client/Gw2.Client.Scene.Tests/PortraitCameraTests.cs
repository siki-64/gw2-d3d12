using Gw2.Formats;
using System;
using System.Numerics;
using Gw2.Client.Scene;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Scene.Tests;

[TestClass]
public sealed class PortraitCameraTests
{
    [TestMethod]
    public void MissingModelPointsFallBackToTheDefaultEyeLookingAtTheOrigin()
    {
        var camera = Gw2PortraitCamera.FromModelPoints(null, null, 0.5f);

        Assert.AreEqual(new Vector3(0f, -250f, 0f), camera.Eye);
        Assert.AreEqual(Vector3.Zero, camera.Target);
        Assert.AreEqual(Vector3.Zero, Vector3.Transform(camera.Target, camera.View) with { Z = 0f });
    }

    [TestMethod]
    public void FieldOfViewSpansTheDiagonalSoASquareTargetNarrowsTheVerticalAngle()
    {
        var camera = new Gw2PortraitCamera(Gw2PortraitCamera.DefaultEye, Vector3.Zero, 1f);
        var expected = 2f * MathF.Atan(MathF.Tan(0.5f) / MathF.Sqrt(2f));

        Assert.AreEqual(expected, camera.VerticalFieldOfView(64, 64), 1e-5f);
    }

    [TestMethod]
    public void PortraitsAboveOneHundredTwentyEightPixelsUseTheLargeTarget()
    {
        Assert.IsFalse(Gw2PortraitCamera.UsesLargeTarget(128, 128));
        Assert.IsTrue(Gw2PortraitCamera.UsesLargeTarget(129, 64));
    }

    [TestMethod]
    public void LightPointsAwayFromItsYawAndPitch()
    {
        var light = new Gw2PortraitLight(0f, 90f, Vector3.One);

        Assert.AreEqual(-1f, light.Direction.Z, 1e-5f);
        Assert.AreEqual(5, Gw2PortraitCamera.Lights.Count);
    }

    [TestMethod]
    public void PortraitBoneTokensSpellThePortraitCameraAndAimBones()
    {
        Assert.AreEqual("portraitcaml", Token64.Decode(Gw2PortraitCamera.EyeBoneToken(Gw2PortraitSide.Left)));
        Assert.AreEqual("portraitaimr", Token64.Decode(Gw2PortraitCamera.TargetBoneToken(Gw2PortraitSide.Right)));
        Assert.AreEqual("camlfov", Token64.Decode(Gw2PortraitCamera.FieldOfViewPropertyToken(Gw2PortraitSide.Left)));
    }

    [TestMethod]
    public void SkeletonCameraUsesTheBindPositionsOfThePortraitBones()
    {
        var skeleton = new Gw2ModelSkeleton(default, [Bone("PortraitCamL", new Vector3(0f, -120f, -160f)), Bone("PortraitAimL", new Vector3(0f, 0f, -150f))], [], [], [], []);

        var camera = Gw2PortraitCamera.FromSkeleton(skeleton, Gw2PortraitSide.Left, 0.5f);

        Assert.AreEqual(new Vector3(0f, -120f, -160f), camera.Eye);
        Assert.AreEqual(new Vector3(0f, 0f, -150f), camera.Target);
        Assert.AreEqual(Gw2PortraitCamera.DefaultEye, Gw2PortraitCamera.FromSkeleton(skeleton, Gw2PortraitSide.Right, 0.5f).Eye);
    }

    private static Gw2ModelBone Bone(string name, Vector3 bindPosition) =>
        new(name, uint.MaxValue, 0, bindPosition, Quaternion.Identity, Matrix4x4.Identity, Matrix4x4.CreateTranslation(-bindPosition), 0f);
}
