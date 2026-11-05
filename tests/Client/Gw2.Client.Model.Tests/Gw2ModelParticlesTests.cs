using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using Gw2.Dat.Content;
using Gw2.Dat.Archive;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2ModelParticlesTests
{
    private const uint HundredBladesHitModelFileId = 0x355B4;

    [TestMethod]
    public void DecodesTheHundredBladesHitParticlesWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var particles = Gw2ModelParticles.Parse(reader.ReadDocument(HundredBladesHitModelFileId));

        Assert.IsTrue(particles.HasParticles);
        Assert.AreEqual(3, particles.Clouds.Count);
        Assert.AreEqual(13, particles.Emitters.Count);
        CollectionAssert.AreEqual(new[] { 1, 0, 2, 7, 8, 9, 10, 11 }, particles.Clouds[0].EmitterIndices.ToArray());
        Assert.AreEqual(2, particles.Clouds[0].MaterialIndex);

        var firstEmitter = particles.Emitters[0];
        Assert.AreEqual(new Gw2ParticleRange(0.3f, 0.4f), firstEmitter.LifetimeSeconds);
        Assert.AreEqual(new Gw2ParticleRange(3, 4), firstEmitter.SpawnGroupSize);
        Assert.AreEqual(4, firstEmitter.Velocity.Count);
        Assert.AreEqual(new Gw2ParticleRange(45, 60), firstEmitter.ScaleInitial[0]);
        Assert.AreEqual(2u, firstEmitter.Flipbook!.Rows);

        var smokeEmitter = particles.Emitters[3];
        Assert.AreEqual(16u, smokeEmitter.Flipbook!.FrameCount);
        Assert.AreEqual(2, smokeEmitter.OpacityCurve!.Keys.Count);
    }

    [TestMethod]
    public void ReadsTheHitAnimationVisibilityTracksWhenConfigured()
    {
        const ulong HitAnimationToken = 0xA5128;
        const ulong ZeroPoseAnimationToken = 192720390330;
        const ulong InnerEmitterBoneToken = 22313615669705;
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var animations = reader.ReadModelAnimations(HundredBladesHitModelFileId)!;
        var hit = animations.Animations.Single(animation => animation.Token == HitAnimationToken);
        var innerEmitter = hit.VisibilityTracksOrEmpty.Single(track => track.BoneToken == InnerEmitterBoneToken);
        Assert.IsTrue(innerEmitter.IsVisibleAt(0.1f));
        Assert.IsFalse(innerEmitter.IsVisibleAt(0.3f));

        var zeroPose = animations.Animations.Single(animation => animation.Token == ZeroPoseAnimationToken);
        Assert.IsFalse(zeroPose.VisibilityTracksOrEmpty.Single(track => track.BoneToken == InnerEmitterBoneToken).IsVisibleAt(0f));
    }

    [TestMethod]
    public void ReadsNestedPlaneAndMeshEmitterSettingsWhenConfigured()
    {
        const uint PlaneEmitterModelFileId = 3821324;
        const uint MeshEmitterModelFileId = 3807037;
        const uint CloudObstacleModelFileId = 3806607;
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);

        var plane = Gw2ModelParticles.Parse(reader.ReadDocument(PlaneEmitterModelFileId)).Emitters[0];
        Assert.AreEqual(new Gw2ParticleRange(2, 20), plane.ScaleInitial[0]);
        Assert.AreEqual(new Gw2ParticleFlipbook(6, 5, 25, 0, 40), plane.Flipbook);
        Assert.AreEqual(0.99f, plane.TexCoordRect.W, 1e-6f);
        Assert.AreEqual(1f, plane.Drag);

        var meshParticles = Gw2ModelParticles.Parse(reader.ReadDocument(MeshEmitterModelFileId));
        Assert.AreEqual(50, meshParticles.Emitters.Count(emitter => emitter.MeshSettings is not null));
        var mesh = meshParticles.Emitters[0].MeshSettings!;
        Assert.IsTrue(mesh.MeshFile.TryGetFileId(out var meshFileId));
        Assert.AreEqual(3712475u, meshFileId);
        Assert.AreEqual(4f, mesh.ObstacleRadius);
        Assert.AreEqual(new Gw2ParticleRange(1, 0.25f), mesh.ScaleInitial[0]);

        var clouds = Gw2ModelParticles.Parse(reader.ReadDocument(CloudObstacleModelFileId)).Clouds;
        CollectionAssert.AreEqual(new[] { 1, 0 }, clouds.SelectMany(cloud => cloud.ObstacleIndices).ToArray());
    }
}
