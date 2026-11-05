using System;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using static Gw2.Client.Scene.Havok.HavokVector;

namespace Gw2.Client.Scene.Havok;

public enum Gw2CharacterStateType
{
    OnGround = 0,
    Jumping = 1,
    InAir = 2,
    Diving = 5,
    Swimming = 6,
    Mounted = 7,
}

[StructLayout(LayoutKind.Explicit, Size = 320)]
public struct Gw2CharacterInput
{
    [FieldOffset(0)] public float InputLeftRight;
    [FieldOffset(4)] public float InputUpDown;
    [FieldOffset(8)] public byte WantJump;
    [FieldOffset(16)] public Vector128<float> Up;
    [FieldOffset(32)] public Vector128<float> Forward;
    [FieldOffset(48)] public byte AtLadder;
    [FieldOffset(64)] public int SurfaceState;
    [FieldOffset(80)] public Vector128<float> SurfaceNormal;
    [FieldOffset(96)] public Vector128<float> SurfaceVelocity;
    [FieldOffset(112)] public int SurfaceIsDynamic;
    [FieldOffset(116)] public byte SurfaceIsCharacter;
    [FieldOffset(128)] public float StartTime;
    [FieldOffset(132)] public float EndTime;
    [FieldOffset(136)] public float DeltaTime;
    [FieldOffset(140)] public float InverseDeltaTime;
    [FieldOffset(144)] public Vector128<float> Position;
    [FieldOffset(160)] public Vector128<float> Velocity;
    [FieldOffset(176)] public Vector128<float> Gravity;
    [FieldOffset(192)] public long UserData;
    [FieldOffset(208)] public int SpeedCapMode;
    [FieldOffset(224)] public Vector128<float> LastForward;
    [FieldOffset(240)] public ulong Flags;
    [FieldOffset(248)] public float SpeedLimit;
    [FieldOffset(252)] public float WaterSurfaceHeight;
    [FieldOffset(256)] public float MoveSpeed;
    [FieldOffset(272)] public Vector128<float> AirSurfaceVelocity;
    [FieldOffset(288)] public Vector128<float> TargetPosition;
    [FieldOffset(304)] public float TargetTolerance;
}

[StructLayout(LayoutKind.Explicit, Size = 80)]
public struct Gw2CharacterOutput
{
    [FieldOffset(0)] public Vector128<float> Velocity;
    [FieldOffset(16)] public Vector128<float> InputVelocity;
    [FieldOffset(32)] public int Events;
    [FieldOffset(48)] public Vector128<float> MoveDirection;
    [FieldOffset(64)] public float SpeedLimit;
    [FieldOffset(68)] public float MoveSpeed;
}

public sealed class Gw2CharacterBody
{
    public const ulong MountTypeId = 0x1000EE4912C99469;

    public ulong Flags;
    public ulong TypeId;
    public Vector128<float> ExternalVelocity;
    public Vector128<float> PushVelocity;
    public float PushSpeedLimit;
    public Vector128<float> LaunchVelocity;
    public float MaxLeapSpeed;
    public float LeapApexHeight;
    public int LeapFollowsDirection;
    public float JumpHeight;
    public float FallSpeedLimit;
    public float SupportOverrideTime;
    public int SupportOverride;

    public bool CanLand => (Flags & 0x1000000000000) == 0;
    public bool HasCustomJumpHeight => (Flags & 0x100000000) != 0;
    public bool HasSupportOverride => (Flags & 0x1800000000) != 0 && SupportOverrideTime > 0f;
    public bool IsImmobilized => (Flags & 0x10000000) != 0;
    public bool IsFrozen => (Flags & 0x8000000) != 0;
    public bool ForcesSurfaceSwim => (Flags & 0x800000) != 0;
    public bool IgnoresWater => (Flags & 0x400000) != 0;

    public bool IsSupported(int surfaceState) => HasSupportOverride ? SupportOverride != 0 : surfaceState == 2;
}

public sealed class Gw2CharacterContext(Gw2CharacterBody body)
{
    private const ulong Halted = 0x100000000000;
    private const ulong SkipEnterUpdate = 0x2000000000000000;
    private const ulong KeepsFilterOff = 0x1C00002000000000;
    private const ulong Unmovable = 0x100000000;
    private const float AboveWater = 1.25f;
    private const float BelowSurface = 1.45f;
    private const float SmallLength = 0.01f;
    private const float TinyLengthSquared = 9.9999997e-5f;
    private const float WalkSpeedCap = 3.28125f;
    private const float RunSpeedCap = 5.625f;

    private static readonly Vector128<float> XywMask = Vector128.Create(-1, -1, 0, -1).AsSingle();
    private static readonly Vector128<float> XyzMask = Vector128.Create(-1, -1, -1, 0).AsSingle();
    private static readonly Vector128<float> AxisX = Vector128.Create(1f, 0f, 0f, 0f);
    private static readonly Vector128<float> AxisY = Vector128.Create(0f, 1f, 0f, 0f);
    private static readonly Vector128<float> AxisZ = Vector128.Create(0f, 0f, 1f, 0f);

    public Gw2CharacterBody Body { get; } = body;
    public Gw2CharacterStateType Current = Gw2CharacterStateType.OnGround;
    public Gw2CharacterStateType Previous = Gw2CharacterStateType.OnGround;
    public Vector128<float> PreviousSurfaceVelocity;
    public int StepsInState;
    public bool FilterEnabled;
    public float MaxLinearAcceleration;
    public float MaxLinearVelocity;
    public float Gain;
    public byte EnteredFrom;
    public byte StepsUnsupported;
    public byte StepsSliding;
    public bool UpdatingOnEnter;

    public void Update(in Gw2CharacterInput input, ref Gw2CharacterOutput output)
    {
        output.Velocity = input.Velocity;
        output.InputVelocity = input.Velocity;
        Change(Current, input, ref output);
        if (Current == Previous) UpdateState(Current, input, ref output);
        Previous = Current;
        PreviousSurfaceVelocity = input.SurfaceVelocity;
        if (FilterEnabled) FilterVelocity(input, ref output);
        StepsInState++;
    }

