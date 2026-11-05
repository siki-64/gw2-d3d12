using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2EmoteAnimationsTests
{
    private const uint EmoteAnimationsFileId = 184803;
    private const ulong IntroLoopOutroEmoteToken = 53285;

    [TestMethod]
    public void ReadsEmoteTimingsWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var emotes = Gw2EmoteAnimations.TryParse(reader.ReadDocument(EmoteAnimationsFileId))!;

        Assert.AreEqual(314, emotes.Animations.Count);
        Assert.AreEqual(1474, emotes.Animations.Sum(animation => animation.Timings.Count));

        var timing = emotes.Animations.Single(animation => animation.Token == IntroLoopOutroEmoteToken).Timings[0];
        Assert.AreEqual(1760015u, timing.AnimationFileId);
        Assert.AreEqual(0.25f, timing.BlendIn);
        Assert.AreEqual(0.25f, timing.BlendOut);
        Assert.AreEqual(1566u, timing.IntroDuration);
        Assert.AreEqual(3366u, timing.LoopDuration);
        Assert.AreEqual(1733u, timing.OutroDuration);
    }
}
