using System.Numerics;
using Gw2.Dat.Content;
using Gw2.Formats;

namespace Gw2.Client.Model;

public sealed class Gw2BoneConstraintSimulator
{
    public const uint AngleLimitFlag = 0x1;
    public const uint TwistLimitFlag = 0x2;
    public const uint EllipticalAngleLimitFlag = 0x8;
    public const uint PlanarAngleLimitFlag = 0x10;
    public const uint DistanceLimitFlag = 0x4;
    public const uint ResetsToBindPositionFlag = 0x80;
    public const uint RedirectsBoneFlag = 0x40;
    public const uint CollidesFlag = 0x20;
    public const uint IgnoresModelSpeedFlag = 0x100;
    public const uint FollowsModelMotionFlag = 0x200;
    public const uint FollowsModelMotionByBlendFlag = 0x400;
    private const uint RootParentIndex = 0xFFFFFFFF;
    private const float MinimumStep = 1e-6f;
    private const float MaximumStep = 0.5f;
    private const float GravityScale = 384f;
    private const float WindScale = 1f / 60f;
    private const float TeleportDistanceSquared = 10000f;
    private const float VelocityLimit = 10000f;
    private const float MinimumAngularVectorLengthSquared = 1e-6f;
    private const float TwistSignEpsilon = 1e-6f;

    private readonly Gw2ModelSkeleton _skeleton;
    private readonly SimulatedConstraint[] _constraints;
    private float _leftoverSeconds;

    public Gw2BoneConstraintSimulator(Gw2ModelSkeleton skeleton)
    {
        _skeleton = skeleton ?? throw new ArgumentNullException(nameof(skeleton));
        var boneByToken = new Dictionary<ulong, int>();
        for (var index = 0; index < skeleton.Bones.Count; index++)
            boneByToken.TryAdd(Token64.EncodeAfterColon(skeleton.Bones[index].Name), index);
        _constraints = skeleton.BoneConstraints
            .Select(constraint => boneByToken.TryGetValue(constraint.Token, out var bone) ? new SimulatedConstraint(constraint, bone, ResolveLinks(constraint, boneByToken)) : null)
            .OfType<SimulatedConstraint>()
            .ToArray();
    }

    private ResolvedLink[] ResolveLinks(Gw2ModelBoneConstraint constraint, Dictionary<ulong, int> boneByToken)
    {
        var links = new List<ResolvedLink>();
        foreach (var link in constraint.Links)
        {
            if (!boneByToken.TryGetValue(link.Token, out var bone)) continue;
            var parent = _skeleton.Bones[bone].ParentIndex;
            var parentInverseBind = parent == RootParentIndex || parent >= _skeleton.Bones.Count ? Matrix4x4.Identity : _skeleton.Bones[(int)parent].InverseWorld;
            links.Add(new ResolvedLink(bone, LinkDirection(link, parentInverseBind), link.Distance.X, link.Distance.Y));
        }
        return links.ToArray();
    }

    public Vector3 Wind { get; set; }

    public const float DefaultModelSpeed = 1f;

    public float ModelSpeed { get; set; } = DefaultModelSpeed;

    public Func<Vector3, Vector3, Vector3?>? Collide { get; set; }

    public Matrix4x4 ModelWorld { get; set; } = Matrix4x4.Identity;

    private Matrix4x4 _previousModelWorld = Matrix4x4.Identity;

    public IReadOnlyList<int> ConstrainedBones => _constraints.Select(constraint => constraint.Bone).ToArray();

    public void Step(Gw2LocalPose pose, float elapsedSeconds)
    {
        var seconds = elapsedSeconds + _leftoverSeconds;
        if (seconds < MinimumStep)
        {
            _leftoverSeconds = seconds;
            seconds = MinimumStep;
        }
        else
        {
            seconds = MathF.Min(seconds, MaximumStep);
            _leftoverSeconds = 0f;
        }
        var integrates = seconds > MinimumStep;
        var modelMotion = Matrix4x4.Invert(ModelWorld, out var modelInverse) ? _previousModelWorld * modelInverse : Matrix4x4.Identity;
        _previousModelWorld = ModelWorld;

        foreach (var constraint in _constraints)
        {
            if (constraint.Initialized) constraint.StayInWorld(modelMotion, MotionFollowed(constraint.Settings, AnimBlendOf(constraint.Settings, ModelSpeed)));
            StepConstraint(constraint, pose, seconds, integrates);
        }
    }

