using System;
using System.Numerics;
using System.Runtime.Intrinsics;
using Gw2.Client.Scene.Havok;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Scene.Tests;

[TestClass]
public sealed class CharacterMoveToTests
{
    private const uint BeachMapFileBaseId = 3719751;
    private const float LeapSpeedInGameUnits = 1000f;
    private const float LeapDistanceInGameUnits = 240f;
    private const float RecordedLeapJumpVelocity = -377.55f;
    private const int MostSteps = 40;

    [TestMethod]
    public void AServerMoveToCarriesTheCharacterToTheDestinationWithoutInputAndThenReleasesIt()
    {
        var (world, start) = GentleGround();
        var destination = start + new Vector3(LeapDistanceInGameUnits, 0f, 0f);
        var character = new Gw2CharacterStep(world, 24f / 32f, 70f / 32f, Gw2CharacterStep.CollisionOf(start));
        var forward = Vector128.Create(1f, 0f, 0f, 0f);
        var time = 0f;
        for (var step = 0; step < 5; step++) character.Step(time += Gw2CharacterStep.StepSeconds, default, forward, 294f / 32f, 0f, false);
        character.Jump(RecordedLeapJumpVelocity, false);
        character.MoveTo(Gw2CharacterStep.CollisionOf(destination), true);
        var steps = 0;
        var leftTheGround = false;
        while (character.MovesToDestination && steps < MostSteps)
        {
            character.Step(time += Gw2CharacterStep.StepSeconds, default, forward, LeapSpeedInGameUnits / 32f, 0f, false);
            leftTheGround |= character.Context.Current is Gw2CharacterStateType.Jumping or Gw2CharacterStateType.InAir;
            steps++;
        }
        var reached = Gw2CharacterStep.GameOf(character.Position);
        var flatMiss = new Vector2(reached.X - destination.X, reached.Y - destination.Y).Length();
        Console.WriteLine($"{steps} steps, flat miss {flatMiss:F2}, airborne {leftTheGround}");
        Assert.IsFalse(character.MovesToDestination, "the move-to ends on arrival");
        Assert.IsTrue(leftTheGround, "the server jump lifts the character");
        Assert.IsTrue(flatMiss < 8f, $"stopped {flatMiss} units from the destination");
        Assert.IsTrue(steps * Gw2CharacterStep.StepSeconds * LeapSpeedInGameUnits >= LeapDistanceInGameUnits * 0.9f, $"{steps} steps is faster than the travel speed");
    }

    [TestMethod]
    public void StoppingAMoveToClearsItsFlags()
    {
        var (world, start) = GentleGround();
        var character = new Gw2CharacterStep(world, 24f / 32f, 70f / 32f, Gw2CharacterStep.CollisionOf(start));
        character.MoveTo(Gw2CharacterStep.CollisionOf(start + new Vector3(500f, 0f, 0f)), true);
        Assert.IsTrue(character.MovesToDestination);
        character.StopMoveTo();
        Assert.IsFalse(character.MovesToDestination);
        Assert.AreEqual(0UL, character.Flags & 0x800UL);
    }

    private static (Gw2HavokMapWorld World, Vector3 Start) GentleGround()
    {
        using var reader = HavokMapWorldTests.OpenReader();
        var map = reader.ReadMap(BeachMapFileBaseId)!;
        var world = Gw2HavokMapWorld.Build(map.Collision, map.Terrain, map.Info.Bounds);
        var bounds = map.Info.Bounds;
        var random = new Random(23);
        for (var attempt = 0; attempt < 20000; attempt++)
        {
            var x = bounds[0] + (0.1f + 0.8f * random.NextSingle()) * (bounds[2] - bounds[0]);
            var y = bounds[1] + (0.1f + 0.8f * random.NextSingle()) * (bounds[3] - bounds[1]);
            if (!IsGentle(world, x, y, out var start)) continue;
            var clear = true;
            for (var along = 30f; along <= LeapDistanceInGameUnits + 30f && clear; along += 30f) clear = IsGentle(world, x + along, y, out _);
            if (clear) return (world, start);
        }
        throw new AssertInconclusiveException("no gentle stretch found");
    }

    private static bool IsGentle(Gw2HavokMapWorld world, float x, float y, out Vector3 ground)
    {
        ground = default;
        if (world.CastGameRay(new Vector3(x, y, -40000f), new Vector3(x, y, 40000f)) is not { IsTerrain: true } hit || hit.Normal.Z > -0.97f) return false;
        ground = hit.Position - new Vector3(0f, 0f, 2f);
        return true;
    }
}
