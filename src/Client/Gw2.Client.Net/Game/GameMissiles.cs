using System;
using System.Collections.Generic;
using System.Numerics;
using Gw2.Protocol.MsgPack;

namespace Gw2.Client.Net.Game;

public enum MissileMotionKind : uint
{
    None = 0,
    Arc = 1,
    Reaimed = 2,
    Straight = 4,
    ReaimedAlternate = 8,
    Orbit = 0x10,
    ReaimedThird = 0x20,
    OrbitAlternate = 0x40,
}

public sealed record GameMissile(
    uint MissileId,
    uint SourceAgentId,
    uint SkillId,
    uint WeaponSkinId,
    Vector3 LaunchPointInGameUnits,
    MissileMotionKind MotionKind,
    uint? TargetAgentId,
    Vector3 TargetPointInGameUnits,
    float Speed,
    uint MotionFlags,
    Vector3 VelocityInGameUnits,
    float ElapsedSeconds,
    float FlightSeconds,
    float SecondsSinceAim)
{
    public const float ArcGravityTerm = 192.9f;

    public bool IsLaunched => MotionKind != MissileMotionKind.None;

    public bool HasArrived => IsLaunched && TargetAgentId is null && ElapsedSeconds >= FlightSeconds;

    public Vector3 PositionInGameUnits
    {
        get
        {
            float t = MathF.Min(ElapsedSeconds, FlightSeconds);
            var position = LaunchPointInGameUnits + VelocityInGameUnits * t;
            return MotionKind == MissileMotionKind.Arc ? position with { Z = position.Z + ArcGravityTerm * t * t } : position;
        }
    }
}

public sealed class GameMissiles
{
    public const int MissileCreate = 0x406;
    public const int MissileRemove = 0x407;
    public const int MissileMotion = 0x409;

    public const float UpdatePeriodSeconds = 0.12f;
    public const float LongestFlightSeconds = 600f;
    private const uint HorizontalOnlyFlag = 0x40;
    private const uint OrbitKinds = (uint)(MissileMotionKind.Orbit | MissileMotionKind.OrbitAlternate);

    private const int MissileIdField = 1;
    private const int CreateSourceField = 2;
    private const int CreateSkillField = 3;
    private const int CreateWeaponSkinField = 4;
    private const int CreateOriginField = 5;
    private const int MotionKindField = 2;
    private const int MotionTargetAgentField = 4;
    private const int MotionTargetPointField = 5;
    private const int MotionSpeedField = 6;
    private const int MotionFlagsField = 7;

    private readonly Dictionary<uint, GameMissile> _missiles = [];

    public IReadOnlyCollection<GameMissile> All => _missiles.Values;

    public bool TryGet(uint missileId, out GameMissile missile) => _missiles.TryGetValue(missileId, out missile!);

    public void Handle(DecodedMessage message, Func<uint, Vector3?> agentPosition)
    {
        switch (message.MessageId)
        {
            case MissileCreate:
                uint createdId = U32(message, MissileIdField);
                var origin = PointOf(message.Fields[CreateOriginField]);
                _missiles[createdId] = new GameMissile(createdId, U32(message, CreateSourceField), U32(message, CreateSkillField),
                    U32(message, CreateWeaponSkinField), origin, MissileMotionKind.None, null, origin, 0f, 0, Vector3.Zero, 0f, 0f, 0f);
                break;
            case MissileMotion when _missiles.TryGetValue(U32(message, MissileIdField), out var missile):
                var kind = (MissileMotionKind)U32(message, MotionKindField);
                if (missile.IsLaunched && ((uint)kind & OrbitKinds) != 0) kind = MissileMotionKind.Straight;
                uint targetAgent = U32(message, MotionTargetAgentField);
                uint? homingTarget = targetAgent != 0 ? targetAgent : null;
                var targetPoint = homingTarget is { } target && agentPosition(target) is { } targetPosition ? targetPosition : PointOf(message.Fields[MotionTargetPointField]);
                var launched = missile with
                {
                    LaunchPointInGameUnits = missile.PositionInGameUnits,
                    MotionKind = kind,
                    TargetAgentId = homingTarget,
                    TargetPointInGameUnits = targetPoint,
                    Speed = BitConverter.UInt32BitsToSingle(U32(message, MotionSpeedField)),
                    MotionFlags = U32(message, MotionFlagsField),
                    ElapsedSeconds = 0f,
                    SecondsSinceAim = 0f,
                };
                _missiles[launched.MissileId] = Aimed(launched);
                break;
            case MissileRemove:
                _missiles.Remove(U32(message, MissileIdField));
                break;
        }
    }

