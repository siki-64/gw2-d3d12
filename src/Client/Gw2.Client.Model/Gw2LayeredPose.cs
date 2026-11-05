using System.Numerics;
using Gw2.Dat.Content;

namespace Gw2.Client.Model;

public sealed record Gw2PoseLayer(IReadOnlyList<Gw2WeightedClipSample> Samples, float AdditiveWeight, IReadOnlyList<float>? TrackMask, bool IsBaseLayer);

public static class Gw2LayeredPose
{
    public static Gw2LocalPose LocalPoseOf(Gw2ModelSkeleton skeleton, IReadOnlyList<Gw2PoseLayer> layers)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        ArgumentNullException.ThrowIfNull(layers);
        var count = skeleton.Bones.Count;
        var pose = new Gw2LocalPose(new Vector3[count], new Quaternion[count], new Vector3[count]);
        for (var bone = 0; bone < count; bone++)
        {
            pose.Positions[bone] = skeleton.Bones[bone].Position;
            pose.Orientations[bone] = skeleton.Bones[bone].Orientation;
            pose.Scales[bone] = Vector3.One;
        }

        var positions = new Vector3[count];
        var scales = new Vector3[count];
        var orientations = new Quaternion[count];
        var weights = new float[count];
        foreach (var layer in layers)
        {
            Accumulate(layer.Samples, additive: false, positions, scales, orientations, weights);
            for (var bone = 0; bone < count; bone++)
            {
                if (weights[bone] <= 0f) continue;
                var blend = layer.IsBaseLayer ? 1f : MaskWeight(layer.TrackMask, bone);
                if (blend <= 0f) continue;
                var position = positions[bone] / weights[bone];
                var scale = scales[bone] / weights[bone];
                var orientation = Quaternion.Normalize(orientations[bone]);
                pose.Positions[bone] = Vector3.Lerp(pose.Positions[bone], position, blend);
                pose.Scales[bone] = Vector3.Lerp(pose.Scales[bone], scale, blend);
                pose.Orientations[bone] = Nlerp(pose.Orientations[bone], orientation, blend);
            }
            ApplyAdditive(layer, pose, positions, scales, orientations, weights);
        }
        return pose;
    }

    private static void ApplyAdditive(Gw2PoseLayer layer, Gw2LocalPose pose, Vector3[] positions, Vector3[] scales, Quaternion[] orientations, float[] weights)
    {
        Accumulate(layer.Samples, additive: true, positions, scales, orientations, weights);
        for (var bone = 0; bone < weights.Length; bone++)
        {
            if (weights[bone] <= 0f) continue;
            var largest = 0f;
            foreach (var sample in layer.Samples)
                if (sample.Clip.IsAdditive && sample.Weight > 0f) largest = MathF.Max(largest, sample.WeightOf(bone));
            var weight = MathF.Min(largest * layer.AdditiveWeight, 1f);
            if (weight <= 0f) continue;
            var deltaOrientation = Quaternion.Normalize(orientations[bone]);
            if (deltaOrientation.W < 0f) deltaOrientation = Quaternion.Negate(deltaOrientation);
            deltaOrientation = Nlerp(Quaternion.Identity, deltaOrientation, weight);
            var deltaPosition = positions[bone] / weights[bone] * weight;
            var deltaScale = Vector3.Lerp(Vector3.One, scales[bone] / weights[bone], weight);
            pose.Positions[bone] += Vector3.Transform(deltaPosition * pose.Scales[bone], pose.Orientations[bone]);
            pose.Orientations[bone] = Quaternion.Normalize(pose.Orientations[bone] * deltaOrientation);
            pose.Scales[bone] *= deltaScale;
        }
    }

    private static void Accumulate(IReadOnlyList<Gw2WeightedClipSample> samples, bool additive, Vector3[] positions, Vector3[] scales, Quaternion[] orientations, float[] weights)
    {
        Array.Clear(positions);
        Array.Clear(scales);
        Array.Clear(orientations);
        Array.Clear(weights);
        foreach (var sample in samples)
        {
            if (sample.Weight <= 0f || sample.Clip.IsAdditive != additive) continue;
            foreach (var track in sample.Clip.Tracks)
            {
                if (track.BoneIndex < 0 || track.BoneIndex >= weights.Length || track.Keys.Count == 0) continue;
                var bone = track.BoneIndex;
                var boneWeight = sample.WeightOf(bone);
                if (boneWeight <= 0f) continue;
                var key = Gw2SkeletonPose.KeyAt(track, sample.Seconds);
                var orientation = weights[bone] > 0f && Quaternion.Dot(orientations[bone], key.Orientation) < 0f ? Quaternion.Negate(key.Orientation) : key.Orientation;
                positions[bone] += key.Position * boneWeight;
                scales[bone] += key.Scale * boneWeight;
                orientations[bone] += orientation * boneWeight;
                weights[bone] += boneWeight;
            }
        }
    }

    private static float MaskWeight(IReadOnlyList<float>? mask, int bone) => mask is not null && bone < mask.Count ? mask[bone] : 1f;

    private static Quaternion Nlerp(Quaternion from, Quaternion to, float amount)
    {
        if (Quaternion.Dot(from, to) < 0f) to = Quaternion.Negate(to);
        return Quaternion.Normalize(Quaternion.Lerp(from, to, amount));
    }
}
