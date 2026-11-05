using Gw2.Formats;
namespace Gw2.Dat.Content;

public sealed record Gw2ModelSnapPoint(ulong Bone, ulong Shape, uint Flags);

public sealed record Gw2ModelExpansionEmitter(float Curl, float VortexSize, uint CurlQuality, uint CurlFlags, float FieldScale);

public sealed class Gw2ModelExpansion
{
    public Gw2ModelExpansion(IReadOnlyList<Gw2ModelSnapPoint> snapPoints, float snapPointPriority, IReadOnlyList<Gw2ModelExpansionEmitter> emitters)
    {
        SnapPoints = snapPoints;
        SnapPointPriority = snapPointPriority;
        Emitters = emitters;
    }

    public IReadOnlyList<Gw2ModelSnapPoint> SnapPoints { get; }
    public float SnapPointPriority { get; }
    public IReadOnlyList<Gw2ModelExpansionEmitter> Emitters { get; }

    public static Gw2ModelExpansion? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.FindRoot("ModelFileExpansion");
        if (root is null) return null;

        var snapPoints = new List<Gw2ModelSnapPoint>();
        if (root.Fields.GetValueOrDefault("snapPoints") is object?[] snapObjects)
            foreach (var snapObject in snapObjects)
                if (snapObject is PackObject snap)
                    snapPoints.Add(new(
                        PackValue.AsU64(snap.Fields.GetValueOrDefault("bone")),
                        PackValue.AsU64(snap.Fields.GetValueOrDefault("shape")),
                        PackValue.AsU32(snap.Fields.GetValueOrDefault("flags"))));

        var emitters = new List<Gw2ModelExpansionEmitter>();
        if (root.Fields.GetValueOrDefault("emitters") is object?[] emitterObjects)
            foreach (var emitterObject in emitterObjects)
                if (emitterObject is PackObject emitter)
                    emitters.Add(new(
                        PackValue.AsF32(emitter.Fields.GetValueOrDefault("curl")),
                        PackValue.AsF32(emitter.Fields.GetValueOrDefault("vortexSize")),
                        PackValue.AsU32(emitter.Fields.GetValueOrDefault("curlQuality")),
                        PackValue.AsU32(emitter.Fields.GetValueOrDefault("curlFlags")),
                        PackValue.AsF32(emitter.Fields.GetValueOrDefault("fieldScale"))));

        return new(snapPoints, PackValue.AsF32(root.Fields.GetValueOrDefault("snapPointPriority")), emitters);
    }
}
