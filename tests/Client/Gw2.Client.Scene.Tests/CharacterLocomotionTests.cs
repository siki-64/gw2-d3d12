using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Gw2.Client.Scene;
using Gw2.Dat.Content;
using Gw2.Formats;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Scene.Tests;

[TestClass]
public sealed class CharacterLocomotionTests
{
    private const uint HumanSkeletonFileId = 29193;

    [TestMethod]
    public void SplitsTheCircleIntoEightDirectionsWithLeftOnThePositiveCrossSide()
    {
        var facing = Vector2.UnitX;
        Assert.AreEqual(Gw2MoveDirection.Forward, Gw2CharacterLocomotion.DirectionOf(facing, new Vector2(1f, 0.1f), Gw2MoveDirection.Right));
        Assert.AreEqual(Gw2MoveDirection.ForwardLeft, Gw2CharacterLocomotion.DirectionOf(facing, new Vector2(1f, 1f), Gw2MoveDirection.Forward));
        Assert.AreEqual(Gw2MoveDirection.Left, Gw2CharacterLocomotion.DirectionOf(facing, Vector2.UnitY, Gw2MoveDirection.Forward));
        Assert.AreEqual(Gw2MoveDirection.BackwardLeft, Gw2CharacterLocomotion.DirectionOf(facing, new Vector2(-1f, 1f), Gw2MoveDirection.Forward));
        Assert.AreEqual(Gw2MoveDirection.Backward, Gw2CharacterLocomotion.DirectionOf(facing, -Vector2.UnitX, Gw2MoveDirection.Forward));
        Assert.AreEqual(Gw2MoveDirection.BackwardRight, Gw2CharacterLocomotion.DirectionOf(facing, new Vector2(-1f, -1f), Gw2MoveDirection.Forward));
        Assert.AreEqual(Gw2MoveDirection.Right, Gw2CharacterLocomotion.DirectionOf(facing, -Vector2.UnitY, Gw2MoveDirection.Forward));
        Assert.AreEqual(Gw2MoveDirection.ForwardRight, Gw2CharacterLocomotion.DirectionOf(facing, new Vector2(1f, -1f), Gw2MoveDirection.Forward));
        Assert.AreEqual(Gw2MoveDirection.Right, Gw2CharacterLocomotion.DirectionOf(facing, Vector2.Zero, Gw2MoveDirection.Right));
    }

    [TestMethod]
    public void PicksWalkRunAndSprintBySpeedAndKeepsBackpedalAWalk()
    {
        var walk = Gw2CharacterLocomotion.WalkThresholdOf(null);
        var run = Gw2CharacterLocomotion.RunThresholdOf(null);
        Assert.AreEqual(100, Gw2CharacterLocomotion.MovingStateOf(50f, Gw2MoveDirection.Forward, walk, run, _ => true));
        Assert.AreEqual(82, Gw2CharacterLocomotion.MovingStateOf(200f, Gw2MoveDirection.Forward, walk, run, _ => true));
        Assert.AreEqual(90, Gw2CharacterLocomotion.MovingStateOf(300f, Gw2MoveDirection.Forward, walk, run, _ => true));
        Assert.AreEqual(82, Gw2CharacterLocomotion.MovingStateOf(300f, Gw2MoveDirection.Forward, walk, run, _ => false));
        Assert.AreEqual(97, Gw2CharacterLocomotion.MovingStateOf(300f, Gw2MoveDirection.Backward, walk, run, _ => true));
        Assert.AreEqual(125f, Gw2CharacterLocomotion.WalkThresholdOf(105f));
    }

    [TestMethod]
    public void DecodesTheHumanGroundLocomotionStatesFromTheMachine()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var states = Gw2CharacterLocomotionStates.Decode(reader, HumanSkeletonFileId);

        Assert.IsTrue(states.Has(100) && states.Has(82));
        Assert.IsTrue(states.Find(82)!.Clip.MoveSpeed > 0f);
        Assert.AreEqual(0.25f, states.IdleBlendInSeconds, 1e-4f);
        Assert.AreEqual(-0.70710677f, states.Find(103)!.Rotation.W * MathF.Sign(states.Find(103)!.Rotation.Z), 1e-5f);
        Assert.IsTrue(states.Find(103)!.KeepsClipTime);

    }

    [TestMethod]
    public void DecodesGroundLocomotionForEveryRaceAndSex()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var composite = reader.ReadComposite(Gw2RaceAnimations.CompositeFileId)!;
        for (var race = 0; race < Gw2CharacterRaces.RaceCount; race++)
        for (var sex = 0; sex < Gw2CharacterRaces.SexCount; sex++)
        {
            var name = Gw2CharacterRaces.CompositeRaceNameOf(race, sex)!;
            var bodies = Gw2CharacterBodyParts.FindRace(composite, name);
            Assert.IsNotNull(bodies, name);
            Assert.IsTrue(bodies.SkeletonFile.TryGetFileId(out var skeletonFileId), name);
            var states = Gw2CharacterLocomotionStates.Decode(reader, skeletonFileId);
            Console.WriteLine($"{name} skeleton {skeletonFileId} states {states.StatesById.Count} idle blend {states.IdleBlendInSeconds} missing {string.Join(",", Gw2CharacterLocomotion.StateNames.Keys.Where(id => id != Gw2CharacterLocomotion.GroundIdle && !states.Has(id)))}");
            Assert.IsTrue(states.Has(100) && states.Has(82) && states.Has(97), name);
        }
    }

    [TestMethod]
    public void KeepsTheBlendedPoseWhenAStateChangesMidFade()
    {
        var clip = new Gw2.Client.Model.Gw2AnimationClip { Duration = 1f, MoveSpeed = 100f };
        var walk = new Gw2LocomotionState(100, clip, 0.25f, Quaternion.Identity, false);
        var run = new Gw2LocomotionState(82, clip, 0.25f, Quaternion.Identity, false);
        var animator = new Gw2CharacterAnimator();
        animator.Play(100, walk, 0.25f);
        animator.Advance(0.125f, 100f);
        animator.Play(82, run, 0.25f);
        animator.Advance(0.0625f, 100f);
        var weights = animator.Samples(clip, 0f).Select(sample => sample.Weight).ToArray();

        Assert.AreEqual(3, weights.Length);
        Assert.AreEqual(1f, weights.Sum(), 1e-5f);
        Assert.AreEqual(0.375f, weights[0], 1e-5f);
        Assert.AreEqual(0.375f, weights[1], 1e-5f);
    }
}