    private void FilterVelocity(in Gw2CharacterInput input, ref Gw2CharacterOutput output)
    {
        var length = Length(output.Velocity);
        var maxVelocity = Broadcast(MaxLinearVelocity);
        if (length.ToScalar() > maxVelocity.ToScalar())
        {
            var estimate = Sse.Reciprocal(length);
            output.Velocity = Sse.Multiply(output.Velocity, Sse.Multiply(Sse.Multiply(Sse.Subtract(Broadcast(2f), Sse.Multiply(length, estimate)), estimate), maxVelocity));
        }
        var inputVelocity = output.InputVelocity;
        var acceleration = Sse.Multiply(Broadcast(input.InverseDeltaTime), Sse.Subtract(output.Velocity, inputVelocity));
        output.Velocity = inputVelocity;
        var maxAcceleration = Broadcast(MaxLinearAcceleration);
        var accelerationLength = Length(acceleration);
        var maxEstimate = Sse.Reciprocal(maxAcceleration);
        var ratio = Sse.Multiply(Sse.Multiply(Sse.Subtract(Broadcast(2f), Sse.Multiply(maxAcceleration, maxEstimate)), maxEstimate), accelerationLength);
        var gain = Broadcast(input.DeltaTime * Gain);
        if (ratio.ToScalar() <= 1f || Current == Gw2CharacterStateType.Jumping)
        {
            output.Velocity = Sse.Add(Sse.Multiply(gain, acceleration), inputVelocity);
        }
        else
        {
            var ratioEstimate = Sse.Reciprocal(ratio);
            output.Velocity = Sse.Add(Sse.Multiply(Sse.Multiply(Sse.Multiply(Sse.Subtract(Broadcast(2f), Sse.Multiply(ratio, ratioEstimate)), ratioEstimate), gain), acceleration), inputVelocity);
        }
    }

    public void SetState(Gw2CharacterStateType next, in Gw2CharacterInput input, ref Gw2CharacterOutput output)
    {
        var previous = Current;
        Current = next;
        EnteredFrom = (byte)previous;
        if ((input.Flags & SkipEnterUpdate) == 0)
        {
            UpdatingOnEnter = true;
            UpdateState(next, input, ref output);
            UpdatingOnEnter = false;
        }
        StepsInState = 0;
    }

    private void Change(Gw2CharacterStateType state, in Gw2CharacterInput input, ref Gw2CharacterOutput output)
    {
        switch (state)
        {
            case Gw2CharacterStateType.OnGround: ChangeOnGround(input, ref output); break;
            case Gw2CharacterStateType.InAir: ChangeInAir(input, ref output); break;
            case Gw2CharacterStateType.Jumping: ChangeJumping(input, ref output); break;
            case Gw2CharacterStateType.Mounted: ChangeMounted(input, ref output); break;
            case Gw2CharacterStateType.Diving: ChangeDiving(input, ref output); break;
            case Gw2CharacterStateType.Swimming: ChangeSwimming(input, ref output); break;
        }
    }

    private void UpdateState(Gw2CharacterStateType state, in Gw2CharacterInput input, ref Gw2CharacterOutput output)
    {
        switch (state)
        {
            case Gw2CharacterStateType.OnGround:
                if (StepsSliding < 4) Walk(input, ref output, false, 1f, 800f);
                else Walk(input, ref output, false, 0.95f, 50f);
                break;
            case Gw2CharacterStateType.InAir: UpdateInAir(input, ref output); break;
            case Gw2CharacterStateType.Jumping: UpdateJumping(input, ref output); break;
            case Gw2CharacterStateType.Mounted: UpdateMounted(input, ref output); break;
            case Gw2CharacterStateType.Diving: Swim(input, ref output, 0.75f, 20f); break;
            case Gw2CharacterStateType.Swimming: Swim(input, ref output, 0.9f, 600f); break;
        }
    }

    private bool AllowsMountedMovement(in Gw2CharacterInput input) =>
        (Body.TypeId == Gw2CharacterBody.MountTypeId || (input.Flags & 0x40000000000000) != 0) && (input.Flags & Unmovable) == 0;

    private bool IsOutOfWater(in Gw2CharacterInput input) =>
        (input.Flags & 0x200000) != 0 || input.WaterSurfaceHeight - AboveWater < input.Position.GetElement(2) || Body.IgnoresWater;

    private Gw2CharacterStateType WaterState(in Gw2CharacterInput input) =>
        input.Position.GetElement(2) >= input.WaterSurfaceHeight - BelowSurface || Body.ForcesSurfaceSwim ? Gw2CharacterStateType.Swimming : Gw2CharacterStateType.Diving;

    private void ChangeOnGround(in Gw2CharacterInput input, ref Gw2CharacterOutput output)
    {
        var flags = input.Flags;
        if ((flags & Halted) != 0) return;
        var graceSteps = Body.HasCustomJumpHeight || (flags & 0x20000000000000) == 0 ? 0 : 4;
        var surfaceSwim = Body.ForcesSurfaceSwim;
        var water = input.WaterSurfaceHeight;
        var height = input.Position.GetElement(2);
        Gw2CharacterStateType next;
        if (IsOutOfWater(input))
        {
            if ((flags & 0x2000000) != 0) next = Gw2CharacterStateType.Jumping;
            else if (Body.IsSupported(input.SurfaceState) || StepsUnsupported < graceSteps)
            {
                if (!AllowsMountedMovement(input)) return;
                next = Gw2CharacterStateType.Mounted;
            }
            else if ((flags & 0x200000) != 0 || water + 0f <= height || !surfaceSwim) next = Gw2CharacterStateType.InAir;
            else next = Gw2CharacterStateType.Swimming;
        }
        else
        {
            next = height >= water - BelowSurface || surfaceSwim ? Gw2CharacterStateType.Swimming : Gw2CharacterStateType.Diving;
        }
        SetState(next, input, ref output);
    }

    private void ChangeInAir(in Gw2CharacterInput input, ref Gw2CharacterOutput output)
    {
        var flags = input.Flags;
        if ((flags & Halted) != 0) return;
        if (!IsOutOfWater(input))
        {
            SetState(WaterState(input), input, ref output);
            return;
        }
        if ((flags & 0x2000000) != 0)
        {
            SetState(Gw2CharacterStateType.Jumping, input, ref output);
            return;
        }
        if (!Body.IsSupported(input.SurfaceState) || !Body.CanLand) return;
        SetState(AllowsMountedMovement(input) ? Gw2CharacterStateType.Mounted : Gw2CharacterStateType.OnGround, input, ref output);
    }

    private void ChangeJumping(in Gw2CharacterInput input, ref Gw2CharacterOutput output)
    {
        var flags = input.Flags;
        if ((flags & Halted) != 0) return;
        if ((flags & 0x10000000000) != 0 || IsOutOfWater(input))
        {
            SetState(Gw2CharacterStateType.InAir, input, ref output);
            if ((input.Flags & 0x10000000000) != 0) output.Events |= 0x10;
            return;
        }
        SetState(WaterState(input), input, ref output);
    }

    private void ChangeMounted(in Gw2CharacterInput input, ref Gw2CharacterOutput output)
    {
        var flags = input.Flags;
        if ((flags & Halted) != 0) return;
        if (!IsOutOfWater(input))
        {
            SetState(WaterState(input), input, ref output);
            return;
        }
        if ((flags & 0x2000000) != 0)
        {
            SetState(Gw2CharacterStateType.Jumping, input, ref output);
            return;
        }
        if (input.SurfaceState == 0 && StepsUnsupported >= 4)
        {
            SetState(Gw2CharacterStateType.InAir, input, ref output);
            return;
        }
        if ((Body.TypeId != Gw2CharacterBody.MountTypeId && (flags & 0x40000000000000) == 0) || (flags & Unmovable) != 0)
            SetState(Gw2CharacterStateType.OnGround, input, ref output);
    }

