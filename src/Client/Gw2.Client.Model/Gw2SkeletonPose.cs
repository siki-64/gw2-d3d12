using System.Numerics;
using Gw2.Dat.Content;
using Gw2.Dat.Archive;

namespace Gw2.Client.Model;

public readonly record struct Gw2WeightedClipSample(Gw2AnimationClip Clip, float Seconds, float Weight, IReadOnlyList<float>? TrackMask = null)
{
    public float WeightOf(int bone) => TrackMask is { } mask && bone < mask.Count ? Weight * mask[bone] : Weight;
}

public readonly record struct Gw2LocalPose(Vector3[] Positions, Quaternion[] Orientations, Vector3[] Scales)
{
    public Matrix4x4 LocalMatrix(int bone) =>
        Matrix4x4.CreateScale(Scales[bone]) * Matrix4x4.CreateFromQuaternion(Orientations[bone]) * Matrix4x4.CreateTranslation(Positions[bone]);
}

public static class Gw2SkeletonPose
{
    private const uint RootParentIndex = 0xFFFFFFFF;

    public static Matrix4x4[] WorldMatricesAt(Gw2ModelSkeleton skeleton, Gw2AnimationClip? clip, float seconds)
    {
        var pose = LocalPoseAt(skeleton, clip, seconds);
        return WorldMatricesOf(skeleton, pose);
    }

    public static Gw2LocalPose LocalPoseAt(Gw2ModelSkeleton skeleton, Gw2AnimationClip? clip, float seconds)
    {
        if (skeleton is null) throw new ArgumentNullException(nameof(skeleton));
        var count = skeleton.Bones.Count;
        var pose = new Gw2LocalPose(new Vector3[count], new Quaternion[count], new Vector3[count]);
        for (var index = 0; index < count; index++)
        {
            var bone = skeleton.Bones[index];
            pose.Positions[index] = bone.Position;
            pose.Orientations[index] = bone.Orientation;
            pose.Scales[index] = Vector3.One;
        }

        if (clip is not null)
            foreach (var track in clip.Tracks)
                if (track.BoneIndex >= 0 && track.BoneIndex < count && track.Keys.Count > 0)
                {
                    var key = KeyAt(track, seconds);
                    pose.Positions[track.BoneIndex] = key.Position;
                    pose.Orientations[track.BoneIndex] = key.Orientation;
                    pose.Scales[track.BoneIndex] = key.Scale;
                }
        return pose;
    }

    public static Matrix4x4[] WorldMatricesOf(Gw2ModelSkeleton skeleton, Gw2LocalPose pose)
    {
        if (skeleton is null) throw new ArgumentNullException(nameof(skeleton));
        var locals = new Matrix4x4[pose.Positions.Length];
        for (var index = 0; index < locals.Length; index++) locals[index] = pose.LocalMatrix(index);
        var worlds = new Matrix4x4[locals.Length];
        var resolved = new bool[locals.Length];
        for (var index = 0; index < worlds.Length; index++) Resolve(skeleton, locals, worlds, resolved, index, 0);
        return worlds;
    }

