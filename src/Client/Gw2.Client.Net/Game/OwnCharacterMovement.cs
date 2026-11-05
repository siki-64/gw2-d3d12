using System;
using System.Collections.Generic;
using System.Numerics;
using Gw2.Protocol.MsgPack;

namespace Gw2.Client.Net.Game;

public readonly record struct OwnDodge(int Number, int DirectionIndex, Vector3 Direction);

public readonly record struct OwnPhysicsState(byte State, byte MotionFlags);

public readonly record struct OwnMovementInput(int HeldMovementKeys, Vector3 Direction, Vector3 Facing, bool IsAirborne = false, OwnDodge? Dodge = null, OwnTargetSelection? Target = null, OwnSkillUse? SkillUse = null, int WeaponSwapNumber = 0, Vector3? HeldGroundReticleInGameUnits = null, OwnTargetSelection? ClickedSelection = null, OwnWaypointTravel? WaypointTravel = null, OwnPhysicsState? Physics = null);

public readonly record struct OwnWaypointTravel(int Number, uint PointOfInterestDataId);

public readonly record struct OwnTargetSelection(int Number, uint TargetAgentId);

public readonly record struct OwnSkillUse(int Number, byte Slot, uint SkillId, uint TargetAgentId, Vector3? GroundPointInGameUnits = null, bool ReleasesAtOnce = false)
{
    public const uint EmptySlot = 0;
}

public sealed class OwnCharacterMovement
{
    public const int MoveCommandMessageId = 0x0b;
    public const int WaypointTravelMessageId = 0xd0;
    public const int StopCommandMessageId = 0x04;
    public const int JumpCommandMessageId = 0x09;
    public const int MotionStateMessageId = 0x1a;
    public const int AgentCommandMessageId = 0x17;
    public const byte TargetSelectionLead = 0x14;
    public const ulong AgentCommandFlags = 3;
    public const byte SkillSlotPressedLead = 0x2A;
    public const byte SkillSlotReleasedLead = 0x29;
    public const byte SkillUseLead = 0x23;
    public const byte SkillTargetLead = 0x2B;
    public const byte ReticleUpdateLead = 0x2C;
    public const long HeldReticleTargetSteps = 5;
    public const long MinimumStepsBetweenReticleUpdates = 2;
    public const byte WeaponSwapLead = 0x0A;
    public const byte GroundSkillUseLead = 0x25;
    public const byte GroundSkillCommitLead = 0x28;
    public const ulong GroundSkillCommitFlags = 0;
    public const int EngagedTargetMessageId = 0xe1;
    public const int SelectedAgentMessageId = 0xeb;
    public const int ActionTargetMessageId = 0xe3;
    public const long SkillSlotHoldSteps = 4;
    public const float DodgeSpeedInGameUnits = 367f;
    public const int DodgeMilliseconds = 800;
    public static readonly long DodgeSteps = DodgeMilliseconds / (long)OwnCharacterSteps.StepLength.TotalMilliseconds;

    private const long MinimumMillisecondsBetweenCommands = 150;
    private const long SteadyMillisecondsBetweenCommands = 1000;
    private const long TurningMillisecondsBetweenCommands = 250;
    private const float DirectionChangeSquared = 0.0001f;
    private const float DirectionTurnSquared = 0.01f;
    private const float DirectionDriftSquared = 0.000025f;
    private const float PositionChangeSquared = 0.01f;
    private const float FacingChangeSquared = 0.01f;
    private const byte StartingFromRest = 0;
    private const byte AlreadyMoving = 64;
    private const byte Dodging = 67;
    private const byte DodgingFromRest = 3;
    private const byte DodgeCodeLead = 0x6C;
    private const byte DodgeCodeMiddle = 0x00;
    private const uint FirstCommandSequence = 0x80000000;
    private static readonly byte[] MoveCommandCode = [0x6B, 0x01];
    private static readonly byte[] JumpCommandCode = [0x5F, 0x10];
    private bool _wasAirborne;