    private void StepConstraint(SimulatedConstraint simulated, Gw2LocalPose pose, float seconds, bool integrates)
    {
        var constraint = simulated.Settings;
        var bone = simulated.Bone;
        var flags = constraint.Flags;
        var animBlend = AnimBlendOf(constraint, ModelSpeed);
        var parentWorld = ParentWorld(pose, bone);
        var parentPosition = parentWorld.Translation;
        if (!Matrix4x4.Invert(parentWorld, out var parentInverse)) return;

        var animatedPosition = (flags & ResetsToBindPositionFlag) != 0 ? _skeleton.Bones[bone].Position : pose.Positions[bone];
        if (!simulated.Initialized) simulated.Initialize(_skeleton);

        var parentMove = parentPosition - simulated.PreviousParentPosition;
        simulated.PreviousParentPosition = parentPosition;
        if (parentMove.LengthSquared() > TeleportDistanceSquared)
        {
            simulated.WorldPosition += parentMove;
            simulated.AngularOrigin += parentMove;
        }

        var startPosition = simulated.WorldPosition;
        var predicted = startPosition;
        if (integrates)
        {
            var external = simulated.DistanceVelocity + new Vector3(0f, 0f, constraint.Gravity * GravityScale) + Wind * (WindScale * constraint.Wind);
            var turn = Quaternion.CreateFromAxisAngle(simulated.AngularAxis, simulated.AngularSpeed * seconds);
            predicted = Vector3.Transform(startPosition - simulated.AngularOrigin, turn) + simulated.AngularOrigin + external * seconds;
        }

        var constrainedPosition = Vector3.Transform(predicted, parentInverse);
        var constrainedLength = constrainedPosition.Length();
        if (constrainedLength < MinimumStep) return;
        var constrainedNormal = constrainedPosition / constrainedLength;

        var animatedDistance = animatedPosition.Length();
        if (animatedDistance < MinimumStep) return;
        var animatedNormal = animatedPosition / animatedDistance;

        var tangent = Vector3.TransformNormal(simulated.WorldTangent, parentInverse);
        tangent -= animatedNormal * Vector3.Dot(tangent, animatedNormal);
        if (tangent.LengthSquared() < MinimumStep) return;
        tangent = Vector3.Normalize(tangent);

        var twistRotation = Quaternion.Identity;
        if ((flags & TwistLimitFlag) != 0 && animBlend < 1f)
            (tangent, twistRotation) = LimitTwist(constraint, simulated, animatedNormal, tangent, seconds);

        var constrainedRotation = Quaternion.Identity;
        if ((flags & AngleLimitFlag) != 0 && animBlend < 1f)
        {
            constrainedNormal = LimitAngle(constraint, animatedNormal, constrainedNormal, tangent, simulated.TwistAxis, seconds);
            constrainedRotation = ShortestArc(animatedNormal, constrainedNormal);
        }
        else
        {
            constrainedNormal = animatedNormal;
        }
        if (simulated.Links.Length > 0)
        {
            var linked = constrainedNormal * constrainedLength;
            var correction = LinkCorrection(simulated.Links, pose, linked, parentInverse);
            if (correction != Vector3.Zero)
            {
                linked += correction;
                startPosition += Vector3.TransformNormal(correction, parentWorld);
                constrainedLength = linked.Length();
                if (constrainedLength < MinimumStep) return;
                constrainedNormal = linked / constrainedLength;
            }
        }
        constrainedPosition = (flags & DistanceLimitFlag) != 0 && animBlend < 1f
            ? constrainedNormal * LimitDistance(constraint, animatedDistance, constrainedLength, seconds)
            : constrainedNormal * animatedDistance;

        if ((flags & TwistLimitFlag) != 0) constrainedRotation = Quaternion.Concatenate(twistRotation, constrainedRotation);

        var parentBone = _skeleton.Bones[bone].ParentIndex;
        if ((flags & RedirectsBoneFlag) != 0 && parentBone != RootParentIndex && parentBone < pose.Positions.Length)
            WriteBackToParent(pose, bone, (int)parentBone, animBlend, constrainedRotation, _skeleton.Bones[bone].Position.Length());
        else
            WriteBack(pose, bone, animBlend, constrainedRotation, constrainedPosition, animatedPosition);

        var endPosition = Vector3.Transform(constrainedPosition, parentWorld);
        if ((flags & CollidesFlag) != 0 && Collide is not null)
            endPosition = Collide(CollisionStart(startPosition, endPosition, animatedDistance), endPosition) ?? endPosition;
        simulated.WorldPosition = endPosition;
        simulated.WorldTangent = Vector3.TransformNormal(tangent, parentWorld);
        if (!integrates) return;

        simulated.AngularOrigin = parentPosition;
        var newOffset = endPosition - simulated.AngularOrigin;
        var oldOffset = startPosition - simulated.AngularOrigin;
        simulated.DistanceVelocity = (flags & DistanceLimitFlag) != 0 ? DistanceVelocity(newOffset, endPosition - startPosition, seconds) : Vector3.Zero;
        simulated.AngularAxis = Vector3.UnitX;
        simulated.AngularSpeed = 0f;
        if ((flags & AngleLimitFlag) != 0
            && newOffset.LengthSquared() >= MinimumAngularVectorLengthSquared
            && oldOffset.LengthSquared() >= MinimumAngularVectorLengthSquared)
        {
            var (axis, angle) = RotationBetween(oldOffset, newOffset);
            simulated.AngularAxis = axis;
            simulated.AngularSpeed = angle / seconds;
        }

        if (constraint.Drag > MinimumStep)
        {
            var retained = 1f - constraint.Drag * seconds;
            var velocity = simulated.DistanceVelocity * retained;
            simulated.DistanceVelocity = Vector3.Dot(simulated.DistanceVelocity, velocity) < 0f ? Vector3.Zero : velocity;
            var speed = simulated.AngularSpeed * retained;
            if (speed <= 0f == simulated.AngularSpeed <= 0f) simulated.AngularSpeed = speed;
        }
        simulated.DistanceVelocity = Vector3.Clamp(simulated.DistanceVelocity, new Vector3(-VelocityLimit), new Vector3(VelocityLimit));
    }

