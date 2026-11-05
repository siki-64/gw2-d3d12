using System;
using System.Numerics;

namespace Gw2.Dat.Content;

public static class Gw2ModelMaterialConstantSampler
{
    public static Vector4 ValueAt(Gw2GrannyAnimation? granny, Gw2ModelMaterialConstantAnimation constant, float seconds)
    {
        ArgumentNullException.ThrowIfNull(constant);
        if (TrackOf(granny, constant) is not { } track) return constant.InitialValue;
        Span<float> value = stackalloc float[Math.Max(track.Dimension, 4)];
        value[..4].Clear();
        if (track.Dimension <= 0 || !track.ValueCurve.TrySample(seconds, value)) return constant.InitialValue;
        return new Vector4(value[0], track.Dimension > 1 ? value[1] : constant.InitialValue.Y, track.Dimension > 2 ? value[2] : constant.InitialValue.Z, track.Dimension > 3 ? value[3] : constant.InitialValue.W);
    }

    private static Gw2GrannyVectorTrack? TrackOf(Gw2GrannyAnimation? granny, Gw2ModelMaterialConstantAnimation constant)
    {
        if (granny is null || constant.TrackGroupIndex >= granny.TrackGroups.Count) return null;
        var group = granny.TrackGroups[(int)constant.TrackGroupIndex];
        return constant.VectorTrackIndex < group.VectorTracks.Count ? group.VectorTracks[(int)constant.VectorTrackIndex] : null;
    }
}
