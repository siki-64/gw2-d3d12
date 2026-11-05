using System;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Gw2.Client.Scene.Havok;

public sealed class Gw2CharacterStep
{
    public const float StepSeconds = 0.04f;
    public const float StepsPerSecond = 25f;
    private const ulong SteppedOnceFlag = 0x20000000000000;
    private const ulong ClearedEachStepFlags = 0xFFFFFF7FF9FFFFFF;
    private const ulong EventFlag = 0x2000000000;
    private const ulong JumpFlag = 0x2000000;
    private const ulong UnsupportedFlag = 0x80000000000;
    private const ulong StandsOnGroundHeightFlag = 0x400000;
    private const ulong CappedWhenTurningFlags = 0x100008000;
    public const ulong SpeedCappedByFacingFlag = 0x40000;
    public const float BackpedalSpeedCap = 3.28125f;
    public const float StrafeSpeedCap = 5.625f;
    private static readonly Vector128<float> FacingFlatMask = Vector128.Create(-1, -1, 0, -1).AsSingle();
    private const float BackwardTurnDot = -0.38268343f;
    private const float SideTurnDot = 0.5f;
    private const float CollisionTolerance = 0.1f;
    private const long PhantomCollidable = 1;

    private static readonly Vector128<float> Down = Vector128.Create(0f, 0f, -1f, 0f);
    private static readonly Vector128<float> Up = Vector128.Create(0f, 0f, 1f, 0f);
    private static readonly Vector128<float> SweepPadding = Vector128.Create(CollisionTolerance, CollisionTolerance, CollisionTolerance, 0f);

    private readonly Gw2HavokMapWorld _world;
    private readonly HavokCapsulePhantomWorld _phantom;
    private readonly Vector128<float>[] _capsule;
    private readonly float _radius;

    public Gw2CharacterStep(Gw2HavokMapWorld world, float radius, float height, Vector128<float> position)
    {
        _world = world;
        _radius = radius;
        Height = height;
        _capsule = [Vector128.Create(0f, 0f, radius, 0f), Vector128.Create(0f, 0f, height - radius, 0f)];
        _phantom = new HavokCapsulePhantomWorld(_capsule, radius, PhantomCollidable)
        {
            IterationEarlyOutDistance = 0.01f,
            MaxIterations = 20,
            CollisionTolerance = CollisionTolerance,
        };
        var settings = new HavokProxySettings
        {
            DynamicFriction = 1f,
            StaticFriction = 0f,
            KeepDistance = 0.05f,
            KeepContactTolerance = 0.1f,
            ContactAngleSensitivity = 10f,
            MaxCharacterSpeedForSolver = 10f,
            PenetrationRecoverySpeed = 10f,
            MaxCastIterations = 10,
            Up = Up,
        };
        var transform = HavokTransform.Identity;
        transform.Translation = position;
        Proxy = new HavokCharacterProxy(new HavokCharacterProxyConfig { Settings = settings, MaxSlopeCosine = MathF.Cos(50f * MathF.PI / 180f), UserPlanes = 4 }, transform);
        Context = new Gw2CharacterContext(Body);
    }

    public float Radius => _radius;
    public float Height { get; }
    public HavokCharacterProxy Proxy { get; }
    public Gw2CharacterBody Body { get; } = new();
    public Gw2CharacterContext Context { get; }
    public ulong Flags { get; set; }
    public float GravityZ { get; set; } = -40.625f;
    public float GroundHeight { get; set; } = float.MinValue;
    public float WaterSurfaceOffset { get; set; }
    public Gw2.Dat.Content.Gw2CollisionWater? Water { get; set; }
    public Vector128<float> AirSurfaceVelocity { get; set; }
    public Gw2CharacterOutput LastOutput { get; private set; }
    public HavokSurfaceInfo LastSurface { get; private set; }

    public Vector128<float> Position => Proxy.Position;
    public Vector128<float> Velocity => Proxy.Velocity;

