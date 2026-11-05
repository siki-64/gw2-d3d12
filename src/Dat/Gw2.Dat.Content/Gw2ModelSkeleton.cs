using Gw2.Formats;
using System.Numerics;

namespace Gw2.Dat.Content;

public sealed record Gw2ModelBone(
    string Name,
    uint ParentIndex,
    uint TransformFlags,
    Vector3 Position,
    Quaternion Orientation,
    Matrix4x4 ScaleShear,
    Matrix4x4 InverseWorld,
    float LodError)
{
    public byte ExtendedDataType { get; init; }
    public byte ExtendedDataObject { get; init; }
}

public sealed record Gw2ModelBoneConstraintLink(float Angle, float Azimuth, Vector2 Distance, ulong Token);

public sealed record Gw2ModelBoneConstraint(
    ulong Token,
    uint Flags,
    float TwistOffset,
    float AnimBlend,
    float Drag,
    float Gravity,
    float Wind,
    IReadOnlyList<Gw2ModelBoneConstraintLink> Links)
{
    public float EllipseRatio { get; init; }
    public float CollisionRadius { get; init; }
    public Vector2 Angle { get; init; }
    public float AngleStrength { get; init; }
    public byte AngleType { get; init; }
    public Vector2 DistanceInner { get; init; }
    public float DistanceInnerStrength { get; init; }
    public byte DistanceInnerType { get; init; }
    public Vector2 DistanceOuter { get; init; }
    public float DistanceOuterStrength { get; init; }
    public byte DistanceOuterType { get; init; }
    public Vector2 Twist { get; init; }
    public float TwistStrength { get; init; }
    public byte TwistType { get; init; }
}

public sealed record Gw2ModelTrackMask(ulong Token, byte[] Data)
{
    private const int DefaultWeightOffset = 0;
    private const int BoneWeightCountOffset = 4;
    private const int BoneWeightsOffsetOffset = 8;

    public float DefaultWeight => Data.Length >= BoneWeightCountOffset ? BitConverter.ToSingle(Data, DefaultWeightOffset) : 1f;

    public IReadOnlyList<float> BoneWeights
    {
        get
        {
            if (Data.Length < BoneWeightsOffsetOffset + 4) return Array.Empty<float>();
            var count = BitConverter.ToInt32(Data, BoneWeightCountOffset);
            var start = BitConverter.ToInt32(Data, BoneWeightsOffsetOffset);
            if (count <= 0 || start < 0 || (long)start + 4L * count > Data.Length) return Array.Empty<float>();
            var weights = new float[count];
            for (var index = 0; index < count; index++) weights[index] = BitConverter.ToSingle(Data, start + 4 * index);
            return weights;
        }
    }

    public float WeightOf(int bone)
    {
        var weights = BoneWeights;
        return bone >= 0 && bone < weights.Count ? weights[bone] : DefaultWeight;
    }
}

public sealed record Gw2ModelSkeletonPlacement(uint Flags, Vector3 Position, Quaternion Orientation, Matrix4x4 ScaleShear);

public sealed class Gw2ModelSkeleton
{
    public Gw2ModelSkeleton(
        PackFileReference fileReference,
        IReadOnlyList<Gw2ModelBone> bones,
        IReadOnlyList<uint> boneFlags,
        IReadOnlyList<Gw2ModelBoneConstraint> boneConstraints,
        IReadOnlyList<uint> emitterBones,
        IReadOnlyList<Gw2ModelTrackMask> trackMasks)
    {
        FileReference = fileReference;
        Bones = bones;
        BoneFlags = boneFlags;
        BoneConstraints = boneConstraints;
        EmitterBones = emitterBones;
        TrackMasks = trackMasks;
    }

    public PackFileReference FileReference { get; }
    public IReadOnlyList<Gw2ModelBone> Bones { get; }
    public IReadOnlyList<uint> BoneFlags { get; }
    public IReadOnlyList<Gw2ModelBoneConstraint> BoneConstraints { get; }
    public IReadOnlyList<uint> EmitterBones { get; }
    public IReadOnlyList<Gw2ModelTrackMask> TrackMasks { get; }
    public string ModelName { get; init; } = string.Empty;
    public string SkeletonName { get; init; } = string.Empty;
    public uint LodType { get; init; }
    public Gw2ModelSkeletonPlacement? InitialPlacement { get; init; }
    public int MeshBindingCount { get; init; }
    public IReadOnlyList<byte> MeshBindingMeshes { get; init; } = [];
    public byte[] MirrorSpec { get; init; } = [];
    public IReadOnlyList<Gw2ModelBoneConstraint> OverrideBoneConstraints { get; init; } = [];