    private static (Vector3 Tangent, Quaternion Twist) LimitTwist(Gw2ModelBoneConstraint constraint, SimulatedConstraint simulated, Vector3 animatedNormal, Vector3 tangent, float seconds)
    {
        var (axis, angle) = RotationBetween(simulated.TwistAxis, tangent);
        var side = Vector3.Dot(Vector3.Cross(simulated.TwistAxis, tangent), animatedNormal);
        var flipped = false;
        if (angle > MathF.PI / 2f)
        {
            var previous = simulated.TwistSide;
            flipped = previous > TwistSignEpsilon ? side < TwistSignEpsilon : previous < TwistSignEpsilon && side > TwistSignEpsilon;
        }
        if (flipped)
        {
            axis = -axis;
            angle = 2f * MathF.PI - angle;
        }
        else
        {
            simulated.TwistSide = side;
        }

        var correction = TwistCorrection(constraint, angle, seconds);
        if (correction != 0f) tangent = Vector3.Transform(tangent, Quaternion.CreateFromAxisAngle(axis, -correction));
        return (tangent, Quaternion.CreateFromAxisAngle(axis, angle));
    }

    public static Vector3 ProjectOntoPlane(Gw2ModelBoneConstraint constraint, Vector3 animatedNormal, Vector3 constrainedNormal, Vector3 twistAxis)
    {
        var planeNormal = Vector3.Transform(twistAxis, Quaternion.CreateFromAxisAngle(animatedNormal, -(constraint.TwistOffset + MathF.PI / 2f)));
        var planeNormalSquared = planeNormal.LengthSquared();
        if (planeNormalSquared >= 1e-12f) constrainedNormal -= planeNormal * (Vector3.Dot(planeNormal, constrainedNormal) / planeNormalSquared);
        return constrainedNormal.LengthSquared() >= MinimumStep ? Vector3.Normalize(constrainedNormal) : constrainedNormal;
    }

    public static float TwistCorrection(Gw2ModelBoneConstraint constraint, float angle, float seconds)
    {
        var start = constraint.Twist.X;
        var end = constraint.Twist.Y;
        if (angle <= start) return 0f;
        var correction = 0f;
        var ratio = 1f;
        if (start != end) ratio = (angle - start) / (end - start);
        if (start == end || ratio >= 1f)
        {
            correction = angle - end;
            ratio = 1f;
        }
        correction += Gw2BoneConstraintCurve.Shape(constraint.TwistType, ratio) * constraint.TwistStrength * seconds;
        return correction >= angle - start ? angle - start : correction;
    }