    public static Vector128<float> FlatFacingOf(System.Numerics.Vector3 gameFacing)
    {
        var facing = Sse.And(Vector128.Create(gameFacing.X, gameFacing.Y, -gameFacing.Z, 0f), FacingFlatMask);
        var squared = Sse.Multiply(facing, facing);
        var lengthSquared = Sse.Add(Sse.Add(Sse.Shuffle(squared, squared, 0x55), Sse.Shuffle(squared, squared, 0x00)), Sse.Shuffle(squared, squared, 0xAA));
        var estimate = Sse.ReciprocalSqrt(lengthSquared);
        var inverse = Sse.AndNot(Sse.CompareLessThanOrEqual(lengthSquared, Vector128<float>.Zero),
            Sse.Multiply(Sse.Subtract(Vector128.Create(3f), Sse.Multiply(Sse.Multiply(estimate, lengthSquared), estimate)), Sse.Multiply(estimate, Vector128.Create(0.5f))));
        return Sse.Multiply(inverse, facing);
    }

    public static int SpeedCapModeOf(Vector128<float> flatFacing, Vector128<float> moveDirection)
    {
        var turn = Dot3(flatFacing, moveDirection);
        if (turn <= BackwardTurnDot) return 1;
        return turn < SideTurnDot ? 2 : 0;
    }

    public static float CappedSpeedOf(Vector128<float> flatFacing, Vector128<float> moveDirection, float speed) => SpeedCapModeOf(flatFacing, moveDirection) switch
    {
        1 => MathF.Min(speed, BackpedalSpeedCap),
        2 => MathF.Min(speed, StrafeSpeedCap),
        _ => speed,
    };

    public const ulong MovesToDestinationFlag = 0x8000;
    private const ulong MoveToKeepsSteeringFlag = 0x800;
    private const ulong MoveToResponsiveFlag = 0x1000;
    private const ulong MoveToCappedFlag = 0x40000;
    private const ulong MoveToHeldInputFlag = 0x400000000000000;
    private const ulong ClearedByMoveToStop = 0xFBFFFFFFFFFD77FF;
    private const ulong ArrivingFlag = 0x20000;
    private const ulong ServerJumpFlags = 0x6000000;
    private const ulong ServerJumpKeepsLargeVelocityFlag = 0x800000000000000;
    private const float ServerJumpSmallVelocityCap = 32f;
    private const float MoveToTargetTolerance = 0.02f;
    private const float ShortestMoveToDirection = 0.0099999998f;
    private const int ArrivedEvent = 2;
    private const int ArrivesThisStepEvent = 1;
    private static readonly Vector128<float> NoMoveToDirection = Vector128.Create(float.NaN);

    public Vector128<float> MoveToDestination { get; private set; }

    public bool MovesToDestination => (Flags & MovesToDestinationFlag) != 0;

    public void MoveTo(Vector128<float> destination, bool keepsSteering)
    {
        Flags &= ~MoveToHeldInputFlag;
        Flags &= ~MoveToResponsiveFlag;
        Flags = keepsSteering ? Flags | MoveToKeepsSteeringFlag : Flags & ~MoveToKeepsSteeringFlag;
        Flags &= ~MoveToCappedFlag;
        Flags |= MovesToDestinationFlag;
        MoveToDestination = destination;
    }

    public void StopMoveTo() => Flags &= ClearedByMoveToStop;

    public void Jump(float wireVerticalVelocity, bool keepsLargeVelocity)
    {
        var vertical = -wireVerticalVelocity * Gw2HavokMapWorld.CollisionUnitsPerWorldUnit;
        if (!keepsLargeVelocity && vertical > ServerJumpSmallVelocityCap) vertical = ServerJumpSmallVelocityCap;
        Body.LaunchVelocity = Vector128.Create(0f, 0f, vertical, 0f);
        Flags = keepsLargeVelocity ? Flags | ServerJumpKeepsLargeVelocityFlag | ServerJumpFlags : (Flags & ~ServerJumpKeepsLargeVelocityFlag) | ServerJumpFlags;
    }

