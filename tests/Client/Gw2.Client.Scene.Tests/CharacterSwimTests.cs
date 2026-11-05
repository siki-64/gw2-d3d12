using System;
using System.Numerics;
using System.Runtime.Intrinsics;
using Gw2.Client.Scene.Havok;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Scene.Tests;

[TestClass]
public sealed class CharacterSwimTests
{
    private const uint BeachMapFileBaseId = 3719751;
    private const float DeepWaterInGameUnits = 400f;
    private const float SurfaceSwimDepthInGameUnits = 1.32f * 32f;

    [TestMethod]
    public void CharacterAtTheSurfaceSwimsAndBelowItDives()
    {
        using var reader = HavokMapWorldTests.OpenReader();
        var map = reader.ReadMap(BeachMapFileBaseId)!;
        var world = Gw2HavokMapWorld.Build(map.Collision, map.Terrain, map.Info.Bounds);
        var water = Gw2CollisionWater.From(map.Collision!);
        var (spot, surfaceZ) = DeepWater(world, water, map.Info.Bounds);

        var floating = Character(world, water, spot with { Z = surfaceZ + SurfaceSwimDepthInGameUnits });
        var time = Settle(floating, 0f, 100, default);
        Assert.AreEqual(Gw2CharacterStateType.Swimming, floating.Context.Current);
        var floatingZ = Gw2CharacterStep.GameOf(floating.Position).Z;
        Console.WriteLine($"surface {surfaceZ:F1}, floats at {floatingZ:F1}");
        Assert.IsTrue(MathF.Abs(floatingZ - surfaceZ - SurfaceSwimDepthInGameUnits) < 4f, $"floats at {floatingZ}, surface {surfaceZ}");

        var diving = Character(world, water, spot with { Z = surfaceZ + 250f });
        Settle(diving, 0f, 100, default);
        Assert.AreEqual(Gw2CharacterStateType.Diving, diving.Context.Current);
        Assert.AreEqual(3, diving.StateByte);
        Assert.AreEqual(4, floating.StateByte);
    }

    private static Gw2CharacterStep Character(Gw2HavokMapWorld world, Gw2CollisionWater water, Vector3 start) =>
        new(world, 24f / 32f, 70f / 32f, Gw2CharacterStep.CollisionOf(start)) { Water = water, WaterSurfaceOffset = Gw2CharacterStep.WaterSurfaceOffsetOf(0f) };

    private static float Settle(Gw2CharacterStep character, float time, int steps, Vector128<float> move)
    {
        var forward = Vector128.Create(1f, 0f, 0f, 0f);
        for (var step = 0; step < steps; step++) character.Step(time += Gw2CharacterStep.StepSeconds, move, forward, 294f / 32f, 0f, false);
        return time;
    }

    private static (Vector3 Spot, float SurfaceZ) DeepWater(Gw2HavokMapWorld world, Gw2CollisionWater water, System.Collections.Generic.IReadOnlyList<float> bounds)
    {
        var random = new Random(3);
        for (var attempt = 0; attempt < 20000; attempt++)
        {
            var x = bounds[0] + random.NextSingle() * (bounds[2] - bounds[0]);
            var y = bounds[1] + random.NextSingle() * (bounds[3] - bounds[1]);
            var surface = water.SurfaceZAt(new Vector3(x, y, -40000f));
            if (surface == Gw2CollisionWater.NoSurface) continue;
            if (world.CastGameRay(new Vector3(x, y, surface - 2000f), new Vector3(x, y, 40000f)) is not { } floor || floor.Position.Z - surface < DeepWaterInGameUnits + 400f) continue;
            return (new Vector3(x, y, 0f), surface);
        }
        throw new AssertInconclusiveException("no deep water found");
    }
}