    public static Vector3 TwistAxisOf(Vector3 bindPosition)
    {
        var normal = bindPosition.LengthSquared() >= 1e-12f ? Vector3.Normalize(bindPosition) : Vector3.Zero;
        var side = MathF.Abs(normal.Z) > 0.999999f
            ? new Vector3(0f, -normal.Z, normal.Y)
            : new Vector3(normal.Y, -normal.X, 0f);
        return Vector3.Cross(normal, side);
    }

    public static Vector3 CollisionStart(Vector3 startPosition, Vector3 endPosition, float reach)
    {
        var offset = startPosition - endPosition;
        return offset.LengthSquared() > reach * reach ? endPosition + Vector3.Normalize(offset) * reach : startPosition;
    }

    public static float AnimBlendOf(Gw2ModelBoneConstraint constraint, float modelSpeed) =>
        (constraint.Flags & IgnoresModelSpeedFlag) == 0 && modelSpeed > 1f ? modelSpeed - 1f : constraint.AnimBlend;

    public static float MotionFollowed(Gw2ModelBoneConstraint constraint, float animBlend)
    {
        if ((constraint.Flags & FollowsModelMotionFlag) != 0) return 1f;
        if ((constraint.Flags & FollowsModelMotionByBlendFlag) == 0) return 0f;
        return Math.Clamp(1f - animBlend, 0f, 1f);
    }

    public static Vector3 LimitAngle(Gw2ModelBoneConstraint constraint, Vector3 animatedNormal, Vector3 constrainedNormal, Vector3 tangent, Vector3 twistAxis, float seconds)
    {
        var inner = constraint.Angle.X;
        var outer = constraint.Angle.Y;
        if ((constraint.Flags & EllipticalAngleLimitFlag) != 0)
        {
            var sideways = constrainedNormal - animatedNormal * Vector3.Dot(constrainedNormal, animatedNormal);
            var (_, around) = RotationBetween(sideways, tangent);
            var ellipse = MathF.Abs(MathF.Sin(around + constraint.TwistOffset)) * (constraint.EllipseRatio - 1f) + 1f;
            inner *= ellipse;
            outer *= ellipse;
        }
        else if ((constraint.Flags & PlanarAngleLimitFlag) != 0)
        {
            constrainedNormal = ProjectOntoPlane(constraint, animatedNormal, constrainedNormal, twistAxis);
        }
        if (inner >= MathF.PI) return constrainedNormal;
        var (axis, angle) = RotationBetween(constrainedNormal, animatedNormal);
        if (angle <= inner) return constrainedNormal;

        var ratio = inner == outer ? 1f : (angle - inner) / (outer - inner);
        var correction = Gw2BoneConstraintCurve.Shape(constraint.AngleType, ratio) * constraint.AngleStrength * seconds;
        if (angle - correction > outer) correction = angle - outer;
        var turn = MathF.Min(correction, angle);
        return turn == 0f ? constrainedNormal : Vector3.Transform(constrainedNormal, Quaternion.CreateFromAxisAngle(axis, turn));
    }

    private Vector3 LinkCorrection(ResolvedLink[] links, Gw2LocalPose pose, Vector3 constrainedPosition, Matrix4x4 parentInverse)
    {
        var total = Vector3.Zero;
        var corrections = new List<Vector3>(links.Length);
        foreach (var link in links)
        {
            var linkWorld = ParentWorld(pose, link.Bone);
            var direction = Vector3.TransformNormal(Vector3.TransformNormal(link.Direction, linkWorld), parentInverse);
            if (direction.LengthSquared() < 1e-12f) continue;
            direction = Vector3.Normalize(direction);
            var linkPosition = Vector3.Transform(Vector3.Transform(pose.Positions[link.Bone], linkWorld), parentInverse);
            var along = Vector3.Dot(constrainedPosition - linkPosition, direction);
            var limit = along < link.Minimum ? link.Minimum : along > link.Maximum ? link.Maximum : along;
            if (limit == along) continue;
            var correction = direction * (limit - along);
            corrections.Add(correction);
            total += correction;
        }
        if (corrections.Count == 0) return Vector3.Zero;
        return ClampLinkTotal(total, corrections);
    }

    public static Vector3 ClampLinkTotal(Vector3 total, IReadOnlyList<Vector3> corrections)
    {
        var totalSquared = total.LengthSquared();
        var largestSquared = 0f;
        if (totalSquared >= 1e-12f)
            foreach (var correction in corrections)
                largestSquared = MathF.Max(largestSquared, (total * (Vector3.Dot(total, correction) / totalSquared)).LengthSquared());
        if (totalSquared > MinimumStep && totalSquared > largestSquared)
            total *= MathF.Sqrt(largestSquared) / MathF.Sqrt(totalSquared);
        return total;
    }