    private Vector128<float> MoveToDirectionFrom(Vector128<float> position)
    {
        var toDestination = Sse.Subtract(MoveToDestination, position);
        var squared = Sse.Multiply(toDestination, toDestination);
        var lengthSquared = Sse.Add(Sse.Add(Sse.Shuffle(squared, squared, 0x55), Sse.Shuffle(squared, squared, 0x00)), Sse.Shuffle(squared, squared, 0xAA));
        var estimate = Sse.ReciprocalSqrt(lengthSquared);
        var inverse = Sse.AndNot(Sse.CompareLessThanOrEqual(lengthSquared, Vector128<float>.Zero),
            Sse.Multiply(Sse.Subtract(Vector128.Create(3f), Sse.Multiply(Sse.Multiply(estimate, lengthSquared), estimate)), Sse.Multiply(estimate, Vector128.Create(0.5f))));
        if (inverse.ToScalar() * lengthSquared.ToScalar() < ShortestMoveToDirection) return NoMoveToDirection;
        return Sse.Multiply(toDestination, inverse);
    }

    public void Step(float time, Vector128<float> moveDirection, Vector128<float> lastForward, float moveSpeed, float speedLimit, bool holdsMovement, bool jumps = false)
    {
        PrepareIntegrate(time, moveDirection, lastForward, moveSpeed, speedLimit, holdsMovement, jumps);
        Integrate();
    }

    public HavokCapsulePhantomWorld Phantom => _phantom;

    public void PrepareIntegrate(float time, Vector128<float> moveDirection, Vector128<float> lastForward, float moveSpeed, float speedLimit, bool holdsMovement, bool jumps = false)
    {
        if (jumps) Flags |= JumpFlag;
        var movesToDestination = MovesToDestination;
        if (movesToDestination)
        {
            moveDirection = MoveToDirectionFrom(Proxy.Position);
            holdsMovement = true;
            speedLimit = 0f;
        }
        var input = new Gw2CharacterInput
        {
            InputLeftRight = 0f,
            InputUpDown = holdsMovement ? 1f : 0f,
            Up = Up,
            Forward = moveDirection,
            SurfaceState = 2,
            StartTime = time - StepSeconds,
            EndTime = time,
            DeltaTime = StepSeconds,
            InverseDeltaTime = StepsPerSecond,
            Position = Proxy.Position,
            Velocity = Proxy.Velocity,
            Gravity = Vector128.Create(0f, 0f, GravityZ, 0f),
            LastForward = lastForward,
            Flags = Flags,
            SpeedLimit = speedLimit,
            WaterSurfaceHeight = WaterSurfaceOffset + WaterLevelAt(Proxy.Position),
            MoveSpeed = moveSpeed,
            AirSurfaceVelocity = AirSurfaceVelocity,
            TargetPosition = movesToDestination ? MoveToDestination : Vector128<float>.Zero,
            TargetTolerance = MoveToTargetTolerance,
        };
        if (holdsMovement && (Flags & CappedWhenTurningFlags) == 0)
        {
            input.SpeedCapMode = SpeedCapModeOf(lastForward, moveDirection);
        }

        Flags &= ClearedEachStepFlags;
        Proxy.RefreshManifoldInCheckSupport = (Flags & SteppedOnceFlag) == 0;
        CollectOverlapping(Proxy.Position, Proxy.Position);
        var surface = Proxy.CheckSupport(Down, _phantom);
        LastSurface = surface;
        input.SurfaceState = (int)surface.SupportedState;
        input.SurfaceNormal = surface.SurfaceNormal;
        input.SurfaceVelocity = surface.SurfaceVelocity;
        input.SurfaceIsDynamic = surface.SurfaceIsDynamic ? 1 : 0;
        if ((Flags & StandsOnGroundHeightFlag) != 0 && GroundHeight + 0f > input.Position.GetElement(2))
        {
            input.SurfaceState = 2;
            input.SurfaceNormal = Up;
        }

        var output = new Gw2CharacterOutput { Events = (Flags & EventFlag) != 0 ? 8 : 0 };
        Context.Update(input, ref output);
        Proxy.Velocity = output.Velocity;
        var reach = Sse.Multiply(output.Velocity, Vector128.Create(StepSeconds));
        CollectOverlapping(Proxy.Position, Sse.Add(Proxy.Position, reach));
        LastOutput = output;
        _supportedThisStep = input.SurfaceState == 2;
    }

