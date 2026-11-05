using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Gw2.Protocol.MsgPack;

namespace Gw2.Client.Net.Game;

public enum GameAttitude : byte
{
    Friendly = 0,
    Hostile = 1,
    Indifferent = 2,
    Neutral = 3,
}

public readonly record struct GameAgent(uint AgentId, uint AgentKind, Vector3 PositionInGameUnits, Vector3 Facing, float HeightInGameUnits, float RadiusInGameUnits, GameAttitude Attitude, string? CodedName, float SwimDepthInGameUnits = 0f)
{
    public Vector3? TravelDestination { get; init; }

    public float TravelSpeed { get; init; }

    public Vector3 VelocityInGameUnits { get; init; }

    private const uint PlayerKindMask = 0xF0000000;
    private const uint PlayerKind = 0x30000000;

    private const uint SpeciesKind = 0x20000000;

    public uint? PlayerId => (AgentKind & PlayerKindMask) == PlayerKind ? AgentKind & ~PlayerKindMask : null;

    public uint? SpeciesId => (AgentKind & PlayerKindMask) == SpeciesKind ? AgentKind & ~PlayerKindMask : null;
}

public enum GameAgentType
{
    Character = 0,
    Gadget = 10,
    GadgetAttackTarget = 11,
    Item = 15,
}

public readonly record struct GameKeyframedAgent(uint AgentId, GameAgentType AgentType, uint ContentInstanceId, ushort Flags, Vector3 PositionInGameUnits, Quaternion Rotation, float Scale, uint? ModelFileId, float? CollisionRadiusInGameUnits, GadgetCreate? Gadget = null);

public readonly record struct GadgetCreate(ulong StateFlags, ulong ExtraStateFlags, uint NameTextId, string CodedName)
{
    private const ulong InfoBarStateFlags = 0x220;
    private const ulong InfoBarExtraStateFlag = 0x2000;
    private const ulong TemplatedNameStateFlags = 0x600000;

    public bool HasNameplate => (StateFlags & InfoBarStateFlags) != 0 || (ExtraStateFlags & InfoBarExtraStateFlag) != 0;

    public bool NameIsTemplated => (StateFlags & TemplatedNameStateFlags) != 0;
}

public readonly record struct GameDynamicAgent(uint AgentId, GameAgentType AgentType, Vector3 PositionInGameUnits, float CollisionRadiusInGameUnits);

public sealed class GameAgents
{
    public const int DynamicCreate = 0x26;
    public const int KeyframedCreate = 0x28;
    private const int FactoryIndexField = 6;
    private const int DynamicRadiusField = 9;
    private const int DynamicPositionField = 10;
    private const int ContentInstanceField = 7;
    private const int CreateDataField = 5;
    private const int GadgetCreateLength = 111;
    private const int GadgetStateFlagsOffset = 17;
    private const int GadgetExtraStateFlagsOffset = 25;
    private const int GadgetNameTextIdOffset = 73;
    private const int GadgetCodedNameOffset = 81;
    private const int GadgetCodedNameWords = 8;
    private const int KeyframedFlagsField = 9;
    private const int KeyframedPositionField = 11;
    private const int KeyframedRotationField = 13;
    private const int KeyframedCollisionField = 16;
    private const int KeyframedModelFileField = 18;
    private const int KeyframedScaleField = 19;
    private const int CollisionRadiusIndex = 1;
    private const float DefaultScale = 1f;
    public const int CharacterCreate = 0x25;
    public const int AgentRemove = 0x29;
    public const int AgentMove = 0x2e;
    public const int AgentTransform = 0x39;
    public const int AgentStop = 0x21;
    private const int StopMotionField = 4;
    private const int MotionVelocityIndex = 2;
    public const int AgentPosition = 0x57;
    public const int AgentTravel = 0x3a;
    public const int AgentDestination = 0x40;
    public const int AgentSpeed = 0x47;
    public const int CharacterAttitude = 0x97;
    public const int CharacterName = 0x179;
    public const int PlayerCreate = 0x1ac;