    private void ChangeDiving(in Gw2CharacterInput input, ref Gw2CharacterOutput output)
    {
        var flags = input.Flags;
        if ((flags & Halted) != 0) return;
        var offset = (flags & 0x800000) != 0 ? 0f : -AboveWater;
        var water = input.WaterSurfaceHeight;
        var height = input.Position.GetElement(2);
        Gw2CharacterStateType next;
        if ((flags & 0x200000) != 0 || offset + water < height || Body.IgnoresWater)
            next = input.SurfaceState != 0 ? Gw2CharacterStateType.OnGround : Gw2CharacterStateType.InAir;
        else if ((height >= water - BelowSurface && input.Forward.GetElement(2) >= 0f) || Body.ForcesSurfaceSwim)
            next = Gw2CharacterStateType.Swimming;
        else if ((flags & 0x2000000) != 0)
            next = Gw2CharacterStateType.Jumping;
        else return;
        SetState(next, input, ref output);
    }

    private void ChangeSwimming(in Gw2CharacterInput input, ref Gw2CharacterOutput output)
    {
        var flags = input.Flags;
        if ((flags & Halted) != 0) return;
        var offset = (flags & 0x800000) != 0 ? 0f : -AboveWater;
        var water = input.WaterSurfaceHeight;
        var height = input.Position.GetElement(2);
        Gw2CharacterStateType next;
        if ((flags & 0x200000) != 0 || offset + water < height || Body.IgnoresWater)
            next = input.SurfaceState != 0 ? Gw2CharacterStateType.OnGround : Gw2CharacterStateType.InAir;
        else if (water - BelowSurface <= height || Body.ForcesSurfaceSwim)
        {
            if ((flags & 0x2000000) == 0) return;
            next = Gw2CharacterStateType.Jumping;
        }
        else next = Gw2CharacterStateType.Diving;
        SetState(next, input, ref output);
    }

    private void CountSupport(int surfaceState)
    {
        if (surfaceState == 1) StepsSliding = StepsSliding + 1 < 0xFF ? (byte)(StepsSliding + 1) : (byte)0xFF;
        if (Body.IsSupported(surfaceState))
        {
            StepsUnsupported = 0;
            StepsSliding = 0;
        }
        else
        {
            StepsUnsupported = StepsUnsupported + 1 < 0xFF ? (byte)(StepsUnsupported + 1) : (byte)0xFF;
        }
    }

    private void RequestFilter(ulong flags, ref Gw2CharacterOutput output)
    {
        if ((flags & KeepsFilterOff) != 0) return;
        FilterEnabled = true;
        output.Events |= 8;
    }

    private Vector128<float> CappedMoveSpeed(in Gw2CharacterInput input)
    {
        var flags = input.Flags;
        var speed = Broadcast(input.MoveSpeed);
        if ((flags & 0x20000) != 0) return Length(Sse.And(Sse.And(input.Velocity, XywMask), XyzMask));
        if ((flags & 0x40000) == 0 || (flags & Unmovable) != 0) return speed;
        if (input.SpeedCapMode == 2)
        {
            if (input.MoveSpeed > RunSpeedCap) speed = Broadcast(RunSpeedCap);
        }
        else if (input.SpeedCapMode == 1 && input.MoveSpeed > WalkSpeedCap)
        {
            speed = Broadcast(WalkSpeedCap);
        }
        return speed;
    }

    private static Vector128<float> FlatForward(in Gw2CharacterInput input, ref Gw2CharacterOutput output, out bool moving)
    {
        var flat = Sse.And(input.Forward, XywMask);
        var lengthSquared = Dot3(flat, flat);
        var inverse = SafeInverseLength(lengthSquared);
        output.MoveDirection = Sse.Multiply(flat, inverse);
        moving = inverse.ToScalar() * lengthSquared.ToScalar() >= SmallLength;
        if (!moving) output.MoveDirection = Vector128<float>.Zero;
        return output.MoveDirection;
    }

    private static Vector128<float> SteeringDirection(in Gw2CharacterInput input, float leftRight, float upDown)
    {
        var forward = input.Forward;
        if (!(Length(forward).ToScalar() > SmallLength)) return Vector128<float>.Zero;
        var side = YZX(Sse.Subtract(Sse.Multiply(YZX(forward), input.Up), Sse.Multiply(YZX(input.Up), forward)));
        var combined = Sse.Add(Sse.Multiply(Normalize(side), Broadcast(leftRight)), Sse.Multiply(forward, Broadcast(upDown)));
        return Normalize(combined);
    }

    private void UpdateInAir(in Gw2CharacterInput input, ref Gw2CharacterOutput output)
    {
        var flags = input.Flags;
        if ((flags & Halted) != 0) return;
        if (!(AllowsMountedMovement(input) && Dot3Scalar(Body.ExternalVelocity, Body.ExternalVelocity) < TinyLengthSquared))
        {
            if ((flags & 0x8000) != 0) Walk(input, ref output, true, 1f, 800f);
            else Walk(input, ref output, true, 0.95f, 50f);
            return;
        }
        FlatForward(input, ref output, out var moving);
        var upDown = moving ? input.InputUpDown : 0f;
        var leftRight = moving ? input.InputLeftRight : 0f;
        var steering = SteeringDirection(input, leftRight, upDown);
        var deltaTime = input.DeltaTime;
        var speed = CappedMoveSpeed(input);
        if (MathF.Abs(leftRight) + MathF.Abs(upDown) >= SmallLength)
        {
            output.MoveSpeed = speed.ToScalar();
            var limit = (deltaTime + deltaTime) * speed.ToScalar();
            var remaining = speed.ToScalar() - Dot3Scalar(input.Velocity, steering);
            if (limit - remaining < 0f) remaining = limit;
            var applied = 0f - remaining < 0f ? remaining : 0f;
            output.Velocity = Sse.Add(Sse.Multiply(Broadcast(applied), steering), output.Velocity);
        }
        else
        {
            output.MoveSpeed = 0f;
        }
        output.Velocity = Sse.Add(Sse.Multiply(Broadcast(deltaTime), input.Gravity), output.Velocity);
    }