    public static Vector3 LinkDirection(Gw2ModelBoneConstraintLink link, Matrix4x4 parentInverseBind)
    {
        var angle = link.Angle * MathF.PI / 180f;
        var azimuth = link.Azimuth * MathF.PI / 180f;
        var direction = new Vector3(MathF.Sin(azimuth) * MathF.Cos(angle), -MathF.Cos(azimuth) * MathF.Cos(angle), -MathF.Sin(angle));
        return Vector3.TransformNormal(direction, parentInverseBind);
    }

    public static float LimitDistance(Gw2ModelBoneConstraint constraint, float animatedDistance, float constrainedLength, float seconds)
    {
        var difference = animatedDistance - constrainedLength;
        var compressed = difference > 0f;
        var range = compressed ? constraint.DistanceInner : constraint.DistanceOuter;
        var strength = compressed ? constraint.DistanceInnerStrength : constraint.DistanceOuterStrength;
        var curve = compressed ? constraint.DistanceInnerType : constraint.DistanceOuterType;
        var start = range.X;
        var limit = range.Y;
        if (limit < MinimumStep) return animatedDistance;

        var stretch = MathF.Abs(difference);
        if (stretch <= start) return constrainedLength;
        var ratio = (stretch - start) / (limit - start);
        var move = MathF.Min(Gw2BoneConstraintCurve.Shape(curve, ratio) * strength * seconds, stretch);
        if (stretch - move > limit) move = stretch - limit;
        return constrainedLength + MathF.CopySign(move, difference);
    }

    private static void WriteBack(Gw2LocalPose pose, int bone, float animBlend, Quaternion constrainedRotation, Vector3 constrainedPosition, Vector3 animatedPosition)
    {
        var simulatedOrientation = Quaternion.Concatenate(pose.Orientations[bone], constrainedRotation);
        if (animBlend <= MinimumStep)
        {
            pose.Orientations[bone] = simulatedOrientation;
            pose.Positions[bone] = constrainedPosition;
            return;
        }

        pose.Orientations[bone] = Gw2BoneConstraintCurve.ShortestSlerp(simulatedOrientation, pose.Orientations[bone], animBlend);
        var simulatedLength = constrainedPosition.Length();
        var length = (animatedPosition.Length() - simulatedLength) * animBlend + simulatedLength;
        var (axis, angle) = RotationBetween(constrainedPosition, animatedPosition);
        var direction = angle == 0f ? constrainedPosition : Vector3.Transform(constrainedPosition, Quaternion.CreateFromAxisAngle(axis, angle * animBlend));
        pose.Positions[bone] = direction.LengthSquared() > 0f ? Vector3.Normalize(direction) * length : direction;
    }

    private static void WriteBackToParent(Gw2LocalPose pose, int bone, int parent, float animBlend, Quaternion constrainedRotation, float bindLength)
    {
        var animated = pose.Positions[bone];
        var bindOffset = animated.LengthSquared() >= 1e-12f ? Vector3.Normalize(animated) * bindLength : Vector3.Zero;
        var simulatedOrientation = Quaternion.Concatenate(constrainedRotation, pose.Orientations[parent]);
        if (animBlend <= MinimumStep)
        {
            pose.Positions[bone] = bindOffset;
            pose.Orientations[parent] = simulatedOrientation;
            return;
        }
        pose.Positions[bone] = Vector3.Lerp(bindOffset, animated, animBlend);
        pose.Orientations[parent] = Gw2BoneConstraintCurve.ShortestSlerp(simulatedOrientation, pose.Orientations[parent], animBlend);
    }

    private static Vector3 DistanceVelocity(Vector3 offset, Vector3 movement, float seconds)
    {
        var lengthSquared = offset.LengthSquared();
        if (lengthSquared < 1e-12f) return Vector3.Zero;
        return offset * (Vector3.Dot(offset, movement) / lengthSquared) / seconds;
    }

    private static (Vector3 Axis, float Angle) RotationBetween(Vector3 from, Vector3 to)
    {
        if (from.LengthSquared() < 1e-12f || to.LengthSquared() < 1e-12f) return (Vector3.UnitX, 0f);
        from = Vector3.Normalize(from);
        to = Vector3.Normalize(to);
        var cross = Vector3.Cross(from, to);
        var sine = MathF.Min(1f, cross.Length());
        if (sine < 1e-6f) return (Vector3.UnitX, 0f);
        var angle = MathF.Asin(sine);
        if (Vector3.Dot(from, to) < 0f) angle = MathF.PI - angle;
        return (cross / sine, angle);
    }

