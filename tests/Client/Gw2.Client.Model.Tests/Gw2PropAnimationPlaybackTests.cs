using Gw2.Formats;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2PropAnimationPlaybackTests
{
    private const uint MapFileId = 187611;
    private const uint CompoundUvModelFileId = 73522;
    private const ulong CompoundStartToken = 0x40000000000001CF;
    private const ulong CompoundLoopToken = 0x80000000000001CF;

    [TestMethod]
    public void ResolvesMapPropTokensToSurfaceAnimationsWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var props = Gw2MapProps.TryParse(reader.ReadDocument(MapFileId))!;
        var animated = props.Placements.Where(placement => placement.AnimSequence is not null).ToList();
        var playbacks = animated.GroupBy(placement => (placement.ModelFileId, placement.AnimSequence!.Value))
            .ToDictionary(group => group.Key, group => Gw2PropAnimationPlayback.For(reader.ReadStreamedModelAnimations(group.Key.ModelFileId), group.Key.Value));
        Assert.AreEqual(141, animated.Count);
        Assert.IsTrue(playbacks.Values.Count(playback => playback is not null) >= 9);

        var compound = playbacks[(CompoundUvModelFileId, 0x1CF)]!;
        Assert.AreEqual(CompoundStartToken, compound.ClipAt(0f).Clip.Data.Token);
        var startDuration = compound.ClipAt(0f).Clip.Duration;
        Assert.AreEqual(CompoundLoopToken, compound.ClipAt(startDuration + 0.01f).Clip.Data.Token);
        var loopDuration = compound.ClipAt(startDuration + 0.01f).Clip.Duration;
        var heldEnd = compound.ClipAt(startDuration + loopDuration * 3f, loops: false);
        Assert.AreEqual(CompoundLoopToken, heldEnd.Clip.Data.Token);
        Assert.AreEqual(loopDuration, heldEnd.Seconds, 1e-4f);

        var model = reader.ReadModel(CompoundUvModelFileId)!;
        var material = Gw2ModelBuilder.Build(CompoundUvModelFileId, model, _ => null).Materials.First(candidate => candidate.Textures.Any(texture => texture.UvAnimId != 0));
        var early = new Dictionary<uint, Vector4>();
        var later = new Dictionary<uint, Vector4>();
        compound.WriteSurface(material, early, startDuration + 0.1f);
        compound.WriteSurface(material, later, startDuration + 0.6f);
        Assert.IsTrue(early.Keys.Any(Gw2ModelUvAnimationSampler.TexTransformRowTokens.Contains));
        Assert.IsTrue(early.Any(pair => later[pair.Key] != pair.Value));
    }
}