    private uint _nextCommandSequence = FirstCommandSequence;
    private bool _hasSent;
    private long _lastSentMilliseconds;
    private int _lastSentKeys;
    private Vector3 _lastSentDirection;
    private Vector3 _lastSentFacing;
    private Vector3 _lastSentPosition;
    private Vector3? _previousStepPosition;
    private Vector3? _previousStepDirection;
    private Vector3? _previousStepVelocity;
    private int _lastDodgeNumber;
    private int _lastTargetSelectionNumber;
    private int _lastSkillUseNumber;
    private int _lastWeaponSwapNumber;
    private uint _lastEngagedTargetAgentId;
    private int _lastClickedSelectionNumber;
    private int _lastWaypointTravelNumber;
    private uint _lastActionTargetAgentId;
    private readonly List<(long Step, byte Slot)> _pendingSkillSlotReleases = [];
    private (byte Slot, long CastStep, long LastSentStep, Vector3 LastSentPoint)? _heldGroundSkill;
    private bool _wasAirborneLastStep;
    private long? _landingMotionStateStep;
    private Vector3? _positionTwoStepsBack;
    private long? _dodgeEndStep;
    private Vector3 _dodgeDirection;

    public IReadOnlyList<DecodedField[]> CommandsToSend(uint agentId, long step, long elapsedMilliseconds, OwnMovementInput input, Vector3 positionInCollisionUnits, float speedInCollisionUnits)
    {
        var previousPosition = _previousStepPosition ?? positionInCollisionUnits;
        var previousVelocity = _previousStepVelocity;
        var climbRate = (positionInCollisionUnits.Z - previousPosition.Z) / (float)OwnCharacterSteps.StepLength.TotalSeconds;
        var landingMotionState = LandingMotionStateFor(agentId, step, input, previousPosition);
        _positionTwoStepsBack = previousPosition;
        var commands = CommandsAfterLanding(agentId, step, elapsedMilliseconds, input, positionInCollisionUnits, speedInCollisionUnits, previousPosition, previousVelocity, climbRate);
        if (landingMotionState is not null) commands = [landingMotionState, .. commands];
        if (input.Target is { } target && target.Number != _lastTargetSelectionNumber)
        {
            _lastTargetSelectionNumber = target.Number;
            commands = [.. commands, TargetSelectionCommandOf(agentId, step, _nextCommandSequence++, target.TargetAgentId)];
        }
        if (input.ClickedSelection is { } clicked && clicked.Number != _lastClickedSelectionNumber)
        {
            _lastClickedSelectionNumber = clicked.Number;
            if (clicked.TargetAgentId != 0) commands = [.. commands, AgentIdMessageOf(SelectedAgentMessageId, clicked.TargetAgentId)];
            if (clicked.TargetAgentId != _lastActionTargetAgentId)
            {
                _lastActionTargetAgentId = clicked.TargetAgentId;
                commands = [.. commands, AgentIdMessageOf(ActionTargetMessageId, clicked.TargetAgentId)];
            }
        }
        if (input.WaypointTravel is { } travel && travel.Number != _lastWaypointTravelNumber)
        {
            _lastWaypointTravelNumber = travel.Number;
            commands = [.. commands, WaypointTravelCommandOf(travel.PointOfInterestDataId)];
        }
        foreach (var release in _pendingSkillSlotReleases.FindAll(release => release.Step <= step))
            commands = [.. commands, SkillSlotCommandOf(agentId, step, _nextCommandSequence++, SkillSlotReleasedLead, release.Slot)];
        _pendingSkillSlotReleases.RemoveAll(release => release.Step <= step);
        commands = WithHeldGroundReticle(agentId, step, input, commands);
        if (input.SkillUse is { } use && use.Number != _lastSkillUseNumber)
        {
            _lastSkillUseNumber = use.Number;
            if (use.SkillId == OwnSkillUse.EmptySlot)
            {
                _pendingSkillSlotReleases.Add((step + SkillSlotHoldSteps, use.Slot));
                return WithWeaponSwap(agentId, step, input, commands);
            }
            if (use.TargetAgentId != 0 && use.TargetAgentId != agentId && use.TargetAgentId != _lastEngagedTargetAgentId)
            {
                _lastEngagedTargetAgentId = use.TargetAgentId;
                commands = [.. commands, EngagedTargetCommandOf(use.TargetAgentId)];
            }
            commands = [.. commands, SkillSlotCommandOf(agentId, step, _nextCommandSequence++, SkillSlotPressedLead, use.Slot)];
            if (use.GroundPointInGameUnits is { } groundPoint)
            {
                commands = [.. commands, SkillTargetCommandOf(agentId, step, _nextCommandSequence, 0, groundPoint)];
                _nextCommandSequence += 2;
                commands = [.. commands, GroundSkillUseCommandOf(agentId, step, _nextCommandSequence++, groundPoint, use.SkillId, use.Slot)];
                commands = [.. commands, SkillTargetCommandOf(agentId, step, _nextCommandSequence++, 0, groundPoint)];
                commands = [.. commands, AgentCommandOf(agentId, step, _nextCommandSequence++, [GroundSkillCommitLead, 0, 0, 0, 0], GroundSkillCommitFlags)];
                if (input.HeldGroundReticleInGameUnits is { } heldReticle)
                {
                    _nextCommandSequence++;
                    commands = [.. commands, SkillTargetCommandOf(agentId, step, _nextCommandSequence++, 0, heldReticle)];
                    _heldGroundSkill = (use.Slot, step, step, heldReticle);
                }
                else _pendingSkillSlotReleases.Add((step + SkillSlotHoldSteps, use.Slot));
                return WithWeaponSwap(agentId, step, input, commands);
            }
            if (use.TargetAgentId != 0)
                commands = [.. commands, SkillTargetCommandOf(agentId, step, _nextCommandSequence++, use.TargetAgentId)];
            _nextCommandSequence++;
            commands = [.. commands, SkillUseCommandOf(agentId, step, _nextCommandSequence++, use.TargetAgentId, use.SkillId, use.Slot)];
            if (use.ReleasesAtOnce)
                commands = [.. commands, SkillSlotCommandOf(agentId, step, _nextCommandSequence++, SkillSlotReleasedLead, use.Slot)];
            else
                _pendingSkillSlotReleases.Add((step + SkillSlotHoldSteps, use.Slot));
        }
        return WithWeaponSwap(agentId, step, input, commands);
    }