    private const int AgentIdField = 1;
    private const int CreateAgentKindField = 7;
    private const int CreatePositionField = 11;
    private const int CreateYawField = 13;
    private const int CreateColliderField = 14;
    private const int CreateRadiusField = 15;
    private const int ColliderHeightIndex = 3;
    private const int ColliderSwimDepthIndex = 7;
    private const int MovePositionField = 6;
    private const int MovePitchField = 7;
    private const int MoveYawField = 8;
    private const ulong UnsetAngle = 0x7FFF;
    private const int AttitudeField = 2;
    private const int CodedNameField = 2;
    private const int PlayerIdField = 1;
    private const int PlayerNameField = 2;
    private const int TransformMotionField = 6;
    private const int TransformDirectionField = 7;
    private const int TransformFacingField = 8;
    private const int ServerStepField = 2;
    private const float StepSeconds = 0.04f;
    private const float LongestCatchUpSeconds = 1f;
    private const int PositionField = 4;
    private const int PositionVelocityField = 6;
    public const float GameUnitsPerCollisionUnit = 32f;
    private const int TravelMotionField = 6;
    private const int TravelSpeedField = 7;
    private const int TravelDestinationField = 9;
    private const int DestinationField = 4;
    private const int SpeedKindField = 4;
    private const ulong TravelSpeedKind = 0;
    private const int SpeedField = 5;
    private const float AngleScale = MathF.PI / 32766f;

    private readonly Dictionary<uint, GameAgent> _agents = [];
    private readonly Dictionary<uint, GameKeyframedAgent> _keyframed = [];
    private readonly Dictionary<uint, GameDynamicAgent> _dynamic = [];
    private readonly Dictionary<uint, string> _playerNames = [];
    private double? _serverStepNow;

    public float DefaultRunSpeedInGameUnits { get; set; }

    public IReadOnlyCollection<GameAgent> All => _agents.Values;

    public IReadOnlyCollection<GameKeyframedAgent> Keyframed => _keyframed.Values;

    public IReadOnlyCollection<GameDynamicAgent> Dynamic => _dynamic.Values;

    public bool TryGet(uint agentId, out GameAgent agent) => _agents.TryGetValue(agentId, out agent);

    public string? PlayerNameOf(GameAgent agent) => agent.PlayerId is { } player && _playerNames.TryGetValue(player, out var name) ? name : null;

    public void Advance(float elapsedSeconds)
    {
        if (_serverStepNow is { } serverStep) _serverStepNow = serverStep + elapsedSeconds / StepSeconds;
        foreach (var agent in _agents.Values.ToArray())
        {
            if (agent.TravelDestination is not { } destination || agent.TravelSpeed <= 0f)
            {
                if (agent.VelocityInGameUnits != Vector3.Zero) _agents[agent.AgentId] = agent with { PositionInGameUnits = agent.PositionInGameUnits + agent.VelocityInGameUnits * elapsedSeconds };
                continue;
            }
            var remaining = destination - agent.PositionInGameUnits;
            var distance = remaining.Length();
            var step = agent.TravelSpeed * elapsedSeconds;
            if (distance <= step)
            {
                _agents[agent.AgentId] = agent with { PositionInGameUnits = destination, TravelDestination = null };
                continue;
            }
            var direction = remaining / distance;
            _agents[agent.AgentId] = agent with { PositionInGameUnits = agent.PositionInGameUnits + direction * step, Facing = new Vector3(direction.X, direction.Y, 0f) };
        }
    }

