using System;
using System.IO;
using Gw2.Client.Model;
using Gw2.Dat.Content;
using Gw2.Formats;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Scene.Tests;

[TestClass]
public sealed class CharacterSocketTests
{
    private const uint HumanSkeletonFileId = 29193;
    private const uint WeaponSkinModelFileId = 2595969;

    [TestMethod]
    public void HumanSkeletonHasEverySocketBoneWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var skeleton = reader.ReadModelSkeleton(HumanSkeletonFileId)!;
        foreach (var socket in new[] { "root", "cholsterback", "lholsterback", "rholsterback", "lefthand", "righthand", "shield", "lholsterhip", "rholsterhip" })
            Assert.IsTrue(Gw2ModelLink.ChildBoneIndex(skeleton, Token64.EncodeAfterColon(socket)) >= 0, socket);
    }

    [TestMethod]
    public void WeaponSkinModelHasTheHandGripBoneWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var skeleton = reader.ReadModelSkeleton(WeaponSkinModelFileId)!;
        Assert.IsTrue(Gw2ModelLink.ChildBoneIndex(skeleton, Token64.EncodeAfterColon("rgriphand")) >= 0);
    }
}