    public int FindBoneIndex(ulong token)
    {
        for (var i = 0; i < Bones.Count; i++)
        {
            var name = Bones[i].Name;
            if (Token64.Encode(name) == token) return i;
            if (name.IndexOf(':') >= 0 && Token64.EncodeAfterColon(name) == token) return i;
        }

        return -1;
    }

    public static Gw2ModelSkeleton? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.FindRoot("ModelFileSkeleton");
        if (root is null) return null;

        var data = root.Fields.GetValueOrDefault("skeletonData") as PackObject;

        var bones = new List<Gw2ModelBone>();
        var granny = data?.Fields.GetValueOrDefault("grannyModel") as PackObject;
        var skeleton = granny?.Fields.GetValueOrDefault("Skeleton") as PackObject;
        if (skeleton?.Fields.GetValueOrDefault("Bones") is object?[] boneObjects)
            foreach (var boneObject in boneObjects)
                if (boneObject is PackObject bone)
                {
                    var transform = bone.Fields.GetValueOrDefault("LocalTransform") as PackObject;
                    bones.Add(new(
                        bone.Fields.GetValueOrDefault("Name") as string ?? string.Empty,
                        PackValue.AsU32(bone.Fields.GetValueOrDefault("ParentIndex")),
                        PackValue.AsU32(transform?.Fields.GetValueOrDefault("Flags")),
                        Vec3(transform, "Position"),
                        Quat(transform, "Orientation"),
                        Matrix(transform?.Fields.GetValueOrDefault("ScaleShear")),
                        Matrix(bone.Fields.GetValueOrDefault("InverseWorld4x4")),
                        F32(bone.Fields.GetValueOrDefault("LODError")))
                    {
                        ExtendedDataType = PackValue.AsU8(bone.Fields.GetValueOrDefault("ExtendedData.Type")),
                        ExtendedDataObject = PackValue.AsU8(bone.Fields.GetValueOrDefault("ExtendedData.Object")),
                    });
                }

        var constraints = ReadConstraints(data);

        var trackMasks = new List<Gw2ModelTrackMask>();
        if (data?.Fields.GetValueOrDefault("trackMasks") is object?[] trackObjects)
            foreach (var trackObject in trackObjects)
                if (trackObject is PackObject track)
                    trackMasks.Add(new(
                        PackValue.AsU64(track.Fields.GetValueOrDefault("token")),
                        PackValue.AsBytes((track.Fields.GetValueOrDefault("data") as PackObject)?.Fields.GetValueOrDefault("trackMask"))));

