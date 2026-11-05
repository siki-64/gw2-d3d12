using System.Numerics;
using Gw2.Dat.Content;

namespace Gw2.Client.Model;

public readonly record struct Gw2LegChainBones(int End, int Middle, int Root);

public static class Gw2TwoBoneLegSolver
{
    private const float DegenerateLengthSquared = 1e-8f;

    public static bool Solve(Gw2ModelSkeleton skeleton, Gw2LocalPose pose, Gw2LegChainBones bones, Vector3 target, float hyperExtensionStart, float hyperExtensionScale)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        ArgumentNullException.ThrowIfNull(pose);
        var end = Gw2SkeletonPose.WorldMatrixOf(skeleton, pose, bones.End).Translation;
        var middle = Gw2SkeletonPose.WorldMatrixOf(skeleton, pose, bones.Middle).Translation;
        var root = Gw2SkeletonPose.WorldMatrixOf(skeleton, pose, bones.Root).Translation;
        var bendNormal = Vector3.Cross(end - middle, root - middle);
        if (bendNormal.LengthSquared() <= DegenerateLengthSquared) return false;

        var reach = target - root;
        var reachSquared = reach.LengthSquared();
        if (reachSquared < DegenerateLengthSquared) return false;
        var upperSquared = Vector3.DistanceSquared(middle, root);
        var lowerSquared = Vector3.DistanceSquared(end, middle);
        var along = (upperSquared - lowerSquared) / reachSquared * 0.5f + 0.5f;
        var kneeCentre = root + reach * along;
        var heightSquared = upperSquared - along * along * reachSquared;
        var hyperExtension = upperSquared * hyperExtensionStart;
        if (hyperExtension > heightSquared) heightSquared = MathF.Max(0f, (heightSquared - hyperExtension) * hyperExtensionScale + hyperExtension);
        var outward = Vector3.Cross(reach, bendNormal);
        var knee = kneeCentre + outward * MathF.Sqrt(heightSquared / outward.LengthSquared());

        RotateBoneToward(skeleton, pose, bones.Root, middle, knee);
        RotateBoneToward(skeleton, pose, bones.Middle, Gw2SkeletonPose.WorldMatrixOf(skeleton, pose, bones.End).Translation, target);
        return true;
    }

    public static void RotateBoneBetween(Gw2ModelSkeleton skeleton, Gw2LocalPose pose, int bone, Vector3 fromDirection, Vector3 toDirection)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        ArgumentNullException.ThrowIfNull(pose);
        if (HalfAngleRotation(fromDirection, toDirection) is { } delta) RotateBone(skeleton, pose, bone, delta);
    }

    private static void RotateBoneToward(Gw2ModelSkeleton skeleton, Gw2LocalPose pose, int bone, Vector3 from, Vector3 to)
    {
        var origin = Gw2SkeletonPose.WorldMatrixOf(skeleton, pose, bone).Translation;
        if (HalfAngleRotation(from - origin, to - origin) is { } delta) RotateBone(skeleton, pose, bone, delta);
    }

    private static Quaternion? HalfAngleRotation(Vector3 from, Vector3 to)
    {
        var current = Vector3.Normalize(from);
        var wanted = Vector3.Normalize(to);
        if (!float.IsFinite(current.X) || !float.IsFinite(wanted.X)) return null;
        var halfway = Vector3.Normalize(current + wanted);
        if (!float.IsFinite(halfway.X)) return null;
        return Quaternion.Normalize(new Quaternion(Vector3.Cross(current, halfway), Vector3.Dot(current, halfway)));
    }

    private static void RotateBone(Gw2ModelSkeleton skeleton, Gw2LocalPose pose, int bone, Quaternion worldDelta)
    {
        if (!Matrix4x4.Decompose(Gw2SkeletonPose.WorldMatrixOf(skeleton, pose, bone), out _, out var boneWorld, out _)) return;
        var parentWorld = Quaternion.Identity;
        var parent = skeleton.Bones[bone].ParentIndex;
        if (parent < pose.Positions.Length && !Matrix4x4.Decompose(Gw2SkeletonPose.WorldMatrixOf(skeleton, pose, (int)parent), out _, out parentWorld, out _)) return;
        var rotatedWorld = Quaternion.Concatenate(boneWorld, worldDelta);
        pose.Orientations[bone] = Quaternion.Normalize(Quaternion.Concatenate(rotatedWorld, Quaternion.Inverse(parentWorld)));
    }
}