    public void Handle(DecodedMessage message)
    {
        switch (message.MessageId)
        {
            case CharacterCreate:
                var collider = (IReadOnlyList<DecodedField[]>)message.Fields[CreateColliderField].Value!;
                _agents[AgentId(message)] = new GameAgent(
                    AgentId(message),
                    (uint)(ulong)message.Fields[CreateAgentKindField].Value!,
                    PositionOf(message.Fields[CreatePositionField]),
                    FacingOf(0, (ulong)message.Fields[CreateYawField].Value!),
                    FloatOf((ulong)collider[ColliderHeightIndex][^1].Value!),
                    FloatOf((ulong)message.Fields[CreateRadiusField].Value!),
                    GameAttitude.Neutral,
                    null,
                    collider.Count > ColliderSwimDepthIndex ? FloatOf((ulong)collider[ColliderSwimDepthIndex][^1].Value!) : 0f);
                break;
            case KeyframedCreate:
                _keyframed[AgentId(message)] = KeyframedOf(message);
                break;
            case DynamicCreate:
                _dynamic[AgentId(message)] = new GameDynamicAgent(
                    AgentId(message),
                    (GameAgentType)(ulong)message.Fields[FactoryIndexField].Value!,
                    PositionOf(message.Fields[DynamicPositionField]),
                    FloatOf((ulong)message.Fields[DynamicRadiusField].Value!));
                break;
            case AgentMove when _agents.TryGetValue(AgentId(message), out var moved):
                _agents[moved.AgentId] = moved with
                {
                    PositionInGameUnits = PositionOf(message.Fields[MovePositionField]),
                    Facing = FacingOf((ulong)message.Fields[MovePitchField].Value!, (ulong)message.Fields[MoveYawField].Value!, moved.Facing),
                };
                break;
            case CharacterAttitude when _agents.TryGetValue(AgentId(message), out var judged):
                _agents[judged.AgentId] = judged with { Attitude = (GameAttitude)(ulong)message.Fields[AttitudeField].Value! };
                break;
            case CharacterName when _agents.TryGetValue(AgentId(message), out var named):
                _agents[named.AgentId] = named with { CodedName = message.Fields[CodedNameField].Value is string { Length: > 0 } coded ? coded : null };
                break;
            case PlayerCreate when message.Fields[PlayerNameField].Value is string playerName:
                _playerNames[(uint)(ulong)message.Fields[PlayerIdField].Value!] = playerName;
                break;
            case AgentTransform when message.Fields[TransformMotionField].Value is DecodedField[] { Length: > 0 } motion && motion[0].Value is byte[] transformPosition
                && _agents.TryGetValue(AgentId(message), out var transformed):
                var walkingVelocity = WalkingVelocityOf(message, motion, transformed);
                _agents[transformed.AgentId] = transformed with
                {
                    PositionInGameUnits = GameUnitsOf(transformPosition) + walkingVelocity * CatchUpSecondsOf(message),
                    VelocityInGameUnits = walkingVelocity,
                    TravelDestination = null,
                    Facing = message.Fields[TransformFacingField].Value is byte[] facing && FacingOf(facing) is { } direction ? direction : transformed.Facing,
                };
                break;
            case AgentStop when message.Fields[StopMotionField].Value is DecodedField[] { Length: > 0 } stopMotion && stopMotion[0].Value is byte[] stopPosition
                && _agents.TryGetValue(AgentId(message), out var stopping):
                CatchUpSecondsOf(message);
                _agents[stopping.AgentId] = stopping with { PositionInGameUnits = GameUnitsOf(stopPosition), VelocityInGameUnits = Vector3.Zero, TravelDestination = null };
                break;
            case AgentPosition when message.Fields[PositionField].Value is byte[] position && _agents.TryGetValue(AgentId(message), out var positioned):
                var syncedVelocity = message.Fields.Count > PositionVelocityField && message.Fields[PositionVelocityField].Value is byte[] { Length: >= 12 } velocityLanes ? GameUnitsOf(velocityLanes) : Vector3.Zero;
                _agents[positioned.AgentId] = positioned with { PositionInGameUnits = GameUnitsOf(position) + syncedVelocity * CatchUpSecondsOf(message), VelocityInGameUnits = syncedVelocity, TravelDestination = null };
                break;
            case AgentTravel when _agents.TryGetValue(AgentId(message), out var traveller):
                var travelPosition = message.Fields[TravelMotionField].Value is DecodedField[] { Length: > 0 } travelMotion && travelMotion[0].Value is byte[] start ? GameUnitsOf(start) : traveller.PositionInGameUnits;
                var travelSpeed = message.Fields[TravelSpeedField].Value is ulong speed && speed != 0 ? speed : traveller.TravelSpeed;
                _agents[traveller.AgentId] = traveller with
                {
                    PositionInGameUnits = travelPosition,
                    TravelSpeed = travelSpeed,
                    TravelDestination = message.Fields[TravelDestinationField].Value is byte[] travelDestination ? GameUnitsWithZDownOf(travelDestination) : traveller.TravelDestination,
                };
                break;
            case AgentDestination when message.Fields[DestinationField].Value is DecodedArrayHeader destination && _agents.TryGetValue(AgentId(message), out var heading):
                _agents[heading.AgentId] = heading with { TravelDestination = GameUnitsWithZDownOf(destination.Header) };
                break;
            case AgentSpeed when message.Fields[SpeedKindField].Value is ulong speedKind && speedKind == TravelSpeedKind && message.Fields[SpeedField].Value is ulong speedBits && _agents.TryGetValue(AgentId(message), out var paced):
                _agents[paced.AgentId] = paced with { TravelSpeed = FloatOf(speedBits) };
                break;
            case AgentRemove:
                _agents.Remove(AgentId(message));
                _keyframed.Remove(AgentId(message));
                _dynamic.Remove(AgentId(message));
                break;
        }
    }

