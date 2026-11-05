using System.Numerics;
using Gw2.Dat.Content;
using Gw2.Dat.Archive;

namespace Gw2.Client.Model;

public static class Gw2ModelAnimationBuilder
{
    public const float DefaultFrameRate = 30f;

    public static Gw2ModelAnimations Build(Gw2ModelAnimation? animation, Gw2ModelSkeleton? skeleton, float frameRate = 0f)
    {
        if (animation is null) return new Gw2ModelAnimations();

        var clips = new List<Gw2AnimationClip>(animation.Animations.Count);
        foreach (var data in animation.Animations)
        {
            var clip = BuildClip(data, skeleton, frameRate);
            if (clip is not null) clips.Add(clip);
        }

        return new Gw2ModelAnimations { Clips = clips, Rest = BuildRest(animation) };
    }

    private static Gw2AnimationPose? BuildRest(Gw2ModelAnimation animation)
    {
        foreach (var data in animation.Animations)
        {
            if (!string.Equals(Gw2AnimationRoles.Name(data.Token), "zeropose", StringComparison.Ordinal)) continue;
            var granny = Gw2GrannyAnimation.TryParse(data.GrannyAnimation, data.PointerSize);
            if (granny is null) continue;

            var bones = new Dictionary<string, Gw2AnimationPoseKey>(StringComparer.Ordinal);
            foreach (var track in granny.TrackGroups.SelectMany(group => group.TransformTracks))
                bones[track.Name] = new Gw2AnimationPoseKey(Position(track, 0f), Orientation(track, 0f), Scale(track, 0f));

            return new Gw2AnimationPose { Bones = bones };
        }

        return null;
    }

    public static Gw2AnimationClip? BuildClip(Gw2ModelAnimationData data, Gw2ModelSkeleton? skeleton, float frameRate = 0f)
    {
        var granny = Gw2GrannyAnimation.TryParse(data.GrannyAnimation, data.PointerSize);
        if (granny is null) return null;

        var frameCount = FrameCount(granny, frameRate);
        var duration = granny.Duration > 0f ? granny.Duration : 0f;
        var tracks = new List<Gw2AnimationTrack>();

        foreach (var group in granny.TrackGroups)
        {
            foreach (var track in group.TransformTracks)
            {
                var keys = new Gw2AnimationKey[frameCount + 1];
                for (var frame = 0; frame <= frameCount; frame++)
                {
                    var time = duration * frame / frameCount;
                    var position = Position(track, time);
                    var orientation = Orientation(track, time);
                    var scale = Scale(track, time);
                    if (!IsFinite(position) || !IsFinite(orientation) || !IsFinite(scale))
                    {
                        position = Vector3.Zero;
                        orientation = Quaternion.Identity;
                        scale = Vector3.One;
                    }

                    keys[frame] = new Gw2AnimationKey(time, position, orientation, scale);
                }

                tracks.Add(new Gw2AnimationTrack(ResolveBoneIndex(skeleton, track.Name), track.Name, keys));
            }
        }

        return new Gw2AnimationClip
        {
            Token = data.Token,
            Name = Gw2AnimationRoles.ClipName(data.Token),
            Duration = duration,
            MoveSpeed = data.MoveSpeed,
            IsAdditive = data.IsAdditive,
            Tracks = tracks,
        };
    }

    private static int FrameCount(Gw2GrannyAnimation granny, float frameRate)
    {
        var rate = frameRate > 0f ? frameRate : (granny.TimeStep > 0f ? 1f / granny.TimeStep : DefaultFrameRate);
        return Math.Max(1, (int)MathF.Ceiling(granny.Duration * rate));
    }

    private static int ResolveBoneIndex(Gw2ModelSkeleton? skeleton, string name)
    {
        if (skeleton is null) return -1;
        for (var i = 0; i < skeleton.Bones.Count; i++)
            if (string.Equals(skeleton.Bones[i].Name, name, StringComparison.Ordinal))
                return i;
        return -1;
    }

    private static Vector3 Position(Gw2GrannyTransformTrack track, float time)
    {
        var value = track.Position.Sample(time);
        return value.Length >= 3 ? new Vector3(value[0], value[1], value[2]) : Vector3.Zero;
    }

    private static Quaternion Orientation(Gw2GrannyTransformTrack track, float time)
    {
        var value = track.Orientation.Sample(time);
        if (value.Length < 4) return Quaternion.Identity;
        var quaternion = new Quaternion(value[0], value[1], value[2], value[3]);
        var lengthSquared = quaternion.LengthSquared();
        return lengthSquared > 1e-12f ? Quaternion.Normalize(quaternion) : Quaternion.Identity;
    }

    private static Vector3 Scale(Gw2GrannyTransformTrack track, float time)
    {
        var value = track.ScaleShear.Sample(time);
        if (value.Length >= 9) return new Vector3(value[0], value[4], value[8]);
        return value.Length >= 3 ? new Vector3(value[0], value[1], value[2]) : Vector3.One;
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static bool IsFinite(Vector3 value) => IsFinite(value.X) && IsFinite(value.Y) && IsFinite(value.Z);

    private static bool IsFinite(Quaternion value) =>
        IsFinite(value.X) && IsFinite(value.Y) && IsFinite(value.Z) && IsFinite(value.W);
}