    private void UpdateJumping(in Gw2CharacterInput input, ref Gw2CharacterOutput output)
    {
        var flags = input.Flags;
        if ((flags & Halted) != 0) return;
        if ((flags & 0x10000000000) != 0)
        {
            output.Velocity = input.Velocity;
            output.MoveDirection = input.Forward;
            return;
        }
        var depth = 0f;
        if ((flags & 0x200000) == 0)
        {
            var surface = input.WaterSurfaceHeight - AboveWater;
            if (surface >= input.Position.GetElement(2)) depth = (surface - input.Position.GetElement(2)) + 0f;
        }
        var gravity = MathF.Abs(input.Gravity.GetElement(2));
        Vector128<float> launch;
        Vector128<float> velocity;
        if ((flags & 0x4000000) != 0)
        {
            var replace = (flags & 0x8000000) != 0;
            var project = replace || (flags & 0x10000000) == 0;
            launch = Body.LaunchVelocity;
            launch = launch.WithElement(2, launch.GetElement(2) + MathF.Sqrt((gravity + gravity) * depth));
            if (replace)
            {
                output.Velocity = launch;
                output.SpeedLimit = input.SpeedLimit;
                output.MoveDirection = input.Forward;
                return;
            }
            velocity = input.Velocity;
            if (!project)
            {
                output.Velocity = Sse.Add(velocity, launch);
                output.SpeedLimit = input.SpeedLimit;
                output.MoveDirection = input.Forward;
                return;
            }
        }
        else
        {
            var height = Body.HasCustomJumpHeight ? Body.JumpHeight : 1.75f;
            velocity = input.Velocity;
            launch = Vector128.Create(0f, 0f, MathF.Sqrt((gravity + gravity) * (height + depth)), 0f);
        }
        var up = input.Up;
        var current = Dot3(Sse.Multiply(Sse.Subtract(velocity, input.SurfaceVelocity), up), Vector128<float>.One);
        var wanted = Dot3(Sse.Multiply(up, launch), Vector128<float>.One);
        output.Velocity = wanted.ToScalar() <= current.ToScalar() ? velocity : Sse.Add(Sse.Multiply(Sse.Subtract(wanted, current), up), velocity);
        output.MoveDirection = input.Forward;
    }

    private void UpdateMounted(in Gw2CharacterInput input, ref Gw2CharacterOutput output)
    {
        var externalVelocity = Body.ExternalVelocity;
        var pushVelocity = Body.PushVelocity;
        var flatExternal = Sse.And(Sse.And(externalVelocity, XywMask), XyzMask);
        var flatPush = Sse.And(Sse.And(pushVelocity, XywMask), XyzMask);
        var flags = input.Flags;
        if ((flags & Halted) != 0) return;
        CountSupport(input.SurfaceState);
        flags = input.Flags;
        if ((flags & 0x8000000) != 0)
        {
            RequestFilter(flags, ref output);
            output.Events |= 2;
        }
        else if ((flags & 0x10000000) != 0)
        {
            RequestFilter(flags, ref output);
            output.Events |= 4;
        }
        FlatForward(input, ref output, out var moving);
        var upDown = moving ? input.InputUpDown : 0f;
        var leftRight = moving ? input.InputLeftRight : 0f;
        var steering = SteeringDirection(input, leftRight, upDown);
        var deltaTime = Broadcast(input.DeltaTime);
        var speed = CappedMoveSpeed(input);

        var difference = Sse.Subtract(output.Velocity, flatExternal);
        var accelerationLimit = Length(flatExternal);
        accelerationLimit = accelerationLimit.WithElement(0, (accelerationLimit.ToScalar() + 7f) * deltaTime.ToScalar());
        var differenceLengthSquared = Dot3(difference, difference);
        var differenceInverse = SafeInverseLength(differenceLengthSquared);
        var step = Sse.Multiply(Sse.Multiply(differenceInverse, difference), Splat(accelerationLimit, 0));
        if (accelerationLimit.ToScalar() <= Sse.Multiply(differenceInverse, differenceLengthSquared).ToScalar())
            flatExternal = Sse.Subtract(output.Velocity, step);
        output.Velocity = flatExternal;

        if (MathF.Abs(leftRight) + MathF.Abs(upDown) >= SmallLength)
        {
            output.MoveSpeed = speed.ToScalar();
            var limit = (speed.ToScalar() + 7f) * deltaTime.ToScalar();
            var remaining = speed.ToScalar() - Dot3Scalar(input.Velocity, steering);
            if (limit - remaining < 0f) remaining = limit;
            if (0f - remaining >= 0f) remaining = 0f;
            output.Velocity = Sse.Add(Sse.Multiply(Broadcast(remaining), steering), flatExternal);
        }
        else
        {
            output.MoveSpeed = 0f;
        }

        var gravityTime = deltaTime;
        if (SurfaceAngle(input) <= 0.2617994f) gravityTime = Vector128.CreateScalar(deltaTime.ToScalar() * deltaTime.ToScalar()).WithElement(1, deltaTime.GetElement(1)).WithElement(2, deltaTime.GetElement(2)).WithElement(3, deltaTime.GetElement(3));
        var rising = pushVelocity.GetElement(2) + externalVelocity.GetElement(2);
        output.Velocity = Sse.Add(Sse.Multiply(Splat(gravityTime, 0), input.Gravity), output.Velocity);
        if (rising > 0f)
        {
            var threshold = 0f;
            if ((input.Flags & 0x30000000000) != 0)
            {
                if ((input.Flags & Unmovable) != 0) threshold = input.DeltaTime * 40.625f;
            }
            else
            {
                threshold = -input.Gravity.GetElement(2) * input.DeltaTime;
            }
            if (rising > threshold) SetState(Gw2CharacterStateType.InAir, input, ref output);
        }
        ApplyPush(input.Flags, flatPush, Body.PushSpeedLimit, ref output.Velocity);
        ClampMountedHorizontalSpeed(ref output.Velocity);
    }

    private static void ClampMountedHorizontalSpeed(ref Vector128<float> velocity)
    {
        var x = velocity.GetElement(0);
        var y = velocity.GetElement(1);
        var z = velocity.GetElement(2);
        var squared = y * y + x * x;
        if (!(squared > 169f)) return;
        var scaled = Sse.Multiply(Broadcast(169f / squared), velocity);
        velocity = scaled.WithElement(2, z);
    }

    private static float SurfaceAngle(in Gw2CharacterInput input)
    {
        var normal = input.SurfaceState != 0 ? input.SurfaceNormal : AxisZ;
        var up = input.Up;
        var cosine = Dot3Scalar(Sse.Multiply(SafeInverseLength(Dot3(up, up)), up), Sse.Multiply(SafeInverseLength(Dot3(normal, normal)), normal));
        return ClampedAcos(cosine);
    }

    public static float ClampedAcos(float value)
    {
        var clamped = value;
        var upper = value - 1f < 0f ? value : 1f;
        if (1f - value < 0f) clamped = upper;
        var lower = -1f - clamped < 0f ? clamped : -1f;
        if (clamped - -1f < 0f) clamped = lower;
        return MathF.Acos(clamped);
    }