    public void Advance(float elapsedSeconds, Func<uint, Vector3?> agentPosition)
    {
        foreach (var missile in new List<GameMissile>(_missiles.Values))
        {
            if (!missile.IsLaunched) continue;
            var advanced = missile with { ElapsedSeconds = missile.ElapsedSeconds + elapsedSeconds, SecondsSinceAim = missile.SecondsSinceAim + elapsedSeconds };
            if (advanced.TargetAgentId is { } target && advanced.SecondsSinceAim >= UpdatePeriodSeconds && agentPosition(target) is { } targetPosition)
                advanced = Aimed(advanced with { LaunchPointInGameUnits = advanced.PositionInGameUnits, TargetPointInGameUnits = targetPosition, ElapsedSeconds = 0f, SecondsSinceAim = 0f });
            _missiles[missile.MissileId] = advanced;
        }
    }

    private static GameMissile Aimed(GameMissile missile) =>
        missile.MotionKind switch
        {
            MissileMotionKind.Arc => ArcOf(missile),
            MissileMotionKind.Reaimed or MissileMotionKind.Straight or MissileMotionKind.ReaimedAlternate or MissileMotionKind.ReaimedThird => StraightOf(missile),
            _ => missile with { VelocityInGameUnits = Vector3.Zero, FlightSeconds = LongestFlightSeconds },
        };

    private static GameMissile StraightOf(GameMissile missile)
    {
        var delta = missile.TargetPointInGameUnits - missile.LaunchPointInGameUnits;
        if ((missile.MotionFlags & HorizontalOnlyFlag) != 0) delta = delta with { Z = 0f };
        float distance = delta.Length();
        var direction = distance * distance >= 1e-12f ? delta / distance : Vector3.UnitX;
        return missile with
        {
            VelocityInGameUnits = direction * missile.Speed,
            FlightSeconds = Math.Clamp(distance / missile.Speed, 0f, LongestFlightSeconds),
        };
    }

    private static GameMissile ArcOf(GameMissile missile)
    {
        var delta = missile.TargetPointInGameUnits - missile.LaunchPointInGameUnits;
        float horizontal = MathF.Sqrt(delta.X * delta.X + delta.Y * delta.Y);
        float flight = horizontal / missile.Speed;
        if (flight <= 1e-6f)
            return missile with { VelocityInGameUnits = new Vector3(missile.Speed, 0f, 0f), FlightSeconds = 0f };
        var velocity = new Vector3(delta.X / horizontal * missile.Speed, delta.Y / horizontal * missile.Speed, delta.Z / flight - flight * GameMissile.ArcGravityTerm);
        return missile with { VelocityInGameUnits = velocity, FlightSeconds = Math.Clamp(flight, 0f, LongestFlightSeconds) };
    }

    private static Vector3 PointOf(DecodedField field)
    {
        var header = ((DecodedArrayHeader)field.Value!).Header;
        return new Vector3(BitConverter.ToSingle(header, 0), BitConverter.ToSingle(header, 4), BitConverter.ToSingle(header, 8));
    }

    private static uint U32(DecodedMessage message, int field) => (uint)(ulong)message.Fields[field].Value!;
}
