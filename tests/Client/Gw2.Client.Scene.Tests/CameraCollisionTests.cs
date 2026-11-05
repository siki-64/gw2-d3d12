using System;
using System.Numerics;
using Gw2.Client.Scene.Havok;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Scene.Tests;

[TestClass]
public sealed class CameraCollisionTests
{
    private const uint BeachMapFileBaseId = 3719751;

    [TestMethod]
    public void EyeSphereFromTheNearPlaneIsCappedAndLeadsAhead()
    {
        var sphere = Gw2CameraCollision.EyeSphereOf(Gw2WorldCameraMotion.DefaultFieldOfView, 16f / 9f, 1f);
        Assert.IsTrue(sphere.Radius > Gw2CameraCollision.EyeRadiusMargin && sphere.Radius <= Gw2CameraCollision.MaximumEyeRadius, $"radius {sphere.Radius}");
        Assert.AreEqual(Gw2CameraCollision.MinimumEyeLead, sphere.Lead);
    }

    [TestMethod]
    public void EyeBehindTheGroundIsPulledInAboveIt()
    {
        using var reader = HavokMapWorldTests.OpenReader();
        var map = reader.ReadMap(BeachMapFileBaseId)!;
        var world = Gw2HavokMapWorld.Build(map.Collision, map.Terrain, map.Info.Bounds);
        var collision = new Gw2CameraCollision(world);
        var ground = Ground(world, map.Info.Bounds);
        var centre = ground - new Vector3(0f, 0f, 150f);
        var desiredEye = ground + new Vector3(200f, 0f, 300f);
        var sphere = new Gw2CameraEyeSphere(Gw2CameraCollision.MaximumEyeRadius, Gw2CameraCollision.MinimumEyeLead);
        var eye = collision.PlaceEye(centre, centre, desiredEye, sphere);
        Console.WriteLine($"ground {ground}, eye {eye}");
        Assert.IsTrue(Vector3.Distance(eye, centre) < Vector3.Distance(desiredEye, centre) - 50f, $"eye {eye} not pulled in");
        var below = world.CastGameRay(eye, eye + new Vector3(0f, 0f, 2000f));
        Assert.IsNotNull(below, "eye ended under the ground");
        Assert.IsTrue(below.Value.Position.Z - eye.Z > Gw2CameraCollision.MaximumEyeRadius * 0.5f, $"eye {eye} hugs ground {below.Value.Position}");

        var clear = collision.PlaceEye(centre, centre, centre - new Vector3(0f, 0f, 100f), sphere);
        Assert.IsTrue(Vector3.Distance(clear, centre - new Vector3(0f, 0f, 100f)) < 1e-3f, $"open sky moved the eye to {clear}");
        var single = collision.PlaceEye(centre, centre, desiredEye, sphere, collisionSensitivity: 0);
        Assert.IsTrue(Vector3.Distance(single, centre) < Vector3.Distance(desiredEye, centre) - 50f, $"single sweep eye {single} not pulled in");
    }

    [TestMethod]
    public void CentreBelowTheGroundStopsAboveItAndThinAgentsSkipTheCast()
    {
        using var reader = HavokMapWorldTests.OpenReader();
        var map = reader.ReadMap(BeachMapFileBaseId)!;
        var world = Gw2HavokMapWorld.Build(map.Collision, map.Terrain, map.Info.Bounds);
        var collision = new Gw2CameraCollision(world);
        var feet = Ground(world, map.Info.Bounds);
        var buried = feet + new Vector3(0f, 0f, 200f);
        var centre = collision.CollideCentre(feet, 35f, 160f, buried);
        Assert.IsTrue(centre.Z < feet.Z, $"centre {centre} under feet {feet}");
        Assert.AreEqual(buried, collision.CollideCentre(feet, 20f, 160f, buried));
    }

