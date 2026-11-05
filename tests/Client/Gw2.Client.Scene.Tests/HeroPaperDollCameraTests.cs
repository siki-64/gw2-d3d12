using System;
using System.Numerics;
using Gw2.Client.Scene;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Scene.Tests;

[TestClass]
public sealed class HeroPaperDollCameraTests
{
    private static readonly Gw2HeroPaperDollCamera Camera = new(0f, 0f, MathF.PI / 2f, 1f, 300f, 0.5f, 100f);

    [TestMethod]
    public void LooksAlongPositiveYFromTheEyeDistance()
    {
        Assert.AreEqual(new Vector3(0f, -300f, -10000f), Camera.Eye);
        var target = Vector3.Transform(Camera.Target, Camera.View);
        Assert.AreEqual(0f, target.X, 1e-3f);
        Assert.AreEqual(0f, target.Y, 1e-3f);
        Assert.AreEqual(300f, target.Z, 1e-3f);
    }

    [TestMethod]
    public void PlacesTheModelAtScaledHeightAndTurnsItBackByTheYawAndOffset()
    {
        var origin = Vector3.Transform(Vector3.Zero, Camera.World);
        Assert.AreEqual(new Vector3(0f, 0f, 85f - 10000f), origin);
        Assert.AreEqual(1f, Vector3.TransformNormal(Vector3.UnitX, Camera.World).X, 1e-5f);
        var turned = Vector3.TransformNormal(Vector3.UnitX, (Camera with { Yaw = MathF.PI / 4f, YawOffset = MathF.PI / 4f }).World);
        Assert.AreEqual(0f, turned.X, 1e-5f);
        Assert.AreEqual(-1f, turned.Y, 1e-5f);
    }

    [TestMethod]
    public void SplitsTheDiagonalFieldOfViewOverTheFrameAspect()
    {
        Assert.AreEqual(1f / MathF.Sqrt(2f), Camera.HalfHeightTangent(100, 100), 1e-5f);
        Assert.AreEqual(1f / MathF.Sqrt(5f), Camera.HalfHeightTangent(200, 100), 1e-5f);
    }

    [TestMethod]
    public void ShiftsTheFrustumByTheVerticalOffset()
    {
        var centred = Camera.Projection(100, 100);
        Assert.AreEqual(0f, centred.M32, 1e-5f);
        var raised = (Camera with { VerticalOffset = 1f }).Projection(100, 100);
        var point = Vector4.Transform(new Vector4(0f, 0f, 10f, 1f), raised);
        Assert.AreEqual(1f, point.Y / point.W, 1e-5f);
    }
}
