using System;
using System.Linq;
using Gw2.Client.Net.Game;
using Gw2.Protocol.Capture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public sealed class ServerForcedMovementTests
{
    [TestMethod]
    public void DecodesTheRecordedMoveToAndJumpOrders()
    {
        var decoded = CaptureDecoding.DecodeAll(RecordedSession.Capture(), RecordedSession.Schemas);
        var gameInbound = decoded.Where(result => result.Corpus == "game" && result.Stream.Direction == CaptureRecordKind.Inbound).SelectMany(result => result.Messages).ToList();
        var moveTos = gameInbound.Select(ServerForcedMovement.MoveToOf).OfType<ServerMoveTo>().ToList();
        var jumps = gameInbound.Select(ServerForcedMovement.JumpOf).OfType<ServerJump>().ToList();
        var teleports = gameInbound.Select(ServerForcedMovement.TeleportOf).OfType<ServerTeleport>().ToList();
        Assert.IsTrue(teleports.All(teleport => float.IsFinite(teleport.PositionInGameUnits.X) && MathF.Abs(teleport.PositionInGameUnits.X) < 200000f), "teleports land in game units");
        var dashes = gameInbound.Select(ServerForcedMovement.DashOf).OfType<ServerDash>().ToList();
        foreach (var dash in dashes) Console.WriteLine(dash);
        Assert.IsTrue(dashes.All(dash => MathF.Abs(dash.CollisionDirection.Length() - 1f) < 0.02f && dash.SpeedInGameUnits > 0f), "dash directions are unit vectors");
        Console.WriteLine($"{moveTos.Count} move-to, {jumps.Count} jump, {teleports.Count} teleport, {dashes.Count} dash orders");
        if (moveTos.Count == 0) Assert.Inconclusive("the capture has no move-to order");
        foreach (var moveTo in moveTos)
        {
            Assert.IsTrue(float.IsFinite(moveTo.DestinationInWireUnits.X) && float.IsFinite(moveTo.DestinationInWireUnits.Z), moveTo.ToString());
            Assert.IsTrue(moveTo.SpeedInGameUnits is >= 0f and < 5000f, moveTo.ToString());
            Assert.IsTrue(moveTo.DurationMilliseconds is >= 0 and < 10000, moveTo.ToString());
        }
        Assert.IsTrue(moveTos.Any(moveTo => moveTo.SpeedInGameUnits == 1000f), "a leap moves at 1000");
        Assert.IsTrue(jumps.All(jump => float.IsFinite(jump.WireVerticalVelocity)));
    }
}