    private void Swim(in Gw2CharacterInput input, ref Gw2CharacterOutput output, float responsiveness, float acceleration)
    {
        var flags = input.Flags;
        if ((flags & Halted) != 0) return;
        var body = Body;
        var upDown = Broadcast(input.InputUpDown);
        var leftRight = Broadcast(input.InputLeftRight);
        var surfaceSwim = body.ForcesSurfaceSwim;
        var forward = input.Forward;
        output.MoveDirection = forward;
        if (surfaceSwim)
        {
            var flat = Sse.And(forward, XywMask);
            output.MoveDirection = flat;
            var lengthSquared = Dot3(flat, flat);
            var inverse = SafeInverseLength(lengthSquared);
            output.MoveDirection = Sse.Multiply(flat, inverse);
            if (lengthSquared.ToScalar() * inverse.ToScalar() < SmallLength)
            {
                output.MoveDirection = Vector128<float>.Zero;
                upDown = Vector128<float>.Zero;
                leftRight = Vector128<float>.Zero;
            }
        }
        var speed = CappedMoveSpeed(input);
        var relativeVelocity = Sse.Subtract(input.Velocity, body.ExternalVelocity);
        var wanted = Sse.MoveLowToHigh(Sse.UnpackLow(Sse.Multiply(leftRight, speed), Sse.Multiply(speed, upDown)), Vector128<float>.Zero);
        var heading = forward;
        if ((Sse.MoveMask(Sse.CompareEqual(Vector128<float>.Zero, heading)) & 7) == 7) heading = input.LastForward;
        var basis = BasisInAir(heading, input.Up);
        var push = body.PushVelocity;
        var pushLimit = body.PushSpeedLimit;
        if (Dot3Scalar(push, push) > TinyLengthSquared && Dot3Scalar(wanted, wanted) < TinyLengthSquared)
            wanted = PushAlongBasis(basis, relativeVelocity, push, pushLimit);
        var local = InverseRotate(basis, Sse.Subtract(relativeVelocity, input.SurfaceVelocity));
        var correction = Sse.Subtract(wanted, local);
        if ((input.Flags & 0x1000) == 0) correction = LimitCorrection(correction, responsiveness, acceleration * input.DeltaTime);
        local = Sse.Add(local, Sse.Multiply(Broadcast(responsiveness), correction));
        output.Velocity = Sse.Add(input.SurfaceVelocity, Rotate(basis, local));
        output.MoveSpeed = Length(wanted).ToScalar();
        ApplyPush(input.Flags, push, pushLimit, ref output.Velocity);
        output.Velocity = Sse.Add(output.Velocity, body.ExternalVelocity);
        flags = input.Flags;
        if ((flags & 0x8000000) != 0)
        {
            RequestFilter(flags, ref output);
            output.Events |= 2;
        }
        else if ((flags & 0x10000000) != 0)
        {
            RequestFilter(flags, ref output);
            output.Events |= 4;
        }
        if (EnteredFrom == (byte)Gw2CharacterStateType.InAir && UpdatingOnEnter)
            output.Velocity = Sse.Multiply(SwimEntryDamping, output.Velocity);
        if (!body.HasSupportOverride && input.Forward.GetElement(2) > 0f)
        {
            var height = input.Position.GetElement(2);
            if (height >= input.WaterSurfaceHeight - 2.75f)
            {
                var cap = ((input.WaterSurfaceHeight - 1.3f - height) * 0.98000002f) * 25f;
                if (output.Velocity.GetElement(2) >= cap) output.Velocity = output.Velocity.WithElement(2, cap);
            }
        }
        if (surfaceSwim)
        {
            var surface = input.WaterSurfaceHeight - 0.25f;
            var predicted = Sse.Add(Sse.Multiply(Broadcast(input.DeltaTime), output.Velocity), input.Position).GetElement(2);
            if (surface > predicted)
                output.Velocity = Sse.Add(Vector128.Create(0f, 0f, (surface - predicted) * input.InverseDeltaTime, 0f), output.Velocity);
        }
    }

    private static readonly Vector128<float> SwimEntryDamping = Vector128.Create(1f, 1f, 0.35f, 0f);

    private static Vector128<float> LimitCorrection(Vector128<float> correction, float responsiveness, float limit)
    {
        var limitVector = Broadcast(limit);
        var lengthSquared = Dot3(correction, correction);
        if (!(lengthSquared.ToScalar() * responsiveness > limitVector.ToScalar() * limitVector.ToScalar())) return correction;
        var inverseResponsiveness = 1f / responsiveness;
        return Sse.Multiply(Sse.Multiply(correction, SafeInverseLength(lengthSquared)), Sse.Multiply(Broadcast(inverseResponsiveness), limitVector));
    }