    private static Vector3 Ground(Gw2HavokMapWorld world, System.Collections.Generic.IReadOnlyList<float> bounds)
    {
        var random = new Random(5);
        for (var attempt = 0; attempt < 5000; attempt++)
        {
            var x = bounds[0] + (0.2f + 0.6f * random.NextSingle()) * (bounds[2] - bounds[0]);
            var y = bounds[1] + (0.2f + 0.6f * random.NextSingle()) * (bounds[3] - bounds[1]);
            if (world.CastGameRay(new Vector3(x, y, -40000f), new Vector3(x, y, 40000f)) is not { IsTerrain: true } hit || hit.Normal.Z > -0.99f) continue;
            var ahead = world.CastGameRay(new Vector3(x + 200f, y, -40000f), new Vector3(x + 200f, y, 40000f));
            if (ahead is not { IsTerrain: true } far || MathF.Abs(far.Position.Z - hit.Position.Z) > 30f) continue;
            return hit.Position;
        }
        throw new AssertInconclusiveException("no flat ground found");
    }
}

[TestClass]
public sealed class CameraCentreLineOfSightTests
{
    [TestMethod]
    public void CentreThroughTheGroundJumpsPastItAndOpenAirEases()
    {
        using var reader = HavokMapWorldTests.OpenReader();
        var map = reader.ReadMap(3719751)!;
        var world = Gw2HavokMapWorld.Build(map.Collision, map.Terrain, map.Info.Bounds);
        var random = new Random(11);
        Vector3 above = default, below = default, groundPosition = default;
        var found = false;
        for (var attempt = 0; attempt < 5000 && !found; attempt++)
        {
            var x = map.Info.Bounds[0] + random.NextSingle() * (map.Info.Bounds[2] - map.Info.Bounds[0]);
            var y = map.Info.Bounds[1] + random.NextSingle() * (map.Info.Bounds[3] - map.Info.Bounds[1]);
            if (world.CastGameRay(new Vector3(x, y, -40000f), new Vector3(x, y, 40000f), Gw2HavokMapWorld.CameraLineOfSightMask) is not { } ground) continue;
            above = ground.Position - new Vector3(0f, 0f, 150f);
            below = ground.Position + new Vector3(0f, 0f, 1500f);
            if (world.CastGameRay(above, below, Gw2HavokMapWorld.CameraLineOfSightMask) is not { } forward) continue;
            if (world.CastGameRay(below, above, Gw2HavokMapWorld.CameraLineOfSightMask) is not { } back) continue;
            if (Vector3.DistanceSquared(back.Position, forward.Position) <= 180f * 180f || back.Fraction * 1650f < 22f) continue;
            groundPosition = forward.Position;
            found = true;
        }
        if (!found) throw new AssertInconclusiveException("no thick ground");
        var moved = Gw2CameraCentreLineOfSight.FollowCentre(world, above, below, above, below, 5000f, 10f, 0.01f);
        Assert.IsTrue(moved.Z > groundPosition.Z, $"centre {moved} still above ground {groundPosition}");
        var open = Gw2CameraCentreLineOfSight.FollowCentre(world, above, above - new Vector3(0f, 0f, 400f), above, above - new Vector3(0f, 0f, 400f), 5000f, 10f, 0.01f);
        Assert.AreEqual(Gw2WorldCameraMotion.Ease(above, above - new Vector3(0f, 0f, 400f), 10f, 0.01f), open);
    }
}

[TestClass]
public sealed class CameraEyeSettleTests
{
    [TestMethod]
    public void UnobstructedEyeKeepsTheCentreAndIsCappedAtTheMaximumDistance()
    {
        var centre = new Vector3(0f, 0f, -100f);
        var desiredEye = new Vector3(-600f, 0f, -300f);
        var (eye, settled) = Gw2CameraCollision.SettleEyeAndCentre(centre, centre, desiredEye, desiredEye, 400f);
        Assert.AreEqual(centre, settled);
        Assert.AreEqual(400f, Vector3.Distance(eye, centre), 1e-3f);
    }

    [TestMethod]
    public void CentreShiftsTowardTheCollidedCentreByAtMostTheirGap()
    {
        var centre = new Vector3(0f, 0f, -100f);
        var collided = new Vector3(0f, 0f, -60f);
        var desiredEye = new Vector3(-600f, 0f, -100f);
        var eye = new Vector3(-300f, 0f, -60f);
        var (_, settled) = Gw2CameraCollision.SettleEyeAndCentre(centre, collided, desiredEye, eye, 10000f);
        Assert.IsTrue(Vector3.Distance(settled, centre) <= Vector3.Distance(collided, centre) + 1e-3f, $"{settled}");
        Assert.AreNotEqual(centre, settled);
    }
}
