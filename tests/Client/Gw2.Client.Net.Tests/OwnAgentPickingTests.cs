using System.Linq;
using System.Numerics;
using Gw2.Client.Net.Game;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public sealed class OwnAgentPickingTests
{
    private static readonly Vector3 Eye = new(0f, -500f, -100f);
    private static readonly Matrix4x4 WorldToClip =
        Matrix4x4.CreateLookAtLeftHanded(Eye, new Vector3(0f, 0f, -100f), -Vector3.UnitZ) *
        Matrix4x4.CreatePerspectiveFieldOfViewLeftHanded(1f, 16f / 9f, 1f, 36864f);
    private static readonly PickRay RayThroughOrigin = new(Eye, new Vector3(0f, 1000f, -100f), Eye, float.PositiveInfinity);

    private static Vector2 CursorOn(Vector3 world) => OwnAgentPicking.Project(world, WorldToClip).Point;

    [TestMethod]
    public void ScoresOneWithTheCursorOnTheBodySegment()
    {
        var agent = new PickCandidate(7, Vector3.Zero, 160f, 30f);
        Assert.AreEqual(1f, OwnAgentPicking.ScoreOf(agent, WorldToClip, CursorOn(new Vector3(0f, 0f, -80f)), RayThroughOrigin, isSelection: false), 1e-5f);
    }

    [TestMethod]
    public void RaisesTheScoreToTheFourthPowerExceptForTheSelection()
    {
        var agent = new PickCandidate(7, Vector3.Zero, 160f, 30f);
        var cursor = CursorOn(new Vector3(0f, 0f, -80f)) + new Vector2(0.03f, 0f);
        var raw = OwnAgentPicking.ScoreOf(agent, WorldToClip, cursor, RayThroughOrigin, isSelection: true);
        var powered = OwnAgentPicking.ScoreOf(agent, WorldToClip, cursor, RayThroughOrigin, isSelection: false);
        Assert.IsTrue(raw is > 0f and < 1f);
        Assert.AreEqual(raw * raw * raw * raw, powered, 1e-6f);
    }

    [TestMethod]
    public void ScoresZeroBehindTheRayOrOutsideTheDepthAllowance()
    {
        var behind = new PickCandidate(7, new Vector3(0f, -1000f, 0f), 160f, 30f);
        Assert.AreEqual(0f, OwnAgentPicking.ScoreOf(behind, WorldToClip, Vector2.Zero, RayThroughOrigin, isSelection: false));
        var agent = new PickCandidate(7, Vector3.Zero, 160f, 30f);
        Assert.AreEqual(0f, OwnAgentPicking.ScoreOf(agent, WorldToClip, CursorOn(new Vector3(0f, 0f, -80f)) + new Vector2(0.19f, 0f), RayThroughOrigin, isSelection: false));
    }

    [TestMethod]
    public void SkipsCandidatesBeyondTheCursorHitUnlessSelected()
    {
        var agent = new PickCandidate(7, Vector3.Zero, 160f, 30f);
        var shortRay = RayThroughOrigin with { CursorHitDistanceInGameUnits = 100f };
        var cursor = CursorOn(new Vector3(0f, 0f, -80f));
        Assert.AreEqual(0f, OwnAgentPicking.ScoreOf(agent, WorldToClip, cursor, shortRay, isSelection: false));
        Assert.AreEqual(1f, OwnAgentPicking.ScoreOf(agent, WorldToClip, cursor, shortRay, isSelection: true), 1e-5f);
    }

    [TestMethod]
    public void HoversTheCandidateClosestToTheCursor()
    {
        var near = new PickCandidate(1, Vector3.Zero, 160f, 30f);
        var beside = new PickCandidate(2, new Vector3(40f, 0f, 0f), 160f, 30f);
        var outOfRange = new PickCandidate(3, new Vector3(0f, 5000f, 0f), 160f, 30f);
        var hovered = OwnAgentPicking.HoveredAgent([near, beside, outOfRange], WorldToClip, CursorOn(new Vector3(38f, 0f, -80f)), RayThroughOrigin, Vector3.Zero, null);
        Assert.AreEqual(2u, hovered);
    }

    [TestMethod]
    public void ClickSelectsTheAgentHoveredAtPressAndRelease()
    {
        var click = new OwnClickSelection();
        click.Press(9, new Vector2(100f, 100f), 1000);
        Assert.IsTrue(click.TryRelease(9, new Vector2(104f, 100f), 1200, 1920f, out var selected));
        Assert.AreEqual(9u, selected);
    }

    [TestMethod]
    public void IgnoresSlowDraggedOrChangedClicks()
    {
        var click = new OwnClickSelection();
        click.Press(9, Vector2.Zero, 0);
        Assert.IsFalse(click.TryRelease(9, Vector2.Zero, 251, 1920f, out _));
        click.Press(9, Vector2.Zero, 0);
        Assert.IsFalse(click.TryRelease(9, new Vector2(10f, 0f), 100, 1920f, out _));
        click.Press(9, Vector2.Zero, 0);
        Assert.IsFalse(click.TryRelease(4, Vector2.Zero, 100, 1920f, out _));
    }

    [TestMethod]
    public void ClickOnNothingClears()
    {
        var click = new OwnClickSelection();
        click.Press(null, Vector2.Zero, 0);
        Assert.IsTrue(click.TryRelease(null, Vector2.Zero, 100, 1920f, out var selected));
        Assert.IsNull(selected);
    }

    [TestMethod]
    public void SendsSelectedAgentThenActionTarget()
    {
        var movement = new OwnCharacterMovement();
        var select = movement.CommandsToSend(6550, 100, 0, new OwnMovementInput(0, Vector3.Zero, Vector3.UnitX, ClickedSelection: new OwnTargetSelection(1, 5254)), Vector3.Zero, 9f);
        CollectionAssert.AreEqual(new[] { "eb:5254", "e3:5254" }, select.Select(MessageOf).ToArray());
        var clear = movement.CommandsToSend(6550, 101, 0, new OwnMovementInput(0, Vector3.Zero, Vector3.UnitX, ClickedSelection: new OwnTargetSelection(2, 0)), Vector3.Zero, 9f);
        CollectionAssert.AreEqual(new[] { "e3:0" }, clear.Select(MessageOf).ToArray());
    }

    [TestMethod]
    public void SendsTheLiveWaypointTravelOncePerPress()
    {
        var movement = new OwnCharacterMovement();
        var travel = new OwnMovementInput(0, Vector3.Zero, Vector3.UnitX, WaypointTravel: new OwnWaypointTravel(1, 92));
        CollectionAssert.AreEqual(new[] { "d0:92" }, movement.CommandsToSend(7699, 100, 0, travel, Vector3.Zero, 9f).Select(MessageOf).ToArray());
        CollectionAssert.AreEqual(System.Array.Empty<string>(), movement.CommandsToSend(7699, 101, 0, travel, Vector3.Zero, 9f).Select(MessageOf).ToArray());
    }

    private static string MessageOf(Gw2.Protocol.MsgPack.DecodedField[] command) => $"{(ulong)command[0].Value!:x}:{(ulong)command[1].Value!}";
}