    private void Walk(in Gw2CharacterInput original, ref Gw2CharacterOutput output, bool airborne, float responsiveness, float acceleration)
    {
        var input = original;
        var flags = input.Flags;
        if ((flags & Halted) != 0) return;
        CountSupport(input.SurfaceState);
        var body = Body;
        var grounded = !airborne && body.IsSupported(input.SurfaceState);
        var upDown = Broadcast(input.InputUpDown);
        var leftRight = Broadcast(input.InputLeftRight);
        var targetDistance = Vector128<float>.Zero;
        if ((flags & 0x8000) != 0)
        {
            var toTarget = Sse.And(Sse.Subtract(input.TargetPosition, input.Position), XywMask);
            var lengthSquared = Dot3(toTarget, toTarget);
            var inverse = SafeInverseLength(lengthSquared);
            output.MoveDirection = Sse.Multiply(toTarget, inverse);
            targetDistance = Sse.Multiply(inverse, lengthSquared);
            if (input.TargetTolerance >= targetDistance.ToScalar())
            {
                output.Events |= 2;
                upDown = Vector128<float>.Zero;
                output.MoveDirection = Vector128<float>.Zero;
                leftRight = Vector128<float>.Zero;
            }
        }
        else
        {
            var forward = input.Forward;
            output.MoveDirection = forward;
            if ((flags & Unmovable) != 0)
            {
                var lengthSquared = Dot3(forward, forward);
                var inverse = SafeInverseLength(lengthSquared);
                output.MoveDirection = Sse.Multiply(inverse, forward);
                if (inverse.ToScalar() * lengthSquared.ToScalar() >= SmallLength)
                {
                    if (grounded && !body.HasSupportOverride && (flags & 0x40000000000) == 0)
                    {
                        var scaledSpeed = 0f;
                        var flat = Sse.And(forward, XywMask);
                        var flatLengthSquared = Dot3(flat, flat);
                        var flatInverse = SafeInverseLength(flatLengthSquared);
                        output.MoveDirection = Sse.Multiply(flatInverse, flat);
                        if (flatInverse.ToScalar() * flatLengthSquared.ToScalar() >= SmallLength)
                        {
                            scaledSpeed = forward.GetElement(0) == 0f
                                ? (forward.GetElement(1) / output.MoveDirection.GetElement(1)) * input.MoveSpeed
                                : (forward.GetElement(0) / output.MoveDirection.GetElement(0)) * input.MoveSpeed;
                        }
                        else
                        {
                            output.MoveDirection = Vector128<float>.Zero;
                            upDown = Vector128<float>.Zero;
                            leftRight = Vector128<float>.Zero;
                        }
                        input.MoveSpeed = scaledSpeed;
                    }
                }
                else
                {
                    output.MoveDirection = Vector128<float>.Zero;
                    upDown = Vector128<float>.Zero;
                    leftRight = Vector128<float>.Zero;
                }
            }
            else
            {
                var flat = Sse.And(forward, XywMask);
                var lengthSquared = Dot3(flat, flat);
                var inverse = SafeInverseLength(lengthSquared);
                output.MoveDirection = Sse.Multiply(flat, inverse);
                if (inverse.ToScalar() * lengthSquared.ToScalar() < SmallLength)
                {
                    output.MoveDirection = Vector128<float>.Zero;
                    upDown = Vector128<float>.Zero;
                    leftRight = Vector128<float>.Zero;
                }
            }
        }

        var externalVelocity = body.ExternalVelocity;
        var pushVelocity = body.PushVelocity;
        var pushLimit = body.PushSpeedLimit;
        var pushing = Dot3Scalar(pushVelocity, pushVelocity) > TinyLengthSquared;
        var surfaceVelocity = input.SurfaceVelocity;
        if ((flags & 0x8000000) != 0)
        {
            Leap(input, ref output, airborne, ref targetDistance);
        }
        else
        {
            var speed = CappedMoveSpeed(input);
            var heading = output.MoveDirection;
            var reportedLimit = input.SpeedLimit;
            if (Dot3Scalar(heading, heading) == 0f) heading = input.LastForward;
            var wanted = Sse.MoveLowToHigh(Sse.UnpackLow(Sse.Multiply(speed, leftRight), Sse.Multiply(speed, upDown)), Vector128<float>.Zero);
            if (!body.IsSupported(input.SurfaceState)) surfaceVelocity = Sse.Add(input.SurfaceVelocity, input.AirSurfaceVelocity);
            if ((input.Flags & 0x400000000) != 0)
            {
                var direction = output.MoveDirection;
                var moved = Dot3Scalar(direction, direction) == 0f ? input.Velocity : Sse.Add(Sse.Multiply(speed, direction), input.Velocity);
                var flat = Sse.And(moved, XywMask);
                var lengthSquared = Dot3(flat, flat);
                var inverse = SafeInverseLength(lengthSquared);
                var flatSpeed = Sse.Multiply(lengthSquared, inverse);
                heading = Sse.Multiply(flat, inverse);
                if (flatSpeed.ToScalar() < SmallLength) heading = AxisX;
                if (input.SpeedLimit - flatSpeed.ToScalar() < 0f) flatSpeed = Vector128.CreateScalar(input.SpeedLimit);
                if (speed.ToScalar() - flatSpeed.ToScalar() < 0f) speed = speed.WithElement(0, flatSpeed.ToScalar());
                if (input.SpeedLimit - speed.ToScalar() >= 0f) reportedLimit = speed.ToScalar();
                wanted = Sse.UnpackLow(Vector128<float>.Zero, Sse.UnpackLow(flatSpeed, Vector128<float>.Zero));
            }
            var relativeVelocity = input.Velocity;
            if (!body.IsFrozen && !body.IsImmobilized) relativeVelocity = Sse.Subtract(relativeVelocity, externalVelocity);
            Basis basis;
            if ((input.Flags & 0x40000000000) != 0 || (input.Flags & 0x10000000000) != 0) basis = BasisInAir(heading, input.Up);
            else if (input.SurfaceState != 0) basis = BasisOnSurface(heading, input.Up, input.SurfaceNormal);
            else basis = BasisInAir(heading, input.Up);
            if (pushing) PushClamp(basis, relativeVelocity, pushVelocity, pushLimit, ref wanted);
            input.SurfaceVelocity = surfaceVelocity;
            var local = InverseRotate(basis, Sse.Subtract(relativeVelocity, surfaceVelocity));
            var correction = Sse.Subtract(wanted, local);
            if ((input.Flags & 0x1000) == 0) correction = LimitCorrection(correction, responsiveness, input.DeltaTime * acceleration);
            local = Sse.Add(local, Sse.Multiply(Broadcast(responsiveness), correction));
            output.SpeedLimit = reportedLimit;
            output.Velocity = Sse.Add(surfaceVelocity, Rotate(basis, local));
            output.MoveSpeed = Length(wanted).ToScalar();
        }

        flags = input.Flags;
        if (grounded && !body.HasSupportOverride)
        {
            if ((flags & 0x40000000000) == 0)
            {
                var baseVelocity = input.SurfaceVelocity;
                var up = input.Up;
                var relative = Sse.Subtract(output.Velocity, baseVelocity);
                if (Dot3Scalar(relative, up) > 0.001f) relative = ProjectOntoSurface(relative, up, input.SurfaceNormal);
                output.Velocity = Sse.Add(baseVelocity, relative);
            }
            if ((flags & 0x10000000) != 0 && !airborne)
            {
                RequestFilter(flags, ref output);
                output.Events |= 4;
            }
            var external = externalVelocity;
            var push = pushVelocity;
            var rising = externalVelocity.GetElement(2) + pushVelocity.GetElement(2);
            if (rising <= 0f || rising <= RisingThreshold(input))
            {
                push = push.WithElement(2, 0f);
                external = external.WithElement(2, 0f);
            }
            else
            {
                SetState(Gw2CharacterStateType.InAir, input, ref output);
            }
            ApplyPush(input.Flags, push, pushLimit, ref output.Velocity);
            output.Velocity = Sse.Add(output.Velocity, external);
        }
        else
        {
            if ((flags & 0x30000000000) == 0)
            {
                var gravityTime = input.DeltaTime;
                if (!airborne && !body.HasSupportOverride) gravityTime *= 3f;
                var up = input.Up;
                var velocity = output.Velocity;
                var adjusted = Sse.Add(
                    Sse.Add(
                        Sse.Add(Sse.Multiply(Sse.Subtract(Vector128<float>.Zero, Dot3(up, velocity)), up), velocity),
                        Sse.Multiply(Dot3(up, input.Velocity), up)),
                    Sse.Multiply(Broadcast(gravityTime), input.Gravity));
                output.Velocity = adjusted;
                var fallLimit = body.FallSpeedLimit;
                if (fallLimit < 0f && (flags & 0x8000000) == 0 && pushVelocity.GetElement(2) >= 0f)
                {
                    var vertical = adjusted.GetElement(2);
                    if (fallLimit > vertical)
                        output.Velocity = adjusted.WithElement(2, vertical * 0.050000012f + fallLimit * 0.94999999f);
                }
            }
            if (!body.IsFrozen && !body.IsImmobilized)
            {
                ApplyPush(input.Flags, pushVelocity, pushLimit, ref output.Velocity);
                output.Velocity = Sse.Add(externalVelocity, output.Velocity);
            }
        }

        if (body.IgnoresWater || body.ForcesSurfaceSwim)
        {
            var predicted = Sse.Add(Sse.Multiply(Broadcast(input.DeltaTime), output.Velocity), input.Position).GetElement(2);
            if (input.WaterSurfaceHeight - AboveWater > predicted)
            {
                var lift = ((input.WaterSurfaceHeight - AboveWater) - predicted) * input.InverseDeltaTime;
                output.Velocity = Sse.Add(Vector128.Create(0f, 0f, lift, 0f), output.Velocity);
            }
        }

        if ((flags & 0x8000) != 0)
        {
            var velocity = output.Velocity;
            var travel = Length(Sse.And(Sse.And(velocity, XywMask), XyzMask));
            travel = travel.WithElement(0, travel.ToScalar() * input.DeltaTime);
            var travelSplat = Splat(travel, 0);
            if (travel.ToScalar() > 0f && input.TargetTolerance + travelSplat.ToScalar() > targetDistance.ToScalar())
            {
                output.Events |= 1;
                var estimate = Sse.Reciprocal(travelSplat);
                var scale = Sse.Multiply(Sse.Multiply(Sse.Subtract(Broadcast(2f), Sse.Multiply(travelSplat, estimate)), estimate), Splat(targetDistance, 0));
                output.Velocity = Sse.Multiply(Sse.MoveLowToHigh(Sse.UnpackLow(scale, scale), AxisX), velocity);
            }
        }
    }

