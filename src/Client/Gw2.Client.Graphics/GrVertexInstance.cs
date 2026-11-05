using System.Numerics;
using System.Runtime.InteropServices;
using Crystal.Graphics;

namespace Gw2.Client.Graphics;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct GrVertexInstance
{
    public static VertexElement[] ElementsAt(int slot) =>
    [
        new("TEXCOORD", 7, VertexElementFormat.Vector4, 0, slot, 1),
        new("TEXCOORD", 6, VertexElementFormat.Vector4, 16, slot, 1),
        new("TEXCOORD", 5, VertexElementFormat.Vector4, 32, slot, 1),
        new("TEXCOORD", 4, VertexElementFormat.Vector4, 48, slot, 1),
    ];

    public const int SizeInBytes = 64;

    private const float ColorByteScale = 1f / 255f;

    public Vector4 RowX;
    public Vector4 RowY;
    public Vector4 RowZ;
    public Vector4 Color;

    public static GrVertexInstance From(Matrix4x4 world, Vector4 color) => new()
    {
        Color = color,
        RowX = new Vector4(world.M11, world.M21, world.M31, world.M41),
        RowY = new Vector4(world.M12, world.M22, world.M32, world.M42),
        RowZ = new Vector4(world.M13, world.M23, world.M33, world.M43),
    };

    public static GrVertexInstance From(Matrix4x4 world, uint argb) => new()
    {
        Color = new Vector4((byte)(argb >> 16) * ColorByteScale, (byte)(argb >> 8) * ColorByteScale, (byte)argb * ColorByteScale, (byte)(argb >> 24) * ColorByteScale),
        RowX = new Vector4(world.M11, world.M21, world.M31, world.M41),
        RowY = new Vector4(world.M12, world.M22, world.M32, world.M42),
        RowZ = new Vector4(world.M13, world.M23, world.M33, world.M43),
    };
}
