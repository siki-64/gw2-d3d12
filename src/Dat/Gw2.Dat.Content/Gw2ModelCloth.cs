using Gw2.Formats;
using System.Numerics;

namespace Gw2.Dat.Content;

public sealed record Gw2ModelClothBoneWeight(ulong Token, byte Weight);

public sealed record Gw2ModelClothGroupBinding(ulong StrippedToken, string BoneName, Vector3 ObbMin, Vector3 ObbMax);

public sealed record Gw2ModelClothSoftLock(byte Weight, ushort VertexIndex);

public sealed record Gw2ModelClothConstraint(ushort Distance, ushort Relationship, ushort VertexIndexA, ushort VertexIndexB);

public sealed record Gw2ModelCloth(
    uint MaterialIndex,
    float Drag,
    float Gravity,
    float Compressibility,
    float Slack,
    float Stretchiness,
    float Weight,
    float Wind,
    uint MeshFvf,
    byte[] MeshVertices,
    IReadOnlyList<ushort> Indices,
    IReadOnlyList<IReadOnlyList<Gw2ModelClothBoneWeight>> Groups,
    IReadOnlyList<Gw2ModelClothGroupBinding> GroupBindings,
    IReadOnlyList<Gw2ModelClothSoftLock> SoftLocks,
    IReadOnlyList<Gw2ModelClothConstraint> Lod0Constraints,
    IReadOnlyList<Gw2ModelClothConstraint> Lod1Constraints,
    IReadOnlyList<ushort> Lod1Indices,
    IReadOnlyList<Vector3> BarycentricCoords,
    IReadOnlyList<ushort> BarycentricIndices,
    IReadOnlyList<uint> ObstacleIndices,
    ushort LockCount,
    IReadOnlyList<uint> LockedNormals,
    IReadOnlyList<uint> LockedTangents,
    IReadOnlyList<uint> LockedBitangents,
    ushort Lod1VertexCount,
    byte Flags,
    byte Rigidness,
    float TranslateWeight,
    ulong VisBone)
{
    public static IReadOnlyList<Gw2ModelCloth> ParseAll(PackObject modelRoot) =>
        PackValue.AsObjects(modelRoot.Fields.GetValueOrDefault("clothData")).Select(Parse).ToArray();

    private static Gw2ModelCloth Parse(PackObject cloth)
    {
        var mesh = cloth.Fields.GetValueOrDefault("mesh") as PackObject;
        return new(
            PackValue.RequireU32(cloth, "materialIndex"),
            PackValue.RequireF32(cloth, "drag"),
            PackValue.RequireF32(cloth, "gravity"),
            PackValue.RequireF32(cloth, "compressibility"),
            PackValue.RequireF32(cloth, "slack"),
            PackValue.RequireF32(cloth, "stretchiness"),
            PackValue.RequireF32(cloth, "weight"),
            PackValue.RequireF32(cloth, "wind"),
            mesh is null ? 0 : PackValue.RequireU32(mesh, "fvf"),
            PackValue.AsBytes(mesh?.Fields.GetValueOrDefault("vertices")),
            PackValue.AsU16List(cloth.Fields.GetValueOrDefault("indices")),
            PackValue.AsObjects(cloth.Fields.GetValueOrDefault("groups"))
                .Select(group => (IReadOnlyList<Gw2ModelClothBoneWeight>)PackValue.AsObjects(group.Fields.GetValueOrDefault("weights"))
                    .Select(weight => new Gw2ModelClothBoneWeight(
                        PackValue.RequireU64(weight, "token"),
                        (byte)PackValue.RequireU32(weight, "weight")))
                    .ToArray())
                .ToArray(),
            PackValue.AsObjects(cloth.Fields.GetValueOrDefault("groupBindings"))
                .Select(binding => new Gw2ModelClothGroupBinding(
                    PackValue.RequireU64(binding, "strippedToken"),
                    binding.Fields.GetValueOrDefault("boneName") as string ?? string.Empty,
                    Vec3(binding, "OBBMin"),
                    Vec3(binding, "OBBMax")))
                .ToArray(),
            PackValue.AsObjects(cloth.Fields.GetValueOrDefault("softLocks"))
                .Select(softLock => new Gw2ModelClothSoftLock(
                    (byte)PackValue.RequireU32(softLock, "weight"),
                    (ushort)PackValue.RequireU32(softLock, "vertIndex")))
                .ToArray(),
            Constraints(cloth, "lod0Constraints"),
            Constraints(cloth, "lod1Constraints"),
            PackValue.AsU16List(cloth.Fields.GetValueOrDefault("lod1Indices")),
            PackValue.AsVec3List(cloth.Fields.GetValueOrDefault("barycentricCoords")),
            PackValue.AsU16List(cloth.Fields.GetValueOrDefault("barycentricIndices")),
            PackValue.AsU32List(cloth.Fields.GetValueOrDefault("obstacleIndices")),
            (ushort)PackValue.RequireU32(cloth, "lockCount"),
            PackValue.AsU32List(cloth.Fields.GetValueOrDefault("lockedNormals")),
            PackValue.AsU32List(cloth.Fields.GetValueOrDefault("lockedTanegents")),
            PackValue.AsU32List(cloth.Fields.GetValueOrDefault("lockedBitangents")),
            (ushort)PackValue.RequireU32(cloth, "lod1VertexCount"),
            (byte)PackValue.RequireU32(cloth, "flags"),
            (byte)PackValue.RequireU32(cloth, "rigidness"),
            PackValue.RequireF32(cloth, "translateWeight"),
            PackValue.RequireU64(cloth, "visBone"));
    }

    private static IReadOnlyList<Gw2ModelClothConstraint> Constraints(PackObject cloth, string name) =>
        PackValue.AsObjects(cloth.Fields.GetValueOrDefault(name))
            .Select(constraint => new Gw2ModelClothConstraint(
                (ushort)PackValue.RequireU32(constraint, "distance"),
                (ushort)PackValue.RequireU32(constraint, "relationship"),
                (ushort)PackValue.RequireU32(constraint, "vertIndexA"),
                (ushort)PackValue.RequireU32(constraint, "vertIndexB")))
            .ToArray();

    private static Vector3 Vec3(PackObject obj, string name) =>
        obj.Fields.GetValueOrDefault(name) is float[] { Length: >= 3 } values ? new Vector3(values[0], values[1], values[2]) : Vector3.Zero;
}