    public void Integrate()
    {
        Proxy.Integrate(StepSeconds, _phantom);
        Flags = (LastOutput.Events & 8) != 0 ? Flags | EventFlag : Flags & ~EventFlag;
        Flags = _supportedThisStep ? Flags & ~UnsupportedFlag : Flags | UnsupportedFlag;
        Flags |= SteppedOnceFlag;
        if ((LastOutput.Events & ArrivedEvent) != 0) Flags &= ClearedByMoveToStop & ~0x8000000UL;
        else if ((LastOutput.Events & ArrivesThisStepEvent) != 0) Flags |= ArrivingFlag;
    }

    private bool _supportedThisStep;

    public static Vector128<float> CollisionOf(System.Numerics.Vector3 game) =>
        Vector128.Create(game.X * Gw2HavokMapWorld.CollisionUnitsPerWorldUnit, game.Y * Gw2HavokMapWorld.CollisionUnitsPerWorldUnit, -game.Z * Gw2HavokMapWorld.CollisionUnitsPerWorldUnit, 0f);

    public float WaterLevelAt(Vector128<float> position)
    {
        var surface = Water?.SurfaceZAt(GameOf(position)) ?? Gw2.Dat.Content.Gw2CollisionWater.NoSurface;
        return -(surface * (1f / 32f));
    }

    private static readonly byte[] StateBytesOfContextStates = [0, 2, 1, 6, 6, 3, 4, 5];
    private const byte StateByteOfUnknownState = 6;
    private const ulong SurfaceSwimFlag = 0x800000;

    public byte StateByte => (uint)Context.Current < StateBytesOfContextStates.Length ? StateBytesOfContextStates[(int)Context.Current] : StateByteOfUnknownState;

    public byte MotionFlags => (byte)((Context.StepsUnsupported != 0 ? 2 : 0) | (Context.StepsSliding != 0 ? 1 : 0) | ((Flags & SurfaceSwimFlag) != 0 ? 4 : 0));

    public static float WaterSurfaceOffsetOf(float colliderSwimDepth) => -(colliderSwimDepth * (1f / 32f));

    public static System.Numerics.Vector3 GameOf(Vector128<float> collision) =>
        new(collision.GetElement(0) * Gw2HavokMapWorld.WorldUnitsPerCollisionUnit, collision.GetElement(1) * Gw2HavokMapWorld.WorldUnitsPerCollisionUnit, -collision.GetElement(2) * Gw2HavokMapWorld.WorldUnitsPerCollisionUnit);

    private void CollectOverlapping(Vector128<float> from, Vector128<float> to)
    {
        var capsuleMin = Sse.Min(_capsule[0], _capsule[1]);
        var capsuleMax = Sse.Max(_capsule[0], _capsule[1]);
        var radius = Vector128.Create(_radius, _radius, _radius, 0f);
        var min = Sse.Subtract(Sse.Add(Sse.Min(from, to), Sse.Subtract(capsuleMin, radius)), SweepPadding);
        var max = Sse.Add(Sse.Add(Sse.Max(from, to), Sse.Add(capsuleMax, radius)), SweepPadding);
        _world.CollectOverlapping(min, max, _phantom.Overlapping);
    }

    private static float Dot3(Vector128<float> first, Vector128<float> second)
    {
        var product = Sse.Multiply(first, second);
        return (product.GetElement(1) + product.GetElement(0)) + product.GetElement(2);
    }
}
