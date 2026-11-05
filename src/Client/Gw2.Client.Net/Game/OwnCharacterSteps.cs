using System;
using System.Numerics;

namespace Gw2.Client.Net.Game;

public sealed class OwnCharacterSteps
{
    public static readonly TimeSpan StepLength = TimeSpan.FromMilliseconds(40);

    private const byte GroundState = 0;
    private const byte InAirState = 1;
    private const byte TakingOffState = 2;
    private const uint NoFlags = 0;
    private const byte NoMotionFlags = 0;
    private const byte AirborneMotionFlags = 2;
    private const byte UnwrittenByte = 0;

    private long _lastStep = -1;
    private Vector3? _lastSentPosition;
    private Vector3 _lastSentVelocity;
    private bool _sendsOneMoreUnchanged;
    private byte _lastState = GroundState;
    private byte _motionFlags = NoMotionFlags;

    public object[]? StateToSend(long step, Vector3 positionInCollisionUnits, bool isAirborne = false, OwnPhysicsState? physics = null)
    {
        if (step <= _lastStep) return null;
        _lastStep = step;
        if (physics is { } state)
        {
            _motionFlags = state.MotionFlags;
            _lastState = state.State;
        }
        else
        {
            _motionFlags = isAirborne && _lastState == InAirState ? AirborneMotionFlags : NoMotionFlags;
            _lastState = !isAirborne ? GroundState : _lastState == GroundState ? TakingOffState : InAirState;
        }
        var velocity = _lastSentPosition is { } previous ? positionInCollisionUnits - previous : Vector3.Zero;
        if (_lastSentPosition != positionInCollisionUnits || velocity != _lastSentVelocity)
        {
            _lastSentPosition = positionInCollisionUnits;
            _lastSentVelocity = velocity;
            _sendsOneMoreUnchanged = true;
            return StateValues(step, positionInCollisionUnits, _motionFlags, _lastState);
        }
        if (!_sendsOneMoreUnchanged) return null;
        _sendsOneMoreUnchanged = false;
        return StateValues(step, positionInCollisionUnits, _motionFlags, _lastState);
    }

    public static Vector3 CollisionUnitsOf(Vector3 gameUnits) => DirectionInCollisionUnits(gameUnits) / GameAgents.GameUnitsPerCollisionUnit;

    public static Vector3 DirectionInCollisionUnits(Vector3 gameUnits) => new(gameUnits.X, gameUnits.Y, -gameUnits.Z);

    private static object[] StateValues(long step, Vector3 position, byte motionFlags, byte state)
    {
        var bytes = new byte[12];
        BitConverter.TryWriteBytes(bytes.AsSpan(0, 4), position.X);
        BitConverter.TryWriteBytes(bytes.AsSpan(4, 4), position.Y);
        BitConverter.TryWriteBytes(bytes.AsSpan(8, 4), position.Z);
        return [(uint)step, (uint)step, bytes, NoFlags, motionFlags, state, UnwrittenByte];
    }
}