    private IReadOnlyList<DecodedField[]> WithHeldGroundReticle(uint agentId, long step, OwnMovementInput input, IReadOnlyList<DecodedField[]> commands)
    {
        if (_heldGroundSkill is not { } held || step <= held.CastStep) return commands;
        if (input.HeldGroundReticleInGameUnits is not { } point)
        {
            _heldGroundSkill = null;
            return [.. commands, SkillSlotCommandOf(agentId, step, _nextCommandSequence++, SkillSlotReleasedLead, held.Slot)];
        }
        long sinceCast = step - held.CastStep;
        if (sinceCast % HeldReticleTargetSteps == 0)
            commands = [.. commands, SkillTargetCommandOf(agentId, step, _nextCommandSequence++, 0, point)];
        else if (point != held.LastSentPoint && step - held.LastSentStep >= MinimumStepsBetweenReticleUpdates)
            commands = [.. commands, ReticleUpdateCommandOf(agentId, step, _nextCommandSequence++, point)];
        else
            return commands;
        _heldGroundSkill = held with { LastSentStep = step, LastSentPoint = point };
        return commands;
    }

    public static DecodedField[] ReticleUpdateCommandOf(uint agentId, long step, uint sequence, Vector3 pointInGameUnits) =>
        AgentCommandOf(agentId, step, sequence, SkillTargetPayloadOf(ReticleUpdateLead, 0, pointInGameUnits));