    private static GameKeyframedAgent KeyframedOf(DecodedMessage message)
    {
        var modelFileId = ModelFileIdOf(message.Fields[KeyframedModelFileField].Value as string);
        var rotation = OptionalOf(message.Fields[KeyframedRotationField]) is { Length: 4 } lanes
            ? new Quaternion(-FloatOf(lanes[0]), -FloatOf(lanes[1]), FloatOf(lanes[2]), FloatOf(lanes[3]))
            : Quaternion.Identity;
        var sentScale = OptionalOf(message.Fields[KeyframedScaleField]) is { Length: > 0 } scaleLanes ? FloatOf(scaleLanes[0]) : DefaultScale;
        var collision = OptionalOf(message.Fields[KeyframedCollisionField]);
        return new GameKeyframedAgent(
            AgentId(message),
            (GameAgentType)(ulong)message.Fields[FactoryIndexField].Value!,
            (uint)(ulong)message.Fields[ContentInstanceField].Value!,
            (ushort)(ulong)message.Fields[KeyframedFlagsField].Value!,
            GameUnitsOf((byte[])message.Fields[KeyframedPositionField].Value!),
            rotation,
            sentScale == 0f && modelFileId is null ? DefaultScale : sentScale,
            modelFileId,
            collision is { Length: > CollisionRadiusIndex } ? FloatOf(collision[CollisionRadiusIndex]) : null,
            (GameAgentType)(ulong)message.Fields[FactoryIndexField].Value! == GameAgentType.Gadget ? GadgetCreateOf(message.Fields[CreateDataField].Value as byte[]) : null);
    }

    private static GadgetCreate? GadgetCreateOf(byte[]? createData)
    {
        if (createData is not { Length: >= GadgetCreateLength }) return null;
        var codedName = new System.Text.StringBuilder(GadgetCodedNameWords);
        for (var word = 0; word < GadgetCodedNameWords; word++)
        {
            var character = (char)BitConverter.ToUInt16(createData, GadgetCodedNameOffset + word * sizeof(ushort));
            if (character == '\0') break;
            codedName.Append(character);
        }
        return new GadgetCreate(
            BitConverter.ToUInt64(createData, GadgetStateFlagsOffset),
            BitConverter.ToUInt64(createData, GadgetExtraStateFlagsOffset),
            BitConverter.ToUInt32(createData, GadgetNameTextIdOffset),
            codedName.ToString());
    }

    private static DecodedField[]? OptionalOf(DecodedField field) => field.Present ? field.Value as DecodedField[] : null;

    private static float FloatOf(DecodedField field) => FloatOf(Convert.ToUInt64(field.Value));

