using System.Numerics;

namespace Gw2.Client.Graphics;

public static class GrMatrix
{
    public static Vector4[] ToRegisterRows(Matrix4x4 matrix) => new[]
    {
        new Vector4(matrix.M11, matrix.M21, matrix.M31, matrix.M41),
        new Vector4(matrix.M12, matrix.M22, matrix.M32, matrix.M42),
        new Vector4(matrix.M13, matrix.M23, matrix.M33, matrix.M43),
        new Vector4(matrix.M14, matrix.M24, matrix.M34, matrix.M44),
    };

    public static Matrix4x4 ClipToWorld(Matrix4x4 view, Matrix4x4 projection)
    {
        Matrix4x4.Invert(projection, out var clipToView);
        return clipToView * RigidInverse(view);
    }

    public static Matrix4x4 RigidInverse(Matrix4x4 rigid)
    {
        var rotation = Matrix4x4.Transpose(rigid with { M41 = 0f, M42 = 0f, M43 = 0f });
        var translation = new Vector3(rigid.M41, rigid.M42, rigid.M43);
        var position = -Vector3.TransformNormal(translation, rotation);
        return rotation with { M41 = position.X, M42 = position.Y, M43 = position.Z, M14 = 0f, M24 = 0f, M34 = 0f, M44 = 1f };
    }
}