    private static Quaternion ShortestArc(Vector3 from, Vector3 to)
    {
        var (axis, angle) = RotationBetween(from, to);
        return angle == 0f ? Quaternion.Identity : Quaternion.CreateFromAxisAngle(axis, angle);
    }

    private Matrix4x4 ParentWorld(Gw2LocalPose pose, int bone)
    {
        var world = Matrix4x4.Identity;
        var parent = _skeleton.Bones[bone].ParentIndex;
        for (var depth = 0; parent != RootParentIndex && parent < pose.Positions.Length && depth <= pose.Positions.Length; depth++)
        {
            world *= pose.LocalMatrix((int)parent);
            parent = _skeleton.Bones[(int)parent].ParentIndex;
        }
        return world;
    }

    private readonly record struct ResolvedLink(int Bone, Vector3 Direction, float Minimum, float Maximum);

    private sealed class SimulatedConstraint(Gw2ModelBoneConstraint settings, int bone, ResolvedLink[] links)
    {
        public ResolvedLink[] Links { get; } = links;
        public Gw2ModelBoneConstraint Settings { get; } = settings;
        public int Bone { get; } = bone;
        public bool Initialized { get; private set; }
        public Vector3 TwistAxis { get; private set; }
        public float TwistSide { get; set; }
        public Vector3 WorldPosition { get; set; }
        public Vector3 WorldTangent { get; set; }
        public Vector3 AngularOrigin { get; set; }
        public Vector3 PreviousParentPosition { get; set; }
        public Vector3 DistanceVelocity { get; set; }
        public Vector3 AngularAxis { get; set; } = Vector3.UnitX;
        public float AngularSpeed { get; set; }

        public void StayInWorld(Matrix4x4 modelMotion, float followed)
        {
            if (followed >= 1f) return;
            WorldPosition = Vector3.Lerp(Vector3.Transform(WorldPosition, modelMotion), WorldPosition, followed);
            AngularOrigin = Vector3.Lerp(Vector3.Transform(AngularOrigin, modelMotion), AngularOrigin, followed);
        }

        public void Initialize(Gw2ModelSkeleton skeleton)
        {
            var bindBone = skeleton.Bones[Bone];
            var bindWorld = BindWorldOf(bindBone);
            var parent = bindBone.ParentIndex;
            var parentBindWorld = parent == RootParentIndex || parent >= skeleton.Bones.Count ? Matrix4x4.Identity : BindWorldOf(skeleton.Bones[(int)parent]);
            TwistAxis = TwistAxisOf(bindBone.Position);
            WorldTangent = Vector3.TransformNormal(TwistAxis, bindWorld);
            WorldPosition = bindWorld.Translation;
            AngularOrigin = bindWorld.Translation;
            PreviousParentPosition = parentBindWorld.Translation;
            TwistSide = 0f;
            Initialized = true;
        }

        private static Matrix4x4 BindWorldOf(Gw2ModelBone bindBone) =>
            Matrix4x4.Invert(bindBone.InverseWorld, out var world) ? world : Matrix4x4.Identity;
    }
}

public static class Gw2BoneConstraintCurve
{
    public static float Shape(byte curve, float ratio) => curve switch
    {
        0 => ratio,
        1 => 1f,
        2 => MathF.Sqrt(ratio),
        3 => ratio * ratio,
        4 => ratio * ratio * (3f - 2f * ratio),
        _ => 1f,
    };

    public static Quaternion ShortestSlerp(Quaternion from, Quaternion to, float amount)
    {
        var dot = Quaternion.Dot(from, to);
        if (dot <= 0f)
        {
            to = Quaternion.Negate(to);
            dot = -dot;
        }
        var fromWeight = 1f - amount;
        var toWeight = amount;
        if (dot < 0.9995f)
        {
            var angle = MathF.Acos(dot);
            var inverseSine = 1f / MathF.Sin(angle);
            fromWeight = MathF.Sin(fromWeight * angle) * inverseSine;
            toWeight = MathF.Sin(amount * angle) * inverseSine;
        }
        return new Quaternion(
            from.X * fromWeight + to.X * toWeight,
            from.Y * fromWeight + to.Y * toWeight,
            from.Z * fromWeight + to.Z * toWeight,
            from.W * fromWeight + to.W * toWeight);
    }
}
