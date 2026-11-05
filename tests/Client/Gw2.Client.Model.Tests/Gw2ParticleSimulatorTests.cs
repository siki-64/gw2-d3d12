using System;
using System.Linq;
using System.Numerics;
using Gw2.Client.Model;
using Gw2.Dat.Content;
using Gw2.Dat.Archive;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2ParticleSimulatorTests
{
    internal static Gw2ParticleEmitter Emitter(
        float spawnPeriodSeconds = 0.1f,
        float lifetimeSeconds = 1f,
        float speed = 10f,
        uint flags = 0,
        uint emitterFlags = 0,
        Gw2ParticleCurve? opacityCurve = null,
        uint opacityCurvePreset = 0) => new(
        BoneToken: 0,
        Flags: flags,
        EmitterFlags: emitterFlags,
        AlignmentType: 0,
        AlignmentDirection: Vector3.UnitX,
        SpawnShape: 0,
        SpawnPeriodSeconds: spawnPeriodSeconds,
        SpawnProbabilityPercent: 100f,
        SpawnGroupSize: new(2, 2),
        SpawnRadius: default,
        LifetimeSeconds: new(lifetimeSeconds, lifetimeSeconds),
        Velocity: new Gw2ParticleRange[] { new(1, 1), new(0, 0), new(0, 0), new(speed, speed) },
        Acceleration: new Gw2ParticleRange[] { new(0, 0), new(0, 0), new(0, 0), new(0, 0) },
        VelocityInherit: default,
        Drag: 0f,
        ScaleInitial: new Gw2ParticleRange[] { new(4, 4), new(8, 8) },
        ScaleChange: new Gw2ParticleRange[] { new(2, 2), new(0, 0) },
        ScaleCurve: null,
        ScaleCurvePreset: 0,
        RotationInitialDegrees: default,
        RotationChangeDegrees: default,
        RotationDrag: 0f,
        ColorBegin: new Vector4(1, 0, 0, 1),
        ColorEnd: new Vector4(0, 0, 1, 1),
        ColorPeriod: 1f,
        ColorFalloff: new(0, 1),
        OpacityCurve: opacityCurve,
        OpacityCurvePreset: opacityCurvePreset,
        Flipbook: null,
        TexCoordRect: new Vector4(0, 0, 1, 1));

    [TestMethod]
    public void SpawnsOneGroupPerSpawnPeriod()
    {
        var simulator = new Gw2ParticleSimulator(Emitter(), new Random(1));
        simulator.Advance(0.35f, true, Matrix4x4.Identity);
        Assert.AreEqual(6, simulator.Particles.Count);
    }

    [TestMethod]
    public void BurstEmittersSpawnOneGroupEachTimeTheyBecomeVisible()
    {
        var simulator = new Gw2ParticleSimulator(Emitter(lifetimeSeconds: 0.2f, emitterFlags: Gw2ParticleSimulator.BurstEmitterFlag), new Random(1));
        simulator.Advance(0.05f, true, Matrix4x4.Identity);
        Assert.AreEqual(2, simulator.Particles.Count);
        simulator.Advance(0.5f, true, Matrix4x4.Identity);
        Assert.AreEqual(0, simulator.Particles.Count);
        simulator.Advance(0.05f, false, Matrix4x4.Identity);
        simulator.Advance(0.05f, true, Matrix4x4.Identity);
        Assert.AreEqual(2, simulator.Particles.Count);
    }

    [TestMethod]
    public void HiddenEmittersDoNotSpawn()
    {
        var simulator = new Gw2ParticleSimulator(Emitter(), new Random(1));
        simulator.Advance(1f, false, Matrix4x4.Identity);
        Assert.AreEqual(0, simulator.Particles.Count);
    }

    [TestMethod]
    public void InvertedOpacityPresetsFadeOut()
    {
        var simulator = new Gw2ParticleSimulator(Emitter(spawnPeriodSeconds: 1f, lifetimeSeconds: 2f, flags: Gw2ParticleSimulator.OpacityPresetInvertedFlag, opacityCurvePreset: 10), new Random(1));
        simulator.Advance(1f, true, Matrix4x4.Identity);
        Assert.AreEqual(1f, simulator.ColourOf(simulator.Particles[0]).W, 1e-4f);
    }

    [TestMethod]
    public void ParticlesMoveAlongTheirVelocityAndGrow()
    {
        var simulator = new Gw2ParticleSimulator(Emitter(spawnPeriodSeconds: 1f), new Random(1));
        simulator.Advance(1f, true, Matrix4x4.Identity);
        simulator.Advance(0.5f, false, Matrix4x4.Identity);
        var particle = simulator.Particles[0];
        Assert.AreEqual(5f, particle.PositionInGameUnits.X, 1e-4f);
        Assert.AreEqual(5f, particle.Width, 1e-4f);
        Assert.AreEqual(8f, particle.Height, 1e-4f);
    }

    [TestMethod]
    public void UniformScaleUsesTheWidthForBothAxes()
    {
        var simulator = new Gw2ParticleSimulator(Emitter(spawnPeriodSeconds: 1f, flags: Gw2ParticleSimulator.UniformScaleFlag), new Random(1));
        simulator.Advance(1f, true, Matrix4x4.Identity);
        Assert.AreEqual(simulator.Particles[0].Width, simulator.Particles[0].Height);
    }

    [TestMethod]
    public void ParticlesDieAtTheEndOfTheirLifetime()
    {
        var simulator = new Gw2ParticleSimulator(Emitter(spawnPeriodSeconds: 1f, lifetimeSeconds: 0.5f), new Random(1));
        simulator.Advance(1f, true, Matrix4x4.Identity);
        simulator.Advance(0.6f, false, Matrix4x4.Identity);
        Assert.AreEqual(0, simulator.Particles.Count);
    }

    [TestMethod]
    public void EmitterTransformPlacesAndOrientsNewParticles()
    {
        var simulator = new Gw2ParticleSimulator(Emitter(spawnPeriodSeconds: 1f), new Random(1));
        var emitterToSimulation = Matrix4x4.CreateRotationZ(MathF.PI / 2f) * Matrix4x4.CreateTranslation(100, 0, 0);
        simulator.Advance(1f, true, emitterToSimulation);
        var particle = simulator.Particles[0];
        Assert.AreEqual(100f, particle.PositionInGameUnits.X, 1e-3f);
        Assert.AreEqual(10f, particle.VelocityInGameUnits.Y, 1e-3f);
    }

    [TestMethod]
    public void ColourBlendsFromBeginToEndAndOpacityFollowsTheCurve()
    {
        var fadeOut = new Gw2ParticleCurve(0, new[] { new Vector2(0, 1), new Vector2(1, 0) });
        var simulator = new Gw2ParticleSimulator(Emitter(spawnPeriodSeconds: 1f, lifetimeSeconds: 2f, opacityCurve: fadeOut), new Random(1));
        simulator.Advance(1f, true, Matrix4x4.Identity);
        simulator.Advance(1f, false, Matrix4x4.Identity);
        var colour = simulator.ColourOf(simulator.Particles[0]);
        Assert.AreEqual(0.5f, colour.X, 1e-4f);
        Assert.AreEqual(0.5f, colour.Z, 1e-4f);
        Assert.AreEqual(0.5f, colour.W, 1e-4f);
    }

    [TestMethod]
    public void OpacityPresetsMatchTheClientEasingTable()
    {
        Assert.AreEqual(1f, Gw2ParticleEasing.Evaluate(0, 0.3f));
        Assert.AreEqual(0.0625f, Gw2ParticleEasing.Evaluate(10, 0.5f), 1e-5f);
        Assert.AreEqual(1f, Gw2ParticleEasing.Evaluate(11, 1f), 1e-4f);
        Assert.AreEqual(1f, Gw2ParticleEasing.Evaluate(17, 0.5f), 1e-5f);
        Assert.AreEqual(0f, Gw2ParticleEasing.Evaluate(17, 1f), 1e-5f);
        Assert.AreEqual(1f, Gw2ParticleEasing.Evaluate(null, 10, true, 0f), 1e-5f);
    }

    [TestMethod]
    public void SmoothFlipbooksCarryTheNextFrameAndItsBlend()
    {
        var emitter = Emitter(spawnPeriodSeconds: 1f, lifetimeSeconds: 5f, flags: Gw2ParticleSimulator.SmoothFlipbookFlag) with { Flipbook = new Gw2ParticleFlipbook(4, 1, 4, 0, 2f) };
        var simulator = new Gw2ParticleSimulator(emitter, new Random(1));
        simulator.Advance(1f, true, Matrix4x4.Identity);
        simulator.Advance(0.25f, false, Matrix4x4.Identity);
        var particle = simulator.Particles[0];
        var frame = simulator.FrameOf(particle);
        var (nextCell, extra) = Gw2ParticleQuads.ExtraTexCoords(simulator, particle);
        Assert.AreEqual(Gw2ParticleQuads.Cell(emitter, (frame + 1) % 4), nextCell);
        var expectedBlend = particle.AgeSeconds * 2f - MathF.Truncate(particle.AgeSeconds * 2f);
        Assert.AreEqual(expectedBlend, extra!.Value.Third.X, 1e-5f);
    }

    [TestMethod]
    public void RandomTexCoordsRotateTheParticleByteBothWays()
    {
        Assert.AreEqual(new Vector2(3f / 255f, 192f / 255f), Gw2ParticleQuads.RotatedRandomPair(0x81, 1));
    }
}
