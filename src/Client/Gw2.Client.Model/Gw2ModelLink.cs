using System.Numerics;
using Gw2.Dat.Content;
using Gw2.Formats;

namespace Gw2.Client.Model;

[Flags]
public enum Gw2ModelLinkFlags : uint
{
    None = 0,
    IgnoreChildBoneScale = 0x1,
    DontInheritRotation = 0x2,
    DontInheritScale = 0x4,
    DontInheritTime = 0x8,
    RootToRoot = 0x10,
    InheritColor = 0x20,
    EquipmentAttach = 0xBE0,
}

public static class Gw2ModelLink
{
    public static int ChildBoneIndex(Gw2ModelSkeleton child, ulong childBoneToken)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (childBoneToken == 0) return -1;
        for (var index = 0; index < child.Bones.Count; index++)
            if (Token64.EncodeAfterColon(child.Bones[index].Name) == childBoneToken)
                return index;
        return -1;
    }

    public static Matrix4x4 ChildWorld(Matrix4x4 parentBoneWorld, Gw2ModelSkeleton child, ulong childBoneToken, Gw2ModelLinkFlags flags, Matrix4x4 offset)
    {
        ArgumentNullException.ThrowIfNull(child);
        var parent = ParentWithoutInheritance(parentBoneWorld, flags);
        var world = offset * parent;
        var childBone = ChildBoneIndex(child, childBoneToken);
        if (childBone < 0) return world;

        var gripInverse = child.Bones[childBone].InverseWorld;
        if ((flags & Gw2ModelLinkFlags.IgnoreChildBoneScale) != 0) gripInverse = WithoutScale(gripInverse);
        return gripInverse * world;
    }

    private static Matrix4x4 ParentWithoutInheritance(Matrix4x4 parent, Gw2ModelLinkFlags flags)
    {
        var keepsRotation = (flags & Gw2ModelLinkFlags.DontInheritRotation) == 0;
        var keepsScale = (flags & Gw2ModelLinkFlags.DontInheritScale) == 0;
        if (keepsRotation && keepsScale) return parent;
        if (!Matrix4x4.Decompose(parent, out var scale, out var rotation, out var translation)) return parent;
        return Matrix4x4.CreateScale(keepsScale ? scale : Vector3.One)
            * Matrix4x4.CreateFromQuaternion(keepsRotation ? rotation : Quaternion.Identity)
            * Matrix4x4.CreateTranslation(translation);
    }

    private static Matrix4x4 WithoutScale(Matrix4x4 matrix)
    {
        if (!Matrix4x4.Decompose(matrix, out _, out var rotation, out var translation)) return matrix;
        return Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(translation);
    }
}
