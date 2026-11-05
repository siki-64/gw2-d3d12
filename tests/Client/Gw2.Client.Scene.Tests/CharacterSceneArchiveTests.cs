using Gw2.Formats;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Gw2.Client.Content;
using Gw2.Client.Scene;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Scene.Tests;

[TestClass]
public sealed class CharacterSceneArchiveTests
{
    [TestMethod]
    public void SettlesHumanFemaleBareBodyWithSkeletonWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        using var content = new Gw2ContentService(reader);
        var character = new CharacterScene(content, "HumanFemale");

        var clock = Stopwatch.StartNew();
        while (!character.IsSettled && clock.Elapsed < TimeSpan.FromMinutes(2))
        {
            character.Update(0f);
            Thread.Sleep(10);
        }

        character.Update(0f);
        Assert.IsTrue(character.IsSettled);
        Assert.IsNull(character.Error);
        Assert.IsNotNull(character.Skeleton);
        Assert.IsNotNull(character.IdleClip);
        Assert.IsTrue(character.IdleClip!.Duration > 0f);
        Assert.AreEqual(character.Skeleton!.Bones.Count, character.SkinPalette.Length);
        Assert.AreEqual(Gw2CharacterBodyParts.BareBody.Count, character.Parts.Count);
        Assert.IsTrue(character.Parts.All(part => part.Meshes.Any(mesh => mesh.IsRenderable && mesh.LodLevel == 0)));
    }

    [TestMethod]
    public void RepeatedRequestsShareOneDecodeWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        using var content = new Gw2ContentService(reader);
        var decodeCount = 0;
        Gw2Composite? Decode(Gw2ContentReader source, uint fileId)
        {
            Interlocked.Increment(ref decodeCount);
            return source.ReadComposite(fileId);
        }

        var first = content.Request<Gw2Composite>(Gw2RaceAnimations.CompositeFileId, Decode);
        var second = content.Request<Gw2Composite>(Gw2RaceAnimations.CompositeFileId, Decode);
        first.Completion.Wait();

        Assert.AreSame(first, second);
        Assert.AreEqual(1, decodeCount);
        Assert.IsNotNull(first.Result);
    }
}
