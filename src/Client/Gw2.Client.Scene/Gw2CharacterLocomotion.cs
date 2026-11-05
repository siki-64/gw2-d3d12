using System;
using System.Collections.Generic;
using System.Numerics;

namespace Gw2.Client.Scene;

public enum Gw2MoveDirection
{
    Forward = 0,
    ForwardLeft = 1,
    Left = 2,
    BackwardLeft = 3,
    Backward = 4,
    BackwardRight = 5,
    Right = 6,
    ForwardRight = 7,
}

public static class Gw2CharacterLocomotion
{
    public const float DefaultWalkThreshold = 105f;
    public const float DefaultRunThreshold = 280f;
    public const float WalkThresholdMargin = 20f;
    public const float SlideVelocitySquaredThreshold = 11025f;

    public const int GroundAlert = 41;
    public const int GroundIdle = 62;
    public const int GroundTurnLeft = 95;
    public const int GroundTurnRight = 96;

    private const float ForwardCosine = 0.92388f;
    private const float DiagonalCosine = 0.38268f;

    private static readonly int[] SlowStates = [100, 101, 103, 98, 97, 99, 104, 102];
    private static readonly int[] WalkToRunStates = [82, 83, 85, 98, 97, 99, 86, 84];
    private static readonly int[] SprintStates = [90, 91, 85, 98, 97, 99, 86, 92];
    private static readonly int[] SlideStates = [88, 88, 88, 87, 87, 87, 88, 88];

    public static readonly IReadOnlyDictionary<int, string> StateNames = new Dictionary<int, string>
    {
        [GroundAlert] = "Ground Alert",
        [GroundIdle] = "Ground Idle",
        [82] = "Ground Run Forward",
        [83] = "Ground Run Forward Left",
        [84] = "Ground Run Forward Right",
        [85] = "Ground Run Left",
        [86] = "Ground Run Right",
        [87] = "Ground Slide Backward",
        [88] = "Ground Slide Forward",
        [90] = "Ground Sprint Forward",
        [91] = "Ground Sprint Forward Left",
        [92] = "Ground Sprint Forward Right",
        [GroundTurnLeft] = "Ground Turn Left",
        [GroundTurnRight] = "Ground Turn Right",
        [97] = "Ground Walk Backward",
        [98] = "Ground Walk Backward Left",
        [99] = "Ground Walk Backward Right",
        [100] = "Ground Walk Forward",
        [101] = "Ground Walk Forward Left",
        [102] = "Ground Walk Forward Right",
        [103] = "Ground Walk Left",
        [104] = "Ground Walk Right",
    };

    public static Gw2MoveDirection DirectionOf(Vector2 facing, Vector2 moveDirection, Gw2MoveDirection previous)
    {
        if (moveDirection.LengthSquared() == 0f || facing.LengthSquared() == 0f) return previous;
        var f = Vector2.Normalize(facing);
        var m = Vector2.Normalize(moveDirection);
        var dot = Vector2.Dot(f, m);
        var isLeft = f.X * m.Y - f.Y * m.X >= 0f;
        if (dot > ForwardCosine) return Gw2MoveDirection.Forward;
        if (dot < -ForwardCosine) return Gw2MoveDirection.Backward;
        if (dot > DiagonalCosine) return isLeft ? Gw2MoveDirection.ForwardLeft : Gw2MoveDirection.ForwardRight;
        if (dot >= -DiagonalCosine) return isLeft ? Gw2MoveDirection.Left : Gw2MoveDirection.Right;
        return isLeft ? Gw2MoveDirection.BackwardLeft : Gw2MoveDirection.BackwardRight;
    }

    public static int MovingStateOf(float speed, Gw2MoveDirection direction, float walkThreshold, float runThreshold, Func<int, bool> machineHasState)
    {
        ArgumentNullException.ThrowIfNull(machineHasState);
        var index = (int)direction;
        if (speed < walkThreshold) return SlowStates[index];
        if (speed < runThreshold) return WalkToRunStates[index];
        return machineHasState(SprintStates[index]) ? SprintStates[index] : WalkToRunStates[index];
    }

    public static int StandingStateOf(float turnRate, bool inCombat) =>
        turnRate > 0f ? GroundTurnLeft : turnRate < 0f ? GroundTurnRight : inCombat ? GroundAlert : GroundIdle;

    public static int SlidingStateOf(Gw2MoveDirection direction) => SlideStates[(int)direction];

    public static float WalkThresholdOf(float? speciesWalkSpeed) =>
        speciesWalkSpeed is { } walk ? walk + WalkThresholdMargin : DefaultWalkThreshold;

    public static float RunThresholdOf(float? speciesRunSpeed) =>
        speciesRunSpeed is > 0f and { } run ? run : DefaultRunThreshold;
}
