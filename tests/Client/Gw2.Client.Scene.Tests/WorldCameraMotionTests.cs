using System;
using System.Numerics;
using Gw2.Client.Scene;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Scene.Tests;

[TestClass]
public sealed class WorldCameraMotionTests
{
    private const float Tolerance = 1e-4f;

    [TestMethod]
    public void WrapsAnglesIntoMinusPiToPi()
    {
        Assert.AreEqual(-MathF.PI / 2f, Gw2WorldCameraMotion.WrapAngle(3f * MathF.PI / 2f), Tolerance);
        Assert.AreEqual(MathF.PI / 2f, Gw2WorldCameraMotion.WrapAngle(-3f * MathF.PI / 2f), Tolerance);
        Assert.AreEqual(0.5f, Gw2WorldCameraMotion.WrapAngle(0.5f + 4f * MathF.Tau), Tolerance);
    }

    [TestMethod]
    public void TurnsAlongTheShorterArcAcrossPi()
    {
        var from = MathF.PI - 0.1f;
        var to = -MathF.PI + 0.1f;

        var halfway = Gw2WorldCameraMotion.TurnAngleToward(from, to, 1f, 0.5f);

        Assert.AreEqual(MathF.PI, MathF.Abs(Gw2WorldCameraMotion.WrapAngle(halfway)), Tolerance);
        Assert.AreEqual(to, Gw2WorldCameraMotion.WrapAngle(Gw2WorldCameraMotion.TurnAngleToward(from, to, 1f, 2f)), Tolerance);
        Assert.AreEqual(from, Gw2WorldCameraMotion.TurnAngleToward(from, to, 1f, -1f), Tolerance);
    }

    [TestMethod]
    public void EasesByRateTimesElapsedClampedToOne()
    {
        var current = Vector3.Zero;
        var desired = new Vector3(100f, -40f, 20f);

        Assert.AreEqual(0f, Vector3.Distance(new Vector3(20f, -8f, 4f), Gw2WorldCameraMotion.Ease(current, desired, Gw2WorldCameraMotion.DefaultCentreRatePerSecond, 0.01f)), Tolerance);
        Assert.AreEqual(desired, Gw2WorldCameraMotion.Ease(current, desired, Gw2WorldCameraMotion.DefaultCentreRatePerSecond, 1f));
    }

    [TestMethod]
    public void SmoothDampsLikeTheExeSpring()
    {
        var omega = 2f / 0.1f;
        var x = omega * 0.016f;
        var decay = 1f / (1f + x + 0.5f * x * x + x * x * x / 6f);
        var step = (-0.4f * omega) * 0.016f;

        var (value, velocity) = Gw2WorldCameraMotion.SmoothDamp(0.5f, 0.9f, 0f, 0.1f, 0.016f);

        Assert.AreEqual(0.9f + (step - 0.4f) * decay, value, Tolerance);
        Assert.AreEqual(-step * omega * decay, velocity, Tolerance);
    }

    [TestMethod]
    public void SmoothDampSettlesOnTheTarget()
    {
        float value = 100f, velocity = 0f;
        for (var frame = 0; frame < 120; frame++)
            (value, velocity) = Gw2WorldCameraMotion.SmoothDamp(value, 600f, velocity, Gw2WorldCameraMotion.DefaultSettingsSmoothSeconds, 1f / 60f);

        Assert.AreEqual(600f, value, 0.01f);
        Assert.AreEqual(350f, Gw2WorldCameraMotion.DefaultDistance);
    }

    [TestMethod]
    public void PlacesTheEyeBehindAndAboveACentreEightyUnitsOverTheAnchor()
    {
        var (eye, centre) = Gw2WorldCameraMotion.PlaceBehindAnchor(new Vector3(10f, 20f, 300f), 0f, 1000f);

        Assert.AreEqual(new Vector3(10f, 20f, 220f), centre);
        Assert.AreEqual(10f - 1000f * MathF.Cos(0.2268928f), eye.X, Tolerance);
        Assert.AreEqual(20f, eye.Y, Tolerance);
        Assert.AreEqual(220f - 1000f * MathF.Sin(0.2268928f), eye.Z, Tolerance);
    }

    [TestMethod]
    public void ZoomsOneNotchBy83UnitsAtTheDefaultSpeedWithinTheDistanceLimits()
    {
        Assert.AreEqual(350f - 83.333336f, Gw2WorldCameraMotion.ZoomedDistance(350f, 1f, Gw2WorldCameraMotion.DefaultZoomSpeedOption, 100f, 600f), Tolerance);
        Assert.AreEqual(350f + 83.333336f / 7f, Gw2WorldCameraMotion.ZoomedDistance(350f, -1f, 0, 100f, 600f), Tolerance);
        Assert.AreEqual(600f, Gw2WorldCameraMotion.ZoomedDistance(590f, -1f, Gw2WorldCameraMotion.DefaultZoomSpeedOption, 100f, 600f), Tolerance);
        Assert.AreEqual(100f, Gw2WorldCameraMotion.ZoomedDistance(120f, 1f, Gw2WorldCameraMotion.DefaultZoomSpeedOption, 100f, 600f), Tolerance);
    }

    [TestMethod]
    public void EasesTheDistanceTowardTheZoomAtTheFollowRate()
    {
        Assert.AreEqual(350f - 250f * 1.5f * 0.1f, Gw2WorldCameraMotion.EaseDistance(350f, 100f, Gw2WorldCameraMotion.FollowDistanceRatePerSecond, 0.1f), 1e-4f);
        Assert.AreEqual(100f, Gw2WorldCameraMotion.EaseDistance(350f, 100f, Gw2WorldCameraMotion.FollowDistanceRatePerSecond, 1f));
    }

    [TestMethod]
    public void EasesTheCentreUnderTheSnapDistanceAndJumpsBeyondIt()
    {
        float snap = Gw2WorldCameraMotion.SnapDistanceOf(Gw2WorldCameraMotion.CharacterSmoothing, snapsCloseToTheCharacter: false);
        Assert.AreEqual(5000f, snap);
        Assert.AreEqual(150f, Gw2WorldCameraMotion.SnapDistanceOf(Gw2WorldCameraMotion.CharacterSmoothing, snapsCloseToTheCharacter: true));
        Assert.AreEqual(2000f, Gw2WorldCameraMotion.SnapDistanceOf(Gw2WorldCameraMotion.WideSmoothing, snapsCloseToTheCharacter: false));
        Assert.AreEqual(20f, Gw2WorldCameraMotion.FollowCentre(Vector3.Zero, new Vector3(100f, 0f, 0f), snap, Gw2WorldCameraMotion.DefaultCentreRatePerSecond, 0.01f).X, 1e-4f);
        Assert.AreEqual(new Vector3(6000f, 0f, 0f), Gw2WorldCameraMotion.FollowCentre(Vector3.Zero, new Vector3(6000f, 0f, 0f), snap, Gw2WorldCameraMotion.DefaultCentreRatePerSecond, 0.01f));
        Assert.AreEqual(new Vector3(1f, 2f, 3f), Gw2WorldCameraMotion.FollowCentre(Vector3.Zero, new Vector3(1f, 2f, 3f), Gw2WorldCameraMotion.SnapDistanceOf(Gw2WorldCameraMotion.NoSmoothing, false), 20f, 0.01f));
    }
}