    private static float RisingThreshold(in Gw2CharacterInput input)
    {
        if ((input.Flags & 0x30000000000) != 0) return (input.Flags & Unmovable) != 0 ? input.DeltaTime * 40.625f : 0f;
        return -input.Gravity.GetElement(2) * input.DeltaTime;
    }

    private void Leap(in Gw2CharacterInput input, ref Gw2CharacterOutput output, bool airborne, ref Vector128<float> targetDistance)
    {
        var body = Body;
        var timeToTarget = 0f;
        var reachable = false;
        var toTarget = Sse.And(Sse.Subtract(input.TargetPosition, input.Position), XywMask);
        var distanceSquared = Dot3(toTarget, toTarget);
        var distanceInverse = SafeInverseLength(distanceSquared);
        targetDistance = Sse.Multiply(distanceInverse, distanceSquared);
        var direction = Sse.Multiply(distanceInverse, toTarget);
        var velocity = input.Velocity;
        var vertical = velocity.GetElement(2);
        var flatVelocity = Sse.And(velocity, XywMask);
        var flatSquared = Dot3(flatVelocity, flatVelocity);
        var flatInverse = SafeInverseLength(flatSquared);
        var apex = body.LeapApexHeight;
        var height = input.Position.GetElement(2);
        var gravity = MathF.Abs(input.Gravity.GetElement(2));
        var flatSpeed = Sse.Multiply(flatInverse, flatSquared);
        var targetHeight = input.TargetPosition.GetElement(2);
        var horizontalSpeed = Splat(flatSpeed, 0);
        var distance = targetDistance;
        if (body.LeapFollowsDirection != 0)
        {
            var alignment = Sse.Multiply(Sse.Multiply(flatInverse, flatVelocity), direction);
            if (Dot3Scalar(alignment, Vector128<float>.One) >= 0.96592581f)
            {
                timeToTarget = targetDistance.ToScalar() / flatSpeed.ToScalar();
                reachable = timeToTarget > 0f;
            }
        }
        else
        {
            if (MathF.Abs(targetHeight - apex) > SmallLength)
            {
                var rise = targetHeight - height;
                var discriminant = vertical * vertical - (gravity + gravity) * rise;
                if (discriminant >= 0f && gravity != 0f)
                {
                    var root = MathF.Sqrt(discriminant);
                    var half = (vertical < 0f ? vertical - root : vertical + root) * 0.5f;
                    var first = rise / half;
                    timeToTarget = (half + half) / gravity;
                    if (timeToTarget - first < 0f) timeToTarget = first;
                    reachable = timeToTarget > 0f;
                }
            }
            else
            {
                if (gravity != 0f) timeToTarget = vertical / gravity;
                reachable = timeToTarget > 0f;
            }
        }
        var result = velocity;
        if (airborne)
        {
            if (reachable)
            {
                var deltaTime = input.DeltaTime;
                var inverseDeltaTime = input.InverseDeltaTime;
                if (vertical > 0f && apex > height)
                {
                    var fall = apex - targetHeight < 0f ? 0f : MathF.Sqrt((gravity + gravity) * (apex - targetHeight));
                    var steps = Ceiling((timeToTarget - fall / gravity) * inverseDeltaTime);
                    vertical = MathF.Min((apex - height) / (deltaTime * steps) + (gravity * 0.5f) * (deltaTime * steps), vertical);
                }
                var totalSteps = Ceiling(inverseDeltaTime * timeToTarget);
                var reach = (deltaTime * totalSteps) * horizontalSpeed.ToScalar();
                var reachSplat = Broadcast(reach);
                if (reach > 0f && reachSplat.ToScalar() + input.TargetTolerance > distance.ToScalar())
                {
                    var maxSpeed = Vector128.CreateScalar(body.MaxLeapSpeed);
                    var estimate = Sse.Reciprocal(reachSplat);
                    var needed = Sse.Multiply(Sse.Multiply(Sse.Multiply(Sse.Subtract(Broadcast(2f), Sse.Multiply(reachSplat, estimate)), estimate), Splat(distance, 0)), horizontalSpeed);
                    if (needed.ToScalar() - maxSpeed.ToScalar() < 0f) maxSpeed = needed;
                    horizontalSpeed = Splat(maxSpeed, 0);
                }
                var planar = Sse.Multiply(direction, horizontalSpeed);
                result = Vector128.Create(planar.GetElement(0), planar.GetElement(1), vertical, planar.GetElement(3));
            }
        }
        else
        {
            RequestFilter(input.Flags, ref output);
            output.Events |= 2;
        }
        output.SpeedLimit = input.SpeedLimit;
        output.Velocity = result;
    }

    public static float Ceiling(float value)
    {
        var shifted = BitConverter.SingleToInt32Bits(value + 14680064f);
        var rounded = (float)((shifted & 0x3FFFFF) - 0x200000);
        return value > rounded ? rounded + 1f : rounded;
    }

    private static Vector128<float> ProjectOntoSurface(Vector128<float> relative, Vector128<float> up, Vector128<float> normal)
    {
        var lengthSquared = Dot3(relative, relative);
        var inverse = SafeInverseLength(lengthSquared);
        var direction = Sse.Multiply(relative, inverse);
        var length = Sse.Multiply(lengthSquared, inverse);
        length = length.WithElement(0, length.ToScalar() / Dot3Scalar(normal, up));
        var side = YZX(Sse.Subtract(Sse.Multiply(YZX(direction), normal), Sse.Multiply(YZX(normal), direction)));
        var along = Sse.Subtract(Sse.Multiply(YZX(up), side), Sse.Multiply(YZX(side), up));
        return Sse.Multiply(Splat(length, 0), YZX(along));
    }

    private static void PushClamp(in Basis basis, Vector128<float> velocity, Vector128<float> push, float limit, ref Vector128<float> wanted)
    {
        if (!(Dot3Scalar(wanted, wanted) < TinyLengthSquared)) return;
        wanted = PushAlongBasis(basis, velocity, push, limit);
    }

