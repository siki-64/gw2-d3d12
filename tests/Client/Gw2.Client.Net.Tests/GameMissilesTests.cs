using System;
using System.Numerics;
using Gw2.Client.Net.Game;
using Gw2.Protocol.MsgPack;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public sealed class GameMissilesTests
{
    private static readonly Vector3 LiveOrigin = new(8193.784f, 33131.906f, -1606.7498f);
    private static readonly Vector3 LiveTarget = new(8708.826f, 33175.523f, -1696.5013f);
    private const ulong LiveSpeedBits = 1153138688;

    [TestMethod]
    public void AStraightMissileReachesItsPointAfterDistanceOverSpeed()
    {
        var missiles = Launched(MissileMotionKind.Straight, 0);
        missiles.TryGet(69, out var missile);
        Assert.AreEqual(1500f, missile.Speed);
        float flight = Vector3.Distance(LiveOrigin, LiveTarget) / 1500f;
        Assert.AreEqual(flight, missile.FlightSeconds, 1e-5f);
        missiles.Advance(flight / 2, NoAgents);
        missiles.TryGet(69, out missile);
        AssertNear(Vector3.Lerp(LiveOrigin, LiveTarget, 0.5f), missile.PositionInGameUnits);
        Assert.IsFalse(missile.HasArrived);
        missiles.Advance(flight, NoAgents);
        missiles.TryGet(69, out missile);
        AssertNear(LiveTarget, missile.PositionInGameUnits);
        Assert.IsTrue(missile.HasArrived);
    }

    [TestMethod]
    public void AnArcLandsOnItsPointAtTheHorizontalFlightTime()
    {
        var missiles = Launched(MissileMotionKind.Arc, 0);
        missiles.TryGet(69, out var missile);
        var delta = LiveTarget - LiveOrigin;
        float flight = MathF.Sqrt(delta.X * delta.X + delta.Y * delta.Y) / 1500f;
        Assert.AreEqual(flight, missile.FlightSeconds, 1e-5f);
        missiles.Advance(flight / 2, NoAgents);
        missiles.TryGet(69, out missile);
        float straightMidZ = (LiveOrigin.Z + LiveTarget.Z) / 2;
        Assert.IsTrue(missile.PositionInGameUnits.Z < straightMidZ, $"apex {missile.PositionInGameUnits.Z} is above {straightMidZ}");
        missiles.Advance(flight / 2, NoAgents);
        missiles.TryGet(69, out missile);
        AssertNear(LiveTarget, missile.PositionInGameUnits);
    }

    [TestMethod]
    public void AHomingMissileReaimsAtItsTargetAndLeavesOnRemove()
    {
        var target = LiveTarget;
        Vector3? AgentAt(uint agentId) => agentId == 6902 ? target : null;
        var missiles = Launched(MissileMotionKind.Straight, 6902, AgentAt);
        target += new Vector3(0f, 300f, 0f);
        missiles.Advance(GameMissiles.UpdatePeriodSeconds, AgentAt);
        missiles.TryGet(69, out var missile);
        Assert.AreEqual(target, missile.TargetPointInGameUnits);
        Assert.IsFalse(missile.HasArrived);
        missiles.Handle(Message(GameMissiles.MissileRemove, 69), AgentAt);
        Assert.IsFalse(missiles.TryGet(69, out _));
    }

    private static GameMissiles Launched(MissileMotionKind kind, ulong targetAgent, Func<uint, Vector3?>? agents = null)
    {
        agents ??= NoAgents;
        var missiles = new GameMissiles();
        missiles.Handle(Message(GameMissiles.MissileCreate, 69, 4494, 3199, 5047, Point(LiveOrigin), 0UL, 0UL), agents);
        missiles.Handle(Message(GameMissiles.MissileMotion, 69, (ulong)kind, 0UL, targetAgent, Point(LiveTarget), LiveSpeedBits, 4UL, new byte[8], 4UL), agents);
        return missiles;
    }

    private static Vector3? NoAgents(uint agentId) => null;

    private static void AssertNear(Vector3 expected, Vector3 actual) =>
        Assert.IsTrue(Vector3.Distance(expected, actual) < 0.5f, $"expected {expected}, got {actual}");

    private static DecodedArrayHeader Point(Vector3 point)
    {
        var header = new byte[16];
        BitConverter.TryWriteBytes(header.AsSpan(0), point.X);
        BitConverter.TryWriteBytes(header.AsSpan(4), point.Y);
        BitConverter.TryWriteBytes(header.AsSpan(8), point.Z);
        return new DecodedArrayHeader(header, 0);
    }

    private static DecodedMessage Message(int id, params object[] values)
    {
        var fields = new DecodedField[values.Length + 1];
        fields[0] = new DecodedField(0, true, (ulong)id);
        for (int i = 0; i < values.Length; i++) fields[i + 1] = new DecodedField(0, true, values[i] is int number ? (ulong)number : values[i]);
        return new DecodedMessage(id, 0, 0, fields);
    }
}