        return new(
            root.Fields.GetValueOrDefault("fileReference") is PackFileReference reference ? reference : default,
            bones,
            PackValue.AsU32List(data?.Fields.GetValueOrDefault("boneFlags")),
            constraints,
            PackValue.AsU32List(data?.Fields.GetValueOrDefault("emitterBones")),
            trackMasks)
        {
            ModelName = granny?.Fields.GetValueOrDefault("Name") as string ?? string.Empty,
            SkeletonName = skeleton?.Fields.GetValueOrDefault("Name") as string ?? string.Empty,
            LodType = PackValue.AsU32(skeleton?.Fields.GetValueOrDefault("LODType")),
            InitialPlacement = granny?.Fields.GetValueOrDefault("InitialPlacement") is PackObject placement
                ? new Gw2ModelSkeletonPlacement(
                    PackValue.AsU32(placement.Fields.GetValueOrDefault("Flags")),
                    Vec3(placement, "Position"),
                    Quat(placement, "Orientation"),
                    Matrix(placement.Fields.GetValueOrDefault("ScaleShear")))
                : null,
            MeshBindingCount = (granny?.Fields.GetValueOrDefault("MeshBindings") as object?[])?.Length ?? 0,
            MeshBindingMeshes = PackValue.AsObjects(granny?.Fields.GetValueOrDefault("MeshBindings"))
                .Select(binding => PackValue.AsU8(binding.Fields.GetValueOrDefault("Mesh")))
                .ToArray(),
            MirrorSpec = PackValue.AsBytes((data?.Fields.GetValueOrDefault("mirrorSpec") as PackObject)?.Fields.GetValueOrDefault("mirrorSpec")),
            OverrideBoneConstraints = ReadConstraints(root.Fields.GetValueOrDefault("overrides") as PackObject),
        };
    }

    private static IReadOnlyList<Gw2ModelBoneConstraint> ReadConstraints(PackObject? owner) =>
        PackValue.AsObjects(owner?.Fields.GetValueOrDefault("boneConstraints"))
            .Select(constraint => new Gw2ModelBoneConstraint(
                PackValue.AsU64(constraint.Fields.GetValueOrDefault("token")),
                PackValue.AsU32(constraint.Fields.GetValueOrDefault("flags")),
                F32(constraint.Fields.GetValueOrDefault("twistOffset")),
                F32(constraint.Fields.GetValueOrDefault("animBlend")),
                F32(constraint.Fields.GetValueOrDefault("drag")),
                F32(constraint.Fields.GetValueOrDefault("gravity")),
                F32(constraint.Fields.GetValueOrDefault("wind")),
                PackValue.AsObjects(constraint.Fields.GetValueOrDefault("links"))
                    .Select(link => new Gw2ModelBoneConstraintLink(
                        F32(link.Fields.GetValueOrDefault("angle")),
                        F32(link.Fields.GetValueOrDefault("azimuth")),
                        Vec2(link, "distance"),
                        PackValue.AsU64(link.Fields.GetValueOrDefault("token"))))
                    .ToArray())
            {
                EllipseRatio = F32(constraint.Fields.GetValueOrDefault("ellipseRatio")),
                CollisionRadius = F32(constraint.Fields.GetValueOrDefault("collisionRadius")),
                Angle = Vec2(constraint, "angle"),
                AngleStrength = F32(constraint.Fields.GetValueOrDefault("angleStrength")),
                AngleType = PackValue.AsU8(constraint.Fields.GetValueOrDefault("angleType")),
                DistanceInner = Vec2(constraint, "distanceInner"),
                DistanceInnerStrength = F32(constraint.Fields.GetValueOrDefault("distanceInnerStrength")),
                DistanceInnerType = PackValue.AsU8(constraint.Fields.GetValueOrDefault("distanceInnerType")),
                DistanceOuter = Vec2(constraint, "distanceOuter"),
                DistanceOuterStrength = F32(constraint.Fields.GetValueOrDefault("distanceOuterStrength")),
                DistanceOuterType = PackValue.AsU8(constraint.Fields.GetValueOrDefault("distanceOuterType")),
                Twist = Vec2(constraint, "twist"),
                TwistStrength = F32(constraint.Fields.GetValueOrDefault("twistStrength")),
                TwistType = PackValue.AsU8(constraint.Fields.GetValueOrDefault("twistType")),
            })
            .ToArray();

    private static Vector2 Vec2(PackObject obj, string name) =>
        obj.Fields.GetValueOrDefault(name) is float[] { Length: >= 2 } values ? new Vector2(values[0], values[1]) : Vector2.Zero;

    private static Vector3 Vec3(PackObject? obj, string name) =>
        obj?.Fields.GetValueOrDefault(name) is float[] values && values.Length >= 3
            ? new Vector3(values[0], values[1], values[2])
            : Vector3.Zero;

    private static Quaternion Quat(PackObject? obj, string name) =>
        obj?.Fields.GetValueOrDefault(name) is float[] values && values.Length >= 4
            ? new Quaternion(values[0], values[1], values[2], values[3])
            : Quaternion.Identity;

    private static Matrix4x4 Matrix(object? value)
    {
        Span<float> m = stackalloc float[16];
        var count = 0;
        switch (value)
        {
            case float[] flat:
                foreach (var f in flat) { if (count >= 16) break; m[count++] = f; }
                break;
            case object?[] rows:
                foreach (var row in rows)
                    if (row is float[] rowValues)
                        foreach (var f in rowValues) { if (count >= 16) break; m[count++] = f; }
                break;
        }

        if (count == 9)
            return new Matrix4x4(
                m[0], m[1], m[2], 0f,
                m[3], m[4], m[5], 0f,
                m[6], m[7], m[8], 0f,
                0f, 0f, 0f, 1f);
        if (count < 16) return Matrix4x4.Identity;
        return new Matrix4x4(
            m[0], m[1], m[2], m[3],
            m[4], m[5], m[6], m[7],
            m[8], m[9], m[10], m[11],
            m[12], m[13], m[14], m[15]);
    }

    private static float F32(object? value) => value switch
    {
        float single => single,
        double wide => (float)wide,
        _ => 0,
    };
}
