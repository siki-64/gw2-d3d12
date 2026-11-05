using System;
using System.Numerics;

namespace Gw2.Dat.Content;

public static class Gw2ModelUvAnimationSampler
{
    public const int ChannelCount = 5;
    public const int RotationChannel = 0;
    public const int ScaleUChannel = 1;
    public const int ScaleVChannel = 2;
    public const int OffsetUChannel = 3;
    public const int OffsetVChannel = 4;
    public const int TextureInputCount = 4;
    public static readonly uint[] TexTransformRowTokens = [0x3674487D, 0x3674487E, 0x3F4721AE, 0x3F4721AF, 0x4819FADF, 0x4819FAE0, 0x50ECD410, 0x50ECD411];

    public static (Vector4 RowA, Vector4 RowB) TransformAt(Gw2GrannyAnimation? granny, Gw2ModelUvAnimation uvAnimation, float seconds)
    {
        ArgumentNullException.ThrowIfNull(uvAnimation);
        Span<float> channels = [0f, 1f, 1f, 0f, 0f];
        foreach (var track in uvAnimation.Tracks)
            if (track.Type < ChannelCount) channels[track.Type] = ChannelValueAt(granny, track, seconds);
        return Transform(channels[RotationChannel], new(channels[ScaleUChannel], channels[ScaleVChannel]), new(channels[OffsetUChannel], channels[OffsetVChannel]));
    }

    public static (Vector4 RowA, Vector4 RowB) Transform(float rotation, Vector2 scale, Vector2 offset)
    {
        var cosine = rotation != 0f ? MathF.Cos(rotation) : 1f;
        var sine = rotation != 0f ? MathF.Sin(rotation) : 0f;
        var scaledCosineU = cosine * scale.X;
        var scaledSineU = sine * scale.X;
        var scaledCosineV = cosine * scale.Y;
        var scaledSineV = sine * scale.Y;
        return (
            new(scaledCosineU, -scaledSineV, 0f, offset.X - (scaledCosineU - scaledSineV) * 0.5f + 0.5f),
            new(scaledSineU, scaledCosineV, 0f, 0.5f - ((scaledCosineV + scaledSineU) * 0.5f + offset.Y)));
    }

    private static float ChannelValueAt(Gw2GrannyAnimation? granny, Gw2ModelAnimationTrack track, float seconds)
    {
        if (TrackOf(granny, track) is not { Dimension: > 0 } vectorTrack) return track.InitialValue.X;
        Span<float> value = stackalloc float[Math.Max(vectorTrack.Dimension, 4)];
        return vectorTrack.ValueCurve.TrySample(seconds, value) ? value[0] : track.InitialValue.X;
    }

    private static Gw2GrannyVectorTrack? TrackOf(Gw2GrannyAnimation? granny, Gw2ModelAnimationTrack track)
    {
        if (granny is null || track.TrackGroupIndex >= granny.TrackGroups.Count) return null;
        var group = granny.TrackGroups[(int)track.TrackGroupIndex];
        return track.VectorTrackIndex < group.VectorTracks.Count ? group.VectorTracks[(int)track.VectorTrackIndex] : null;
    }
}
