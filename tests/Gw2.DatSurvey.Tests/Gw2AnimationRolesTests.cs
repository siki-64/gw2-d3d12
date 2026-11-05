using Gw2.Formats;
using System.Numerics;
using Gw2.Dat.Content;
using Gw2.Dat.Archive;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.DatSurvey.Tests;

[TestClass]
public sealed class Gw2AnimationRolesTests
{
    [TestMethod]
    public void ClassifiesTheSharedHumanoidLocomotionRoles()
    {
        Assert.AreEqual(Gw2LocomotionKind.Idle, Gw2AnimationRoles.Classify(Token64.Encode("ugmidle")));
        Assert.AreEqual(Gw2LocomotionKind.Walk, Gw2AnimationRoles.Classify(Token64.Encode("ugmwalk___f")));
        Assert.AreEqual(Gw2LocomotionKind.Run, Gw2AnimationRoles.Classify(Token64.Encode("ugmrun____f")));
        Assert.AreEqual(Gw2LocomotionKind.Run, Gw2AnimationRoles.Classify(Token64.Encode("sprint")));
        Assert.AreEqual(Gw2LocomotionKind.Jump, Gw2AnimationRoles.Classify(Token64.Encode("uamjump")));
        Assert.AreEqual(Gw2LocomotionKind.Fall, Gw2AnimationRoles.Classify(Token64.Encode("uamfall")));
        Assert.AreEqual(Gw2LocomotionKind.Land, Gw2AnimationRoles.Classify(Token64.Encode("uamland____s")));
        Assert.AreEqual(Gw2LocomotionKind.Dodge, Gw2AnimationRoles.Classify(Token64.Encode("ugmdodge__f")));
        Assert.AreEqual(Gw2LocomotionKind.Dodge, Gw2AnimationRoles.Classify(Token64.Encode("ugmevade__b")));
        Assert.AreEqual(Gw2LocomotionKind.Swim, Gw2AnimationRoles.Classify(Token64.Encode("uwmswim___f")));
        Assert.AreEqual(Gw2LocomotionKind.None, Gw2AnimationRoles.Classify(Token64.Encode("combat")));
        Assert.AreEqual(Gw2LocomotionKind.None, Gw2AnimationRoles.Classify(0));
    }

    [TestMethod]
    public void SelectsJumpFallAndLandFromABank()
    {
        var jump = Animation(Token64.Encode("uamjump"), 0f);
        var fall = Animation(Token64.Encode("uamfall"), 0f);
        var land = Animation(Token64.Encode("uamland____s"), 0f);
        var walk = Animation(Token64.Encode("ugmwalk___f"), 120f);

        var selection = Gw2Locomotion.SelectAirborne(new[] { walk, land, fall, jump });

        Assert.AreSame(jump, selection.Jump);
        Assert.AreSame(fall, selection.Fall);
        Assert.AreSame(land, selection.Land);
    }

    [TestMethod]
    public void DecodesKnownRoleNames()
    {
        Assert.AreEqual("zeropose", Gw2AnimationRoles.Name(0x2CDF07C8BAUL));
        Assert.AreEqual("risen_undead", Gw2AnimationRoles.Name(Token64.Encode("risen_undead")));
    }

    [TestMethod]
    public void ClassifiesDiscreteRolesAsActions()
    {
        Assert.AreEqual(Gw2LocomotionKind.None, Gw2AnimationRoles.Classify(Token64.Encode("ughhit")));
        Assert.AreEqual(Gw2LocomotionKind.None, Gw2AnimationRoles.Classify(Token64.Encode("ugddeath")));
        Assert.AreEqual(Gw2LocomotionKind.None, Gw2AnimationRoles.Classify(Token64.Encode("ugmalert")));
        Assert.AreEqual(Gw2LocomotionKind.None, Gw2AnimationRoles.Classify(Token64.Encode("ugathrust")));
        Assert.AreEqual(Gw2LocomotionKind.None, Gw2AnimationRoles.Classify(Token64.Encode("wgaslash")));
        Assert.IsFalse(Gw2AnimationRoles.IsLocomotion(Token64.Encode("wgaslash")));
        Assert.IsTrue(Gw2AnimationRoles.IsLocomotion(Token64.Encode("ugmwalk___f")));
    }

    [TestMethod]
    public void MatchesAMachineRoleFamilyToTheBankClip()
    {
        var machine = Token64.Encode("_gmwalk___f");
        var bank = Token64.Encode("ugmwalk___f");

        Assert.AreNotEqual(machine, bank);
        Assert.IsTrue(Gw2AnimationRoles.SharesRoleFamily(machine, bank));
        Assert.AreEqual(0x180002D81BB4E0UL, Gw2AnimationRoles.RoleFamily(machine));
        Assert.AreEqual(Gw2AnimationRoles.RoleFamily(machine), Gw2AnimationRoles.RoleFamily(bank));
        Assert.IsFalse(Gw2AnimationRoles.SharesRoleFamily(machine, Token64.Encode("ugmrun____f")));
    }

    [TestMethod]
    public void NamesTheRoleStateByToken()
    {
        Assert.AreEqual("anim_2CDF07C8BA", Gw2AnimationRoles.ClipName(0x2CDF07C8BAUL));
        Assert.AreEqual("anim_289420F5", Gw2AnimationRoles.ClipName(0x289420F5UL));
    }

    [TestMethod]
    public void SelectsIdleWalkAndRunFromABank()
    {
        var idle = Animation(Token64.Encode("ugmidle"), 0f);
        var walk = Animation(Token64.Encode("ugmwalk___f"), 120f);
        var run = Animation(Token64.Encode("ugmrun____f"), 240f);
        var combat = Animation(Token64.Encode("combat"), 0f);

        var selection = Gw2Locomotion.Select(new[] { combat, walk, run, idle });

        Assert.AreSame(idle, selection.Idle);
        Assert.AreSame(walk, selection.Walk);
        Assert.AreSame(run, selection.Run);
    }

    [TestMethod]
    public void SelectsNothingWhenTheBankHasNoLocomotion()
    {
        var selection = Gw2Locomotion.Select(new[] { Animation(Token64.Encode("zeropose"), 0f) });

        Assert.IsNull(selection.Idle);
        Assert.IsNull(selection.Walk);
        Assert.IsNull(selection.Run);
    }

    private static Gw2ModelAnimationData Animation(ulong token, float moveSpeed) =>
        new(token, moveSpeed, false, Vector3.Zero, 0f, [], []);
}