    public static Matrix4x4 WorldMatrixOf(Gw2ModelSkeleton skeleton, Gw2LocalPose pose, int bone)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        var world = pose.LocalMatrix(bone);
        var parent = skeleton.Bones[bone].ParentIndex;
        for (var depth = 0; parent != RootParentIndex && parent < pose.Positions.Length && depth < pose.Positions.Length; depth++)
        {
            world *= pose.LocalMatrix((int)parent);
            parent = skeleton.Bones[(int)parent].ParentIndex;
        }
        return world;
    }

    public static Matrix4x4[] WorldMatricesBlended(Gw2ModelSkeleton skeleton, IReadOnlyList<Gw2WeightedClipSample> samples) =>
        WorldMatricesOf(skeleton, LocalPoseBlended(skeleton, samples));

    public static Gw2LocalPose LocalPoseBlended(Gw2ModelSkeleton skeleton, IReadOnlyList<Gw2WeightedClipSample> samples)
    {
        if (skeleton is null) throw new ArgumentNullException(nameof(skeleton));
        if (samples is null) throw new ArgumentNullException(nameof(samples));
        var count = skeleton.Bones.Count;
        var positions = new Vector3[count];
        var scales = new Vector3[count];
        var orientations = new Quaternion[count];
        var weights = new float[count];
        foreach (var sample in samples)
        {
            if (sample.Weight <= 0f || sample.Clip.IsAdditive) continue;
            foreach (var track in sample.Clip.Tracks)
            {
                if (track.BoneIndex < 0 || track.BoneIndex >= count || track.Keys.Count == 0) continue;
                var key = KeyAt(track, sample.Seconds);
                var bone = track.BoneIndex;
                var boneWeight = sample.WeightOf(bone);
                if (boneWeight <= 0f) continue;
                var orientation = weights[bone] > 0f && Quaternion.Dot(orientations[bone], key.Orientation) < 0f ? Quaternion.Negate(key.Orientation) : key.Orientation;
                positions[bone] += key.Position * boneWeight;
                scales[bone] += key.Scale * boneWeight;
                orientations[bone] += orientation * boneWeight;
                weights[bone] += boneWeight;
            }
        }

        for (var index = 0; index < count; index++)
        {
            if (weights[index] > 0f)
            {
                positions[index] /= weights[index];
                scales[index] /= weights[index];
                orientations[index] = Quaternion.Normalize(orientations[index]);
                continue;
            }
            var bone = skeleton.Bones[index];
            positions[index] = bone.Position;
            scales[index] = Vector3.One;
            orientations[index] = bone.Orientation;
        }

        foreach (var sample in samples)
        {
            if (sample.Weight <= 0f || !sample.Clip.IsAdditive) continue;
            foreach (var track in sample.Clip.Tracks)
            {
                if (track.BoneIndex < 0 || track.BoneIndex >= count || track.Keys.Count == 0) continue;
                var key = KeyAt(track, sample.Seconds);
                var bone = track.BoneIndex;
                var weight = MathF.Min(sample.WeightOf(bone), 1f);
                if (weight <= 0f) continue;
                var deltaOrientation = key.Orientation.W < 0f ? Quaternion.Negate(key.Orientation) : key.Orientation;
                deltaOrientation = Quaternion.Normalize(Quaternion.Lerp(Quaternion.Identity, deltaOrientation, weight));
                var deltaPosition = key.Position * weight;
                positions[bone] += Vector3.Transform(deltaPosition * scales[bone], orientations[bone]);
                orientations[bone] = Quaternion.Normalize(orientations[bone] * deltaOrientation);
                scales[bone] *= Vector3.Lerp(Vector3.One, key.Scale, weight);
            }
        }

        return new Gw2LocalPose(positions, orientations, scales);
    }

    private static Matrix4x4 Resolve(Gw2ModelSkeleton skeleton, Matrix4x4[] locals, Matrix4x4[] worlds, bool[] resolved, int index, int depth)
    {
        if (resolved[index]) return worlds[index];
        var parent = skeleton.Bones[index].ParentIndex;
        worlds[index] = parent == RootParentIndex || parent >= locals.Length || depth > locals.Length
            ? locals[index]
            : locals[index] * Resolve(skeleton, locals, worlds, resolved, (int)parent, depth + 1);
        resolved[index] = true;
        return worlds[index];
    }

    internal static Gw2AnimationKey KeyAt(Gw2AnimationTrack track, float seconds)
    {
        var keys = track.Keys;
        var next = 0;
        var last = keys.Count;
        while (next < last)
        {
            var middle = (next + last) >> 1;
            if (keys[middle].Time < seconds) next = middle + 1;
            else last = middle;
        }
        if (next == 0) return keys[0];
        if (next >= keys.Count) return keys[^1];
        var previous = keys[next - 1];
        var following = keys[next];
        var span = following.Time - previous.Time;
        var blend = span <= 0f ? 1f : (seconds - previous.Time) / span;
        return new Gw2AnimationKey(seconds, Vector3.Lerp(previous.Position, following.Position, blend), Quaternion.Slerp(previous.Orientation, following.Orientation, blend), Vector3.Lerp(previous.Scale, following.Scale, blend));
    }
}
