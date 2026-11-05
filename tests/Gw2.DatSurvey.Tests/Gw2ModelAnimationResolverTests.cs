using Gw2.Formats;
using System.Numerics;
using Gw2.Dat.Content;
using Gw2.Dat.Archive;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.DatSurvey.Tests;

[TestClass]
public sealed class Gw2ModelAnimationResolverTests
{
    [TestMethod]
    public void ResolvesARequestedSequenceThroughIntermediateImports()
    {
        var source3 = BankWith([Anim(0xC)]);
        var source2 = BankWith([], Import(3, 0xC));
        var root = BankWith([Anim(0xA)], Import(2, 0xC));

        var resolved = Resolve(root, (2, source2), (3, source3));

        CollectionAssert.AreEquivalent(
            new ulong[] { 0xA, 0xC },
            resolved.Animations.Select(a => a.Token).ToArray());
    }

    [TestMethod]
    public void MergesOnlyTheSequencesTheImportRequests()
    {
        var source = BankWith([Anim(0xB), Anim(0xC)]);
        var root = BankWith([Anim(0xA)], Import(2, 0xB));

        var resolved = Resolve(root, (2, source));

        CollectionAssert.AreEquivalent(
            new ulong[] { 0xA, 0xB },
            resolved.Animations.Select(a => a.Token).ToArray());
    }

    [TestMethod]
    public void OwnAnimationWinsATokenClash()
    {
        var source = BankWith([Anim(0xA, moveSpeed: 5f)]);
        var root = BankWith([Anim(0xA, moveSpeed: 1f)], Import(2, 0xA));

        var resolved = Resolve(root, (2, source));

        Assert.AreEqual(1f, resolved.Animations.Single(a => a.Token == 0xA).MoveSpeed);
    }

    [TestMethod]
    public void ResolvesImportCycles()
    {
        var root = BankWith([Anim(0xA)], Import(2, 0xB));
        var source = BankWith([Anim(0xB)], Import(1, 0xA));

        var resolved = Resolve(root, (2, source));

        CollectionAssert.AreEquivalent(
            new ulong[] { 0xA, 0xB },
            resolved.Animations.Select(a => a.Token).ToArray());
    }

    [TestMethod]
    public void IgnoresImportsThatAreAbsentFromTheArchive()
    {
        var root = BankWith([Anim(0xA)], Import(2, 0xB));

        var resolved = Resolve(root);

        Assert.AreEqual(1, resolved.Animations.Count);
        Assert.AreEqual(0xAUL, resolved.Animations[0].Token);
    }

    [TestMethod]
    public void HasLocomotionAndHasAirborneMatchTheAuthoredRoles()
    {
        var ground = BankWith([Anim(Token64.Encode("ugmwalk___f")), Anim(Token64.Encode("ugmidle"))]);
        var air = BankWith([Anim(Token64.Encode("uamjump")), Anim(Token64.Encode("uamfall"))]);
        var combat = BankWith([Anim(Token64.Encode("wgaslash"))]);

        Assert.IsTrue(Gw2LocomotionBank.HasLocomotion(ground));
        Assert.IsFalse(Gw2LocomotionBank.HasLocomotion(combat));
        Assert.IsTrue(Gw2LocomotionBank.HasAirborne(air));
        Assert.IsFalse(Gw2LocomotionBank.HasAirborne(combat));
        Assert.IsFalse(Gw2LocomotionBank.HasLocomotion(null));
    }

    private static Gw2ModelAnimation Resolve(Gw2ModelAnimation root, params (uint Id, Gw2ModelAnimation Bank)[] sources)
    {
        var map = new Dictionary<uint, Gw2ModelAnimation> { [1] = root };
        foreach (var (id, bank) in sources) map[id] = bank;
        return Gw2ModelAnimationResolver.Resolve(1, id => map.GetValueOrDefault(id));
    }

    private static Gw2ModelAnimation BankWith(Gw2ModelAnimationData[] animations, params Gw2ModelAnimationImport[] imports) =>
        new(animations, [], [], imports, [], []);

    private static Gw2ModelAnimationImport Import(uint fileId, params ulong[] sequences) =>
        new(FileRef(fileId), sequences.Select(name => new Gw2ModelAnimationImportSequence(name, 0f)).ToArray());

    private static PackFileReference FileRef(uint fileId) =>
        new((ushort)(fileId - 1 + 0x100), 0x100, 0);

    private static Gw2ModelAnimationData Anim(ulong token, float moveSpeed = 0f) =>
        new(token, moveSpeed, false, Vector3.Zero, 0f, [], []);
}
