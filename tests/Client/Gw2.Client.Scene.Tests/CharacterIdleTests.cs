using Gw2.Formats;
using System;
using System.IO;
using Gw2.Client.Scene;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Scene.Tests;

[TestClass]
public sealed class CharacterIdleTests
{
    private const uint HumanSkeletonFileId = 29193;
    private const ulong UnarmedGroundIdleToken = 0x15844B4F5;

    [TestMethod]
    public void PlaysTheGroundIdleStateOfTheModelMachineWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var bank = reader.ReadModelAnimations(HumanSkeletonFileId)!;
        var idle = Gw2CharacterIdle.MachineIdle(reader.ReadAnimMachines(), bank, HumanSkeletonFileId);

        Assert.IsNotNull(idle);
        Assert.AreEqual(UnarmedGroundIdleToken, idle.Token);
    }

    [TestMethod]
    public void DecodesAnIdlePlaybackThatKeepsItsClipTimeInRange()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var playback = Gw2CharacterIdle.Decode(reader, HumanSkeletonFileId);

        Assert.IsNotNull(playback?.Clip);
        for (var frame = 0; frame < 600; frame++)
        {
            playback.Advance(0.1f);
            Assert.IsNotNull(playback.Clip);
            Assert.IsTrue(playback.Seconds >= 0f && playback.Seconds <= playback.Clip.Duration);
        }
    }
}