    private static Vector128<float> PushAlongBasis(in Basis basis, Vector128<float> velocity, Vector128<float> push, float limit)
    {
        var localVelocity = InverseRotate(basis, velocity);
        var localPush = InverseRotate(basis, push);
        var direction = Normalize(localPush);
        var along = Dot3(direction, localVelocity).ToScalar();
        var capped = along - limit < 0f ? along : limit;
        if (limit - along < 0f) along = capped;
        var positive = 0f - along < 0f ? along : 0f;
        if (along - 0f < 0f) along = positive;
        return Sse.Multiply(Broadcast(along), direction);
    }

    public static void ApplyPush(ulong flags, Vector128<float> push, float limit, ref Vector128<float> velocity)
    {
        if ((flags & Unmovable) != 0) return;
        var pushSquared = Dot3(push, push);
        if (!(pushSquared.ToScalar() >= TinyLengthSquared)) return;
        var original = velocity;
        var combined = Sse.Add(velocity, push);
        var velocitySquaredParts = Sse.Multiply(velocity, velocity);
        var combinedSquared = Dot3(combined, combined);
        if (combinedSquared.ToScalar() <= 1024f)
        {
            var pushInverse = SafeInverseLength(pushSquared);
            var pushLength = Sse.Multiply(pushSquared, pushInverse);
            var velocitySquared = Sum3(velocitySquaredParts);
            var pushDirection = Sse.Multiply(pushInverse, push);
            var velocityInverse = SafeInverseLength(velocitySquared);
            var velocityDirection = Sse.Multiply(velocityInverse, original);
            var speed = Sse.Multiply(velocityInverse, velocitySquared);
            var alongPush = speed.ToScalar() * Dot3Scalar(velocityDirection, pushDirection);
            var across = Sse.Subtract(original, Sse.Multiply(Broadcast(alongPush), pushDirection));
            var raised = alongPush - limit < 0f ? limit : alongPush;
            var reach = pushLength.ToScalar() + alongPush;
            if (reach - raised < 0f) raised = reach;
            var result = Sse.Add(Sse.Multiply(Broadcast(raised), pushDirection), across);
            velocity = result;
            var resultSquared = Dot3(result, result);
            var resultInverse = SafeInverseLength(resultSquared);
            var resultLength = resultInverse.ToScalar() * resultSquared.ToScalar();
            var resultDirection = Sse.Multiply(resultInverse, result);
            var cap = speed.ToScalar() - limit < 0f ? limit : speed.ToScalar();
            if (resultLength > cap) velocity = Sse.Multiply(Broadcast(cap), resultDirection);
        }
        else
        {
            var speed = Length(Sum3(velocitySquaredParts)).ToScalar();
            var velocitySquared = Sum3(velocitySquaredParts);
            var length = Sse.AndNot(Sse.CompareLessThanOrEqual(velocitySquared, Vector128<float>.Zero), Sse.Multiply(InverseSquareRoot(velocitySquared), velocitySquared)).ToScalar();
            if (length - limit < 0f) length = limit;
            velocity = combined;
            var combinedInverse = SafeInverseLength(combinedSquared);
            if (combinedInverse.ToScalar() * combinedSquared.ToScalar() > length)
                velocity = Sse.Multiply(Sse.Multiply(combined, combinedInverse), Broadcast(length));
            _ = speed;
        }
    }

    public readonly struct Basis(Vector128<float> side, Vector128<float> forward, Vector128<float> up)
    {
        public readonly Vector128<float> Side = side;
        public readonly Vector128<float> Forward = forward;
        public readonly Vector128<float> Up = up;
    }

    public static Basis BasisInAir(Vector128<float> heading, Vector128<float> up)
    {
        if (Dot3Scalar(heading, heading) < TinyLengthSquared) heading = AxisY;
        var headingYzx = YZX(heading);
        var side = YZX(Sse.Subtract(Sse.Multiply(YZX(up), heading), Sse.Multiply(headingYzx, up)));
        if (Dot3Scalar(side, side) < TinyLengthSquared) side = AxisX;
        side = Normalize(side);
        var top = YZX(Sse.Subtract(Sse.Multiply(headingYzx, side), Sse.Multiply(YZX(side), heading)));
        if (Dot3Scalar(top, top) < TinyLengthSquared) top = AxisZ;
        return new Basis(side, heading, Normalize(top));
    }

    public static Basis BasisOnSurface(Vector128<float> heading, Vector128<float> up, Vector128<float> normal)
    {
        var side = YZX(Sse.Subtract(Sse.Multiply(YZX(up), heading), Sse.Multiply(YZX(heading), up)));
        if (Dot3Scalar(side, side) < TinyLengthSquared) side = AxisX;
        side = Normalize(side);
        var sideYzx = YZX(side);
        var forward = YZX(Sse.Subtract(Sse.Multiply(normal, sideYzx), Sse.Multiply(YZX(normal), side)));
        if (Dot3Scalar(forward, forward) < TinyLengthSquared) forward = AxisY;
        forward = Normalize(forward);
        var top = YZX(Sse.Subtract(Sse.Multiply(YZX(forward), side), Sse.Multiply(sideYzx, forward)));
        if (Dot3Scalar(top, top) < TinyLengthSquared) top = AxisZ;
        return new Basis(side, forward, Normalize(top));
    }

    public static Vector128<float> InverseRotate(in Basis basis, Vector128<float> vector) =>
        HavokBvTreeCast.InverseRotate(new HavokTransform { Column0 = basis.Side, Column1 = basis.Forward, Column2 = basis.Up }, vector);

    public static Vector128<float> Rotate(in Basis basis, Vector128<float> vector) =>
        Sse.Add(Sse.Add(Sse.Multiply(Splat(vector, 1), basis.Forward), Sse.Multiply(Splat(vector, 0), basis.Side)), Sse.Multiply(Splat(vector, 2), basis.Up));

    private static Vector128<float> Sum3(Vector128<float> parts) => Sse.Add(Sse.Add(Splat(parts, 1), Splat(parts, 0)), Splat(parts, 2));

    private static Vector128<float> SafeInverseLength(Vector128<float> lengthSquared) =>
        Sse.AndNot(Sse.CompareLessThanOrEqual(lengthSquared, Vector128<float>.Zero), InverseSquareRoot(lengthSquared));

    private static Vector128<float> Normalize(Vector128<float> vector)
    {
        var lengthSquared = Dot3(vector, vector);
        return Sse.Multiply(SafeInverseLength(lengthSquared), vector);
    }

    private static Vector128<float> Length(Vector128<float> vector)
    {
        var lengthSquared = Dot3(vector, vector);
        return Sse.AndNot(Sse.CompareLessThanOrEqual(lengthSquared, Vector128<float>.Zero), Sse.Multiply(InverseSquareRoot(lengthSquared), lengthSquared));
    }
}
