using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Gw2.Client.Model;
using Gw2.Dat.Content;
using Gw2.Formats;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2EffectModelPlaybackTests
{
    private const uint HundredBladesHitModelFileId = 0x355B4;
    private const ulong HitAnimationToken = 0xA5128;
    private const float FrameSeconds = 1f / 60f;
    private const uint TelegraphModelFileId = 0xD226;
    private const uint DiffuseFadeToken = 0x5472FF5C;
    private const uint GreatswordTrailModelFileId = 0xE4C6;

    [TestMethod]
    public void EncodesTheZeroPoseToken() => Assert.AreEqual(192720390330ul, Token64.Encode("zeropose"));

    [TestMethod]
    public void EmitsOnlyOnceTheHitAnimationPlaysWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var asset = reader.ReadEffectModel(HundredBladesHitModelFileId)!;
        var playback = new Gw2EffectModelPlayback(asset, HitAnimationToken, new Random(1));
        Assert.AreEqual(13, playback.Emitters.Count);
        Assert.IsTrue(playback.Emitters.All(emitter => emitter.BoneIndex >= 0));

        for (var frame = 0; frame < 10; frame++) playback.Advance(FrameSeconds, null, Matrix4x4.Identity);
        Assert.AreEqual(0, playback.Emitters.Sum(emitter => emitter.Simulator.Particles.Count));

        for (var frame = 0; frame < 6; frame++) playback.Advance(FrameSeconds, frame * FrameSeconds, Matrix4x4.Identity);
        Assert.IsTrue(playback.Emitters.Sum(emitter => emitter.Simulator.Particles.Count) > 0);

        var corners = new List<Gw2ParticleCorner>();
        var camera = new Gw2ParticleCamera(new Vector3(0, -500, 0), Vector3.UnitX, Vector3.UnitZ, Vector3.UnitY);
        foreach (var emitter in playback.Emitters) Gw2ParticleQuads.Build(emitter.Simulator, emitter.World, 1f, camera, corners);
        Assert.AreEqual(playback.Emitters.Sum(emitter => emitter.Simulator.Particles.Count) * Gw2ParticleQuads.CornersPerQuad, corners.Count);
        Assert.IsTrue(corners.All(corner => float.IsFinite(corner.Position.X) && corner.TexCoord.X >= 0f && corner.TexCoord.Y <= 1f));
    }

    [TestMethod]
    public void PicksTheFlipbookCellInsideTheTextureRectangle()
    {
        var emitter = Gw2ParticleSimulatorTests.Emitter() with
        {
            TexCoordRect = new Vector4(0f, 0.5f, 0.5f, 1f),
            Flipbook = new Gw2ParticleFlipbook(Columns: 2, Rows: 2, FrameCount: 4, StartFrame: 0, FramesPerSecond: 10),
        };
        Assert.AreEqual(new Vector4(0f, 0.5f, 0.25f, 0.75f), Gw2ParticleQuads.Cell(emitter, 0));
        Assert.AreEqual(new Vector4(0.25f, 0.75f, 0.5f, 1f), Gw2ParticleQuads.Cell(emitter, 3));
    }

    [TestMethod]
    public void BendsTheCornerNormalAwayFromTheCamera()
    {
        var normal = Gw2ParticleQuads.BentNormal(Vector3.Zero, new Vector3(2f, 0f, 0f), -Vector3.UnitY);
        Assert.AreEqual(new Vector3(0.70720518f, 0.70720518f, 0f), normal);
    }

    [TestMethod]
    public void PlaysTheTelegraphCompoundOnTheAllyMesh()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var asset = reader.ReadEffectModel(TelegraphModelFileId)!;
        var model = Gw2ModelBuilder.Build(TelegraphModelFileId, asset.Model, _ => null);
        var playback = new Gw2EffectModelPlayback(asset, Token64.Encode("areasa"), new Random(1));
        var allyMesh = model.Meshes.Single(mesh => mesh.VisBone == unchecked((long)Token64.Encode("AreaSA")));
        var enemyMesh = model.Meshes.Single(mesh => mesh.VisBone == unchecked((long)Token64.Encode("AreaSE")));
        var allyMaterialId = model.Materials[(int)allyMesh.MaterialIndex].MaterialId;

        playback.Advance(FrameSeconds, null, Matrix4x4.Identity);
        Assert.AreEqual(0.002f, playback.MaterialConstants(allyMaterialId).Single(constant => constant.Token == DiffuseFadeToken).Value.X, 1e-3f);

        playback.Advance(FrameSeconds, 0.2f, Matrix4x4.Identity);
        Assert.IsTrue(playback.IsMeshVisible(allyMesh));
        Assert.IsFalse(playback.IsMeshVisible(enemyMesh));
        var fadingIn = playback.MaterialConstants(allyMaterialId).Single(constant => constant.Token == DiffuseFadeToken).Value.X;
        Assert.IsTrue(fadingIn is > 0.1f and < 0.95f, fadingIn.ToString());

        playback.Advance(FrameSeconds, 3f, Matrix4x4.Identity);
        Assert.AreEqual(1f, playback.MaterialConstants(allyMaterialId).Single(constant => constant.Token == DiffuseFadeToken).Value.X, 0.01f);
        Assert.AreEqual(120f, Vector3.Transform(new Vector3(120f, 0f, 0f), playback.MeshWorld(allyMesh)).Length(), 1f);

        playback.Advance(FrameSeconds, 3.1f, Matrix4x4.Identity, isFinishing: true);
        playback.Advance(0.25f, 3.35f, Matrix4x4.Identity, isFinishing: true);
        var fadingOut = playback.MaterialConstants(allyMaterialId).Single(constant => constant.Token == DiffuseFadeToken).Value.X;
        Assert.IsTrue(fadingOut is > 0.1f and < 0.9f, fadingOut.ToString());
        playback.Advance(0.5f, 3.85f, Matrix4x4.Identity, isFinishing: true);
        Assert.AreEqual(0f, playback.MaterialConstants(allyMaterialId).Single(constant => constant.Token == DiffuseFadeToken).Value.X, 0.02f);
    }

    [TestMethod]
    public void LeavesGreatswordTrailRibbonsBehindTheMovingModel()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var asset = reader.ReadEffectModel(GreatswordTrailModelFileId)!;
        var animation = asset.Animations!.Animations[0].Token;
        var playback = new Gw2EffectModelPlayback(asset, animation, new Random(1));
        Assert.AreEqual(2, playback.Streaks.Count);
        Assert.IsTrue(playback.Streaks.All(streak => streak.AnchorBoneIndices.All(bone => bone >= 0)));

        playback.Advance(FrameSeconds, null, Matrix4x4.Identity);
        Assert.IsTrue(playback.Streaks.All(streak => streak.Simulator.Rows.Count == 0));

        for (var frame = 0; frame < 10; frame++)
            playback.Advance(FrameSeconds, frame * FrameSeconds, Matrix4x4.CreateRotationZ(frame * 0.2f));
        var blade = playback.Streaks[0];
        Assert.IsTrue(blade.Simulator.Rows.Count > 2);

        var corners = new List<Gw2ParticleCorner>();
        var camera = new Gw2ParticleCamera(new Vector3(0, -500, 0), Vector3.UnitX, Vector3.UnitZ, Vector3.UnitY);
        Gw2StreakQuads.Build(blade.Simulator, blade.Anchors, camera, corners);
        Assert.AreEqual((blade.Simulator.Rows.Count - 1) * blade.Anchors.Count / 2 * Gw2ParticleQuads.CornersPerQuad, corners.Count);
        Assert.IsTrue(corners.All(corner => float.IsFinite(corner.Position.X)));
    }

    [TestMethod]
    public void UnpacksStreakColoursAsArgb() =>
        Assert.AreEqual(new Vector4(0x7E / 255f, 0x60 / 255f, 0x28 / 255f, 0x96 / 255f), Gw2StreakQuads.UnpackArgb(0x967E6028));
}