    private IReadOnlyList<DecodedField[]> WithWeaponSwap(uint agentId, long step, OwnMovementInput input, IReadOnlyList<DecodedField[]> commands)
    {
        if (input.WeaponSwapNumber == _lastWeaponSwapNumber) return commands;
        _lastWeaponSwapNumber = input.WeaponSwapNumber;
        return [.. commands, AgentCommandOf(agentId, step, _nextCommandSequence++, [WeaponSwapLead])];
    }

    public static DecodedField[] GroundSkillUseCommandOf(uint agentId, long step, uint sequence, Vector3 groundPointInGameUnits, uint skillId, byte slot)
    {
        var payload = new byte[22];
        payload[0] = GroundSkillUseLead;
        WritePosition(payload.AsSpan(1), groundPointInGameUnits);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(17), skillId);
        payload[21] = slot;
        return AgentCommandOf(agentId, step, sequence, payload);
    }

    private static void WritePosition(Span<byte> destination, Vector3 position)
    {
        System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(destination, position.X);
        System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(destination[4..], position.Y);
        System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(destination[8..], position.Z);
    }

    public static DecodedField[] WaypointTravelCommandOf(uint pointOfInterestDataId) => AgentIdMessageOf(WaypointTravelMessageId, pointOfInterestDataId);

    public static DecodedField[] EngagedTargetCommandOf(uint targetAgentId) => AgentIdMessageOf(EngagedTargetMessageId, targetAgentId);

    public static DecodedField[] AgentIdMessageOf(int messageId, uint agentId) =>
    [
        Value((ulong)messageId),
        Value((ulong)agentId),
    ];

    public static DecodedField[] SkillTargetCommandOf(uint agentId, long step, uint sequence, uint targetAgentId, Vector3? groundPointInGameUnits = null) =>
        AgentCommandOf(agentId, step, sequence, SkillTargetPayloadOf(SkillTargetLead, targetAgentId, groundPointInGameUnits));

    private static byte[] SkillTargetPayloadOf(byte lead, uint targetAgentId, Vector3? groundPointInGameUnits)
    {
        var payload = new byte[37];
        payload[0] = lead;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(1), targetAgentId);
        var unset = new Vector3(float.PositiveInfinity);
        WritePosition(payload.AsSpan(5), groundPointInGameUnits ?? unset);
        WritePosition(payload.AsSpan(21), unset);
        return payload;
    }

    public static DecodedField[] TargetSelectionCommandOf(uint agentId, long step, uint sequence, uint targetAgentId)
    {
        var payload = new byte[5];
        payload[0] = TargetSelectionLead;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(1), targetAgentId);
        return
        [
            Value((ulong)AgentCommandMessageId),
            Value((ulong)agentId),
            Value((ulong)(uint)step),
            Value(AgentCommandFlags),
            Value((ulong)sequence),
            Value(payload),
        ];
    }

    public static DecodedField[] SkillSlotCommandOf(uint agentId, long step, uint sequence, byte lead, byte slot) =>
        AgentCommandOf(agentId, step, sequence, [lead, slot]);

    public static DecodedField[] SkillUseCommandOf(uint agentId, long step, uint sequence, uint targetAgentId, uint skillId, byte slot)
    {
        var payload = new byte[10];
        payload[0] = SkillUseLead;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(1), targetAgentId);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(5), skillId);
        payload[9] = slot;
        return AgentCommandOf(agentId, step, sequence, payload);
    }

    private static DecodedField[] AgentCommandOf(uint agentId, long step, uint sequence, byte[] payload, ulong flags = AgentCommandFlags) =>
    [
        Value((ulong)AgentCommandMessageId),
        Value((ulong)agentId),
        Value((ulong)(uint)step),
        Value(flags),
        Value((ulong)sequence),
        Value(payload),
    ];

    private DecodedField[]? LandingMotionStateFor(uint agentId, long step, OwnMovementInput input, Vector3 previousPosition)
    {
        if (_wasAirborneLastStep && !input.IsAirborne) _landingMotionStateStep = step + 1;
        _wasAirborneLastStep = input.IsAirborne;
        if (_landingMotionStateStep is not { } dueStep || step < dueStep) return null;
        _landingMotionStateStep = null;
        var velocity = (previousPosition - (_positionTwoStepsBack ?? previousPosition)) / (float)OwnCharacterSteps.StepLength.TotalSeconds;
        var horizontal = new Vector2(velocity.X, velocity.Y);
        var direction = input.HeldMovementKeys != 0 ? input.Direction : horizontal == Vector2.Zero ? Vector3.Zero : new Vector3(Vector2.Normalize(horizontal), 0f);
        return MotionStateOf(agentId, step, previousPosition, velocity, direction);
    }

    private IReadOnlyList<DecodedField[]> CommandsAfterLanding(uint agentId, long step, long elapsedMilliseconds, OwnMovementInput input, Vector3 positionInCollisionUnits, float speedInCollisionUnits, Vector3 previousPosition, Vector3? previousVelocity, float climbRate)
    {
        if (_dodgeEndStep is { } endStep)
        {
            _previousStepPosition = positionInCollisionUnits;
            if (step < endStep) return [];
            _dodgeEndStep = null;
            return DodgeEndCommands(agentId, step, elapsedMilliseconds, input, previousPosition, climbRate, positionInCollisionUnits);
        }
        if (input.Dodge is { } dodge && dodge.Number != _lastDodgeNumber)
        {
            _lastDodgeNumber = dodge.Number;
            _dodgeEndStep = step + DodgeSteps;
            _dodgeDirection = Vector3.Normalize(dodge.Direction with { Z = 0f });
            _previousStepPosition = positionInCollisionUnits;
            var motionDirection = _previousStepDirection;
            _previousStepDirection = null;
            _previousStepVelocity = null;
            return [DodgeCommandOf(agentId, step, _nextCommandSequence++, dodge.DirectionIndex, previousPosition, previousVelocity, motionDirection, dodge.Direction, input.Facing)];
        }
        var command = CommandToSend(agentId, step, elapsedMilliseconds, input, positionInCollisionUnits, speedInCollisionUnits);
        _previousStepVelocity = _previousStepDirection is { } walking ? OwnCharacterSteps.DirectionInCollisionUnits(walking) * speedInCollisionUnits + new Vector3(0f, 0f, climbRate) : null;
        return command is null ? [] : [command];
    }

    private IReadOnlyList<DecodedField[]> DodgeEndCommands(uint agentId, long step, long elapsedMilliseconds, OwnMovementInput input, Vector3 previousPosition, float climbRate, Vector3 positionInCollisionUnits)
    {
        var dodgeSpeed = DodgeSpeedInGameUnits / GameAgents.GameUnitsPerCollisionUnit;
        var climb = Math.Clamp(climbRate, -dodgeSpeed, dodgeSpeed);
        var dodgeVelocity = OwnCharacterSteps.DirectionInCollisionUnits(_dodgeDirection) * MathF.Sqrt(dodgeSpeed * dodgeSpeed - climb * climb) + new Vector3(0f, 0f, climb);
        var directionInEffect = input.HeldMovementKeys == 0 ? Vector3.Zero : input.Direction;
        var motionState = MotionStateOf(agentId, step, previousPosition, dodgeVelocity, directionInEffect);
        _previousStepDirection = input.HeldMovementKeys == 0 ? null : input.Direction;
        _previousStepVelocity = null;
        _hasSent = true;
        _lastSentMilliseconds = elapsedMilliseconds;
        _lastSentKeys = input.HeldMovementKeys;
        _lastSentDirection = input.Direction;
        _lastSentFacing = input.Facing;
        _lastSentPosition = positionInCollisionUnits;
        if (input.HeldMovementKeys == 0) return [motionState];
        return [motionState, MoveCommandOf(agentId, step, _nextCommandSequence++, false, previousPosition, dodgeVelocity, directionInEffect, input)];
    }

    public static DecodedField[] MotionStateOf(uint agentId, long step, Vector3 position, Vector3 velocity, Vector3 motionDirection) =>
    [
        Value((ulong)MotionStateMessageId),
        Value((ulong)agentId),
        Value((ulong)(uint)step),
        Value(0UL),
        Value(FourLanes(position)),
        Value(0UL),
        Value(FourLanes(velocity)),
        Value(FourLanes(motionDirection == Vector3.Zero ? Vector3.Zero : OwnCharacterSteps.DirectionInCollisionUnits(motionDirection))),
        Value(0UL),
        Value(0UL),
    ];

    public static DecodedField[] DodgeCommandOf(uint agentId, long step, uint sequence, int directionIndex, Vector3 position, Vector3? velocity, Vector3? motionDirection, Vector3 dodgeDirection, Vector3 facing) =>
    [
        Value((ulong)MoveCommandMessageId),
        Value((ulong)agentId),
        Value((ulong)(uint)step),
        Value((ulong)(velocity is null && motionDirection is null ? DodgingFromRest : Dodging)),
        Value((ulong)sequence),
        Value(new[] { DodgeCodeLead, DodgeCodeMiddle, (byte)directionIndex }),
        Optional(MotionOf(position, velocity, motionDirection)),
        Value(ThreeLanes(dodgeDirection)),
        Value(ThreeLanes(facing)),
        Optional([Value((ulong)DodgeSpeedInGameUnits), Value((ulong)DodgeMilliseconds), Optional(null)]),
    ];

    private DecodedField[]? CommandToSend(uint agentId, long step, long elapsedMilliseconds, OwnMovementInput input, Vector3 positionInCollisionUnits, float speedInCollisionUnits)
    {
        var previousPosition = _previousStepPosition ?? positionInCollisionUnits;
        var motionDirection = _previousStepDirection ?? input.Direction;
        var wasWalkingLastStep = _previousStepDirection is not null;
        var climb = new Vector3(0f, 0f, (positionInCollisionUnits.Z - previousPosition.Z) / (float)OwnCharacterSteps.StepLength.TotalSeconds);
        _previousStepPosition = positionInCollisionUnits;
        var takesOff = input.IsAirborne && !_wasAirborne;
        _wasAirborne = input.IsAirborne;
        if (takesOff)
        {
            Vector3? takeOffVelocity = wasWalkingLastStep && input.HeldMovementKeys != 0 ? OwnCharacterSteps.DirectionInCollisionUnits(input.Direction) * speedInCollisionUnits : null;
            Vector3? takeOffDirection = input.HeldMovementKeys != 0 ? input.Direction : null;
            _previousStepDirection = input.HeldMovementKeys == 0 ? null : input.Direction;
            return
            [
                Value((ulong)JumpCommandMessageId),
                Value((ulong)agentId),
                Value((ulong)(uint)step),
                Value((ulong)StartingFromRest),
                Value((ulong)_nextCommandSequence++),
                Value(JumpCommandCode),
                Optional(MotionOf(previousPosition, takeOffVelocity, takeOffDirection)),
            ];
        }
        _previousStepDirection = input.HeldMovementKeys == 0 ? null : input.Direction;
        if (input.HeldMovementKeys == 0)
        {
            var wasWalking = _lastSentKeys != 0;
            _lastSentKeys = 0;
            if (!wasWalking) return null;
            var stoppedWhileMoving = wasWalkingLastStep;
            Vector3? stoppingVelocity = stoppedWhileMoving ? OwnCharacterSteps.DirectionInCollisionUnits(motionDirection) * speedInCollisionUnits + climb : null;
            return StopCommandOf(agentId, step, stoppedWhileMoving || _hasSent, previousPosition, stoppingVelocity, motionDirection);
        }
        if (!IsDue(elapsedMilliseconds, input, positionInCollisionUnits)) return null;
        _hasSent = true;
        _lastSentMilliseconds = elapsedMilliseconds;
        _lastSentKeys = input.HeldMovementKeys;
        _lastSentDirection = input.Direction;
        _lastSentFacing = input.Facing;
        _lastSentPosition = positionInCollisionUnits;
        var isMoving = wasWalkingLastStep;
        Vector3? velocity = isMoving ? OwnCharacterSteps.DirectionInCollisionUnits(input.Direction) * speedInCollisionUnits + climb : null;
        return MoveCommandOf(agentId, step, _nextCommandSequence++, isMoving, previousPosition, velocity, input.Direction, input);
    }

    private bool IsDue(long elapsedMilliseconds, OwnMovementInput input, Vector3 position)
    {
        if (!_hasSent) return true;
        var sinceLastSent = elapsedMilliseconds - _lastSentMilliseconds;
        if (sinceLastSent < MinimumMillisecondsBetweenCommands) return false;
        if (input.HeldMovementKeys != _lastSentKeys) return true;
        var directionChange = Vector3.DistanceSquared(input.Direction, _lastSentDirection);
        if (directionChange > DirectionTurnSquared) return true;
        var isTurning = directionChange > DirectionDriftSquared || Vector3.DistanceSquared(input.Facing, _lastSentFacing) > FacingChangeSquared;
        var interval = isTurning ? TurningMillisecondsBetweenCommands : SteadyMillisecondsBetweenCommands;
        if (sinceLastSent <= interval) return false;
        return Vector3.DistanceSquared(input.Direction, _lastSentDirection) > DirectionChangeSquared
            || Vector3.DistanceSquared(position, _lastSentPosition) > PositionChangeSquared;
    }

    public static DecodedField[] StopCommandOf(uint agentId, long step, bool isMoving, Vector3 position, Vector3? velocity, Vector3? motionDirection) =>
    [
        Value((ulong)StopCommandMessageId),
        Value((ulong)agentId),
        Value((ulong)(uint)step),
        Value((ulong)(isMoving ? AlreadyMoving : StartingFromRest)),
        Optional(MotionOf(position, velocity, motionDirection)),
    ];

    private static DecodedField[] MotionOf(Vector3 position, Vector3? velocity, Vector3? motionDirection) =>
    [
        Value(FourLanes(position)),
        Value(0UL),
        Optional(velocity is { } moving && moving != Vector3.Zero ? [Value(FourLanes(moving))] : null),
        Optional(motionDirection is { } direction && direction != Vector3.Zero ? [Value(FourLanes(OwnCharacterSteps.DirectionInCollisionUnits(direction)))] : null),
        Value(0UL),
        Value(0UL),
    ];

    public static DecodedField[] MoveCommandOf(uint agentId, long step, uint sequence, bool isMoving, Vector3 position, Vector3? velocity, Vector3 motionDirection, OwnMovementInput input)
    {
        var motion = MotionOf(position, velocity, motionDirection);
        return
        [
            Value((ulong)MoveCommandMessageId),
            Value((ulong)agentId),
            Value((ulong)(uint)step),
            Value((ulong)(isMoving ? AlreadyMoving : StartingFromRest)),
            Value((ulong)sequence),
            Value(MoveCommandCode),
            Optional(motion),
            Value(ThreeLanes(input.Direction)),
            Value(ThreeLanes(input.Facing)),
            Optional(null),
        ];
    }

    private static DecodedField Value(object value) => new(0, true, value);

    private static DecodedField Optional(DecodedField[]? nested) => new(0, nested is not null, nested);

    private static byte[] FourLanes(Vector3 vector)
    {
        var bytes = new byte[16];
        WriteLanes(vector, bytes);
        return bytes;
    }

    private static byte[] ThreeLanes(Vector3 vector)
    {
        var bytes = new byte[12];
        WriteLanes(vector, bytes);
        return bytes;
    }

    private static void WriteLanes(Vector3 vector, byte[] bytes)
    {
        BitConverter.TryWriteBytes(bytes.AsSpan(0, 4), vector.X);
        BitConverter.TryWriteBytes(bytes.AsSpan(4, 4), vector.Y);
        BitConverter.TryWriteBytes(bytes.AsSpan(8, 4), vector.Z);
    }
}
