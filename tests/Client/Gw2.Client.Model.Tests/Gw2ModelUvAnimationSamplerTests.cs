using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2ModelUvAnimationSamplerTests
{
    private const uint EffectWindModelFileId = 88293;
    private const ulong ZeroPoseAnimationToken = 192720390330;
    private const uint AtlasUvAnimId = 28;
    private const uint NoTrack = uint.MaxValue;

    [TestMethod]
    public void MissingChannelsGiveIdentity()
    {
        var (rowA, rowB) = Gw2ModelUvAnimationSampler.TransformAt(null, new Gw2ModelUvAnimation(1, []), 0f);

        Assert.AreEqual(new Vector4(1f, 0f, 0f, 0f), rowA);
        Assert.AreEqual(new Vector4(0f, 1f, 0f, 0f), rowB);
    }

    [TestMethod]
    public void UntrackedChannelsUseTheirInitialValue()
    {
        var uv = new Gw2ModelUvAnimation(1, [
            new(Gw2ModelUvAnimationSampler.ScaleVChannel, NoTrack, NoTrack, new(0.25f, 0f, 0f, 0f)),
            new(Gw2ModelUvAnimationSampler.OffsetUChannel, NoTrack, NoTrack, new(0.5f, 0f, 0f, 0f)),
        ]);

        var (rowA, rowB) = Gw2ModelUvAnimationSampler.TransformAt(null, uv, 3f);

        Assert.AreEqual(new Vector4(1f, 0f, 0f, 0.5f), rowA);
        Assert.AreEqual(new Vector4(0f, 0.25f, 0f, 0.375f), rowB);
    }

    [TestMethod]
    public void RotationTurnsAboutTextureCentre()
    {
        var (rowA, rowB) = Gw2ModelUvAnimationSampler.Transform(MathF.PI / 2f, Vector2.One, Vector2.Zero);
        var centre = new Vector4(0.5f, 0.5f, 0f, 1f);

        Assert.AreEqual(0.5f, Vector4.Dot(rowA, centre), 1e-5f);
        Assert.AreEqual(0.5f, Vector4.Dot(rowB, centre), 1e-5f);
    }

    [TestMethod]
    public void EffectAtlasSelectsFirstStripWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var zeroPose = reader.ReadModelAnimations(EffectWindModelFileId)!.Animations.Single(animation => animation.Token == ZeroPoseAnimationToken);
        var model = reader.ReadModel(EffectWindModelFileId)!;
        var uv = zeroPose.UvAnimations.Single();
        var bound = model.Materials.SelectMany(material => material.Textures).Single(texture => texture.UvAnimId == uv.UvAnimId);

        var (rowA, rowB) = Gw2ModelUvAnimationSampler.TransformAt(null, uv, 0f);

        Assert.AreEqual(AtlasUvAnimId, uv.UvAnimId);
        Assert.AreEqual(0, bound.UvPSInputIndex);
        Assert.AreEqual(new Vector4(1f, 0f, 0f, 0f), rowA);
        Assert.AreEqual(0.1667f, rowB.Y, 1e-4f);
        Assert.AreEqual(0f, rowB.W, 1e-4f);
    }
}
