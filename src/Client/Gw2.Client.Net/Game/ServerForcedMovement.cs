using System;
using System.Numerics;
using Gw2.Protocol.MsgPack;

namespace Gw2.Client.Net.Game;

public readonly record struct ServerMoveTo(Vector3 DestinationInWireUnits, float SpeedInGameUnits, int DurationMilliseconds, bool KeepsSteeringFlag);

public readonly record struct ServerJump(float WireVerticalVelocity, bool KeepsLargeVelocity);

public readonly record struct ServerTeleport(Vector3 PositionInGameUnits);

public readonly record struct ServerDash(Vector3 CollisionDirection, Vector2 FlatFacing, float SpeedInGameUnits, int DurationMilliseconds);

public static class ServerForcedMovement
{
    public const int DashMessageId = 0x39;
    private const int DashFlagsField = 3;
    private const int DashDirectionField = 7;
    private const int DashFacingField = 8;
    private const int DashTravelField = 9;
    private const int DashTravelSpeedIndex = 0;
    private const int DashTravelDurationIndex = 1;
    public const int TeleportMessageId = 0x42;
    private const int TeleportFlagsField = 3;
    private const int TeleportPositionField = 6;
    private const ulong AppliesToControlledCharacterFlag = 0x10;
    public const int MoveToMessageId = 0x43;
    public const int JumpMessageId = 0x35;

    private const int AgentIdField = 1;
    private const int MoveToDestinationField = 7;
    private const int MoveToSpeedField = 10;
    private const int MoveToDurationField = 11;
    private const int MoveToSteeringFlagField = 12;
    private const int JumpVelocityField = 7;
    private const int JumpKeepsLargeVelocityField = 8;

    public static uint? AgentOf(DecodedMessage message) =>
        message.MessageId is MoveToMessageId or JumpMessageId or TeleportMessageId or DashMessageId && message.Fields[AgentIdField].Value is ulong agent ? (uint)agent : null;

    public static ServerMoveTo? MoveToOf(DecodedMessage message)
    {
        if (message.MessageId != MoveToMessageId || message.Fields[MoveToDestinationField].Value is not DecodedArrayHeader destination) return null;
        var header = destination.Header;
        return new ServerMoveTo(
            new Vector3(BitConverter.ToSingle(header, 0), BitConverter.ToSingle(header, 4), BitConverter.ToSingle(header, 8)),
            BitConverter.UInt32BitsToSingle((uint)UnsignedOf(message, MoveToSpeedField)),
            (int)UnsignedOf(message, MoveToDurationField),
            UnsignedOf(message, MoveToSteeringFlagField) != 0);
    }

    public static ServerJump? JumpOf(DecodedMessage message)
    {
        if (message.MessageId != JumpMessageId) return null;
        return new ServerJump(BitConverter.UInt32BitsToSingle((uint)UnsignedOf(message, JumpVelocityField)), UnsignedOf(message, JumpKeepsLargeVelocityField) != 0);
    }

    public static ServerTeleport? TeleportOf(DecodedMessage message)
    {
        if (message.MessageId != TeleportMessageId || (UnsignedOf(message, TeleportFlagsField) & AppliesToControlledCharacterFlag) == 0) return null;
        if (message.Fields[TeleportPositionField].Value is not DecodedArrayHeader position) return null;
        var header = position.Header;
        return new ServerTeleport(new Vector3(BitConverter.ToSingle(header, 0), BitConverter.ToSingle(header, 4), BitConverter.ToSingle(header, 8)));
    }

    public static ServerDash? DashOf(DecodedMessage message)
    {
        if (message.MessageId != DashMessageId || message.Fields.Count <= DashTravelField || (UnsignedOf(message, DashFlagsField) & AppliesToControlledCharacterFlag) == 0) return null;
        if (message.Fields[DashTravelField].Value is not DecodedField[] travel || travel.Length <= DashTravelDurationIndex) return null;
        if (travel[DashTravelSpeedIndex].Value is not ulong speed || travel[DashTravelDurationIndex].Value is not ulong duration || duration == 0) return null;
        if (message.Fields[DashDirectionField].Value is not byte[] { Length: >= 12 } direction) return null;
        var facing = message.Fields[DashFacingField].Value is byte[] { Length: >= 8 } facingLanes
            ? new Vector2(BitConverter.ToSingle(facingLanes, 0), BitConverter.ToSingle(facingLanes, 4))
            : Vector2.Zero;
        return new ServerDash(
            new Vector3(BitConverter.ToSingle(direction, 0), BitConverter.ToSingle(direction, 4), -BitConverter.ToSingle(direction, 8)),
            facing,
            (int)(long)speed,
            (int)duration);
    }

    private static ulong UnsignedOf(DecodedMessage message, int field) =>
        message.Fields.Count > field && message.Fields[field].Value is ulong value ? value : 0;
}