    private static uint? ModelFileIdOf(string? reference) =>
        reference is { Length: 2 } && new Gw2.Formats.PackFileReference(reference[0], reference[1], 0).TryGetFileId(out var fileId) ? fileId : null;

    private static uint AgentId(DecodedMessage message) => (uint)(ulong)message.Fields[AgentIdField].Value!;

    private static Vector3 PositionOf(DecodedField field)
    {
        var header = ((DecodedArrayHeader)field.Value!).Header;
        return new Vector3(BitConverter.ToSingle(header, 0), BitConverter.ToSingle(header, 4), BitConverter.ToSingle(header, 8));
    }

    private float CatchUpSecondsOf(DecodedMessage message)
    {
        if (message.Fields[ServerStepField].Value is not ulong sentStep) return 0f;
        _serverStepNow = Math.Max(_serverStepNow ?? sentStep, sentStep);
        return Math.Clamp((float)(_serverStepNow.Value - sentStep) * StepSeconds, 0f, LongestCatchUpSeconds);
    }

    private Vector3 WalkingVelocityOf(DecodedMessage message, DecodedField[] motion, GameAgent agent)
    {
        var sentVelocity = VelocityOf(motion);
        if (message.Fields[TransformDirectionField].Value is not byte[] directionLanes) return sentVelocity;
        var direction = new Vector3(BitConverter.ToSingle(directionLanes, 0), BitConverter.ToSingle(directionLanes, 4), 0f);
        if (direction == Vector3.Zero) return Vector3.Zero;
        var sentGroundSpeed = new Vector2(sentVelocity.X, sentVelocity.Y).Length();
        var previousGroundSpeed = new Vector2(agent.VelocityInGameUnits.X, agent.VelocityInGameUnits.Y).Length();
        var runSpeed = agent.TravelSpeed > 0f ? agent.TravelSpeed : DefaultRunSpeedInGameUnits;
        var speed = sentGroundSpeed > 0f ? sentGroundSpeed : previousGroundSpeed > 0f ? previousGroundSpeed : runSpeed;
        return Vector3.Normalize(direction) * speed + new Vector3(0f, 0f, sentVelocity.Z);
    }

    private static Vector3 VelocityOf(DecodedField[] motion) =>
        motion.Length > MotionVelocityIndex && motion[MotionVelocityIndex].Value is DecodedField[] { Length: > 0 } velocity && velocity[0].Value is byte[] lanes ? GameUnitsOf(lanes) : Vector3.Zero;

    private static Vector3 GameUnitsOf(byte[] collisionPosition) => new Vector3(
        BitConverter.ToSingle(collisionPosition, 0),
        BitConverter.ToSingle(collisionPosition, 4),
        -BitConverter.ToSingle(collisionPosition, 8)) * GameUnitsPerCollisionUnit;

    private static Vector3 GameUnitsWithZDownOf(byte[] position) =>
        new(BitConverter.ToSingle(position, 0), BitConverter.ToSingle(position, 4), BitConverter.ToSingle(position, 8));

    private static Vector3? FacingOf(byte[] direction)
    {
        var facing = new Vector3(BitConverter.ToSingle(direction, 0), BitConverter.ToSingle(direction, 4), -BitConverter.ToSingle(direction, 8));
        return facing.LengthSquared() > 0f ? Vector3.Normalize(facing) : null;
    }

    private static float FloatOf(ulong bits) => BitConverter.UInt32BitsToSingle((uint)bits);

    private static Vector3 FacingOf(ulong pitch, ulong yaw, Vector3 unchanged = default)
    {
        if (yaw == UnsetAngle) return unchanged;
        var pitchRadians = pitch == UnsetAngle ? 0f : (short)pitch * AngleScale;
        var yawRadians = (short)yaw * AngleScale;
        return new Vector3(MathF.Cos(pitchRadians) * MathF.Cos(yawRadians), MathF.Cos(pitchRadians) * MathF.Sin(yawRadians), MathF.Sin(pitchRadians));
    }
}
