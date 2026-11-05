using System.Runtime.Intrinsics.X86;
using System;
using System.Numerics;
using System.Runtime.Intrinsics;
using Gw2.Client.Scene.Havok;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Scene.Tests;

[TestClass]
public sealed class CharacterSpeedCapTests
{
    private const uint BeachMapFileBaseId = 3719751;
    private const float RunSpeed = 294f / 32f;
    private const int SettleSteps = 25;
    private const int MeasuredSteps = 10;

    [TestMethod]
    public void FacingAgainstMoveDirectionCapsGroundSpeed()
    {
        using var reader = HavokMapWorldTests.OpenReader();
        var map = reader.ReadMap(BeachMapFileBaseId)!;
        var world = Gw2HavokMapWorld.Build(map.Collision, map.Terrain, map.Info.Bounds);
        var start = FlatGround(world, map.Info.Bounds);
        var move = new Vector3(1f, 0f, 0f);
        Assert.AreEqual(RunSpeed * Gw2CharacterStep.StepSeconds, StepLength(world, start, move, move), 2e-3f, "facing along the move");
        Assert.AreEqual(Gw2CharacterStep.StrafeSpeedCap * Gw2CharacterStep.StepSeconds, StepLength(world, start, move, new Vector3(0f, 1f, 0f)), 2e-3f, "facing sideways");
        Assert.AreEqual(Gw2CharacterStep.BackpedalSpeedCap * Gw2CharacterStep.StepSeconds, StepLength(world, start, move, new Vector3(-1f, 0f, 0f)), 2e-3f, "facing backwards");
        Assert.AreEqual(RunSpeed * Gw2CharacterStep.StepSeconds, StepLength(world, start, move, Vector3.Normalize(new Vector3(1f, 1f, 0f))), 2e-3f, "facing diagonally ahead");
    }

    private static Vector3 FlatGround(Gw2HavokMapWorld world, System.Collections.Generic.IReadOnlyList<float> bounds)
    {
        var random = new Random(9);
        for (var attempt = 0; attempt < 5000; attempt++)
        {
            var x = bounds[0] + (0.2f + 0.6f * random.NextSingle()) * (bounds[2] - bounds[0]);
            var y = bounds[1] + (0.2f + 0.6f * random.NextSingle()) * (bounds[3] - bounds[1]);
            if (world.CastGameRay(new Vector3(x, y, -40000f), new Vector3(x, y, 40000f)) is not { IsTerrain: true } hit || hit.Normal.Z > -0.999f) continue;
            var ahead = world.CastGameRay(new Vector3(x + 200f, y, -40000f), new Vector3(x + 200f, y, 40000f));
            if (ahead is not { IsTerrain: true } far || far.Normal.Z > -0.999f || MathF.Abs(far.Position.Z - hit.Position.Z) > 1f) continue;
            return hit.Position - new Vector3(0f, 0f, 2f);
        }
        throw new AssertInconclusiveException("no flat ground found");
    }

    private static float StepLength(Gw2HavokMapWorld world, Vector3 start, Vector3 move, Vector3 facing)
    {
        var character = new Gw2CharacterStep(world, 24f / 32f, 70f / 32f, Gw2CharacterStep.CollisionOf(start));
        character.Flags |= Gw2CharacterStep.SpeedCappedByFacingFlag;
        var flatFacing = Gw2CharacterStep.FlatFacingOf(facing);
        var direction = Vector128.Create(move.X, move.Y, 0f, 0f);
        var time = 0f;
        for (var step = 0; step < SettleSteps; step++) character.Step(time += Gw2CharacterStep.StepSeconds, default, flatFacing, RunSpeed, 0f, false);
        for (var step = 0; step < SettleSteps; step++) character.Step(time += Gw2CharacterStep.StepSeconds, direction, flatFacing, RunSpeed, 0f, true);
        var from = character.Position;
        for (var step = 0; step < MeasuredSteps; step++) character.Step(time += Gw2CharacterStep.StepSeconds, direction, flatFacing, RunSpeed, 0f, true);
        var travelled = Sse.Subtract(character.Position, from);
        return MathF.Sqrt(travelled.GetElement(0) * travelled.GetElement(0) + travelled.GetElement(1) * travelled.GetElement(1) + travelled.GetElement(2) * travelled.GetElement(2)) / MeasuredSteps;
    }
}
