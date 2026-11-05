using System;
using System.Numerics;
using Gw2.Client.Scene.Havok;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Scene.Tests;

[TestClass]
public sealed class CharacterSlopeTests
{
    private const uint BeachMapFileBaseId = 3719751;
    private const int Steps = 50;

    [TestMethod]
    public void StandingOnASteepSlopeSlidesDownAndOnAGentleOneStays()
    {
        using var reader = HavokMapWorldTests.OpenReader();
        var map = reader.ReadMap(BeachMapFileBaseId)!;
        var world = Gw2HavokMapWorld.Build(map.Collision, map.Terrain, map.Info.Bounds);
        var steep = Ground(world, map.Info.Bounds, normalZ => normalZ > -0.55f && normalZ < -0.3f);
        var gentle = Ground(world, map.Info.Bounds, normalZ => normalZ < -0.97f);
        var steepDrop = HeightLostStandingStill(world, steep);
        var gentleDrop = HeightLostStandingStill(world, gentle);
        Console.WriteLine($"steep drop {steepDrop:F2}, gentle drop {gentleDrop:F2}");
        Assert.IsTrue(steepDrop > 20f, $"steep slope only dropped {steepDrop} units");
        Assert.IsTrue(gentleDrop < 2f, $"gentle slope dropped {gentleDrop} units");
    }

    private static Vector3 Ground(Gw2HavokMapWorld world, System.Collections.Generic.IReadOnlyList<float> bounds, Func<float, bool> accepts)
    {
        var random = new Random(17);
        for (var attempt = 0; attempt < 20000; attempt++)
        {
            var x = bounds[0] + (0.1f + 0.8f * random.NextSingle()) * (bounds[2] - bounds[0]);
            var y = bounds[1] + (0.1f + 0.8f * random.NextSingle()) * (bounds[3] - bounds[1]);
            if (world.CastGameRay(new Vector3(x, y, -40000f), new Vector3(x, y, 40000f)) is not { IsTerrain: true } hit || !accepts(hit.Normal.Z)) continue;
            var sameSlope = true;
            for (var offset = -1; offset <= 1 && sameSlope; offset += 2)
                sameSlope = world.CastGameRay(new Vector3(x + 30f * offset, y + 30f * offset, -40000f), new Vector3(x + 30f * offset, y + 30f * offset, 40000f)) is { IsTerrain: true } near && accepts(near.Normal.Z);
            if (sameSlope) return hit.Position - new Vector3(0f, 0f, 2f);
        }
        throw new AssertInconclusiveException("no matching slope found");
    }

    private static float HeightLostStandingStill(Gw2HavokMapWorld world, Vector3 start)
    {
        var character = new Gw2CharacterStep(world, 24f / 32f, 70f / 32f, Gw2CharacterStep.CollisionOf(start));
        var time = 0f;
        var forward = System.Runtime.Intrinsics.Vector128.Create(1f, 0f, 0f, 0f);
        for (var step = 0; step < 5; step++) character.Step(time += Gw2CharacterStep.StepSeconds, default, forward, 294f / 32f, 0f, false);
        var settled = Gw2CharacterStep.GameOf(character.Position).Z;
        for (var step = 0; step < Steps; step++) character.Step(time += Gw2CharacterStep.StepSeconds, default, forward, 294f / 32f, 0f, false);
        return Gw2CharacterStep.GameOf(character.Position).Z - settled;
    }
}
