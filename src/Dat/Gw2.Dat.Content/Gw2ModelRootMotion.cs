using Gw2.Formats;
using System.Numerics;

namespace Gw2.Dat.Content;

public sealed record Gw2RootMotion(
    ulong Sequence,
    IReadOnlyList<float> Keys,
    IReadOnlyList<Vector3> Positions,
    IReadOnlyList<Vector4> Quaternions);

public sealed class Gw2ModelRootMotion
{
    public Gw2ModelRootMotion(IReadOnlyList<Gw2RootMotion> rootMotions)
    {
        RootMotions = rootMotions;
    }

    public IReadOnlyList<Gw2RootMotion> RootMotions { get; }

    public static Gw2ModelRootMotion? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.FindRoot("ModelFileRootMotion");
        if (root is null) return null;

        var motions = new List<Gw2RootMotion>();
        if (root.Fields.GetValueOrDefault("rootMotions") is object?[] motionObjects)
            foreach (var motionObject in motionObjects)
                if (motionObject is PackObject motion)
                    motions.Add(new(
                        PackValue.AsU64(motion.Fields.GetValueOrDefault("sequence")),
                        PackValue.AsFloats(motion.Fields.GetValueOrDefault("keys")),
                        PackValue.AsVec3List(motion.Fields.GetValueOrDefault("posValues")),
                        Vec4s(motion.Fields.GetValueOrDefault("quatValues"))));

        return new(motions);
    }

    private static IReadOnlyList<Vector4> Vec4s(object? value)
    {
        if (value is not object?[] values) return [];
        var result = new List<Vector4>(values.Length);
        foreach (var item in values)
            if (item is float[] floats && floats.Length >= 4)
                result.Add(new Vector4(floats[0], floats[1], floats[2], floats[3]));
        return result;
    }
}
