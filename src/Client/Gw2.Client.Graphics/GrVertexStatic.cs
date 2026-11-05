using System.Numerics;
using System.Runtime.InteropServices;
using Crystal;
using Crystal.Graphics;

namespace Gw2.Client.Graphics;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct GrVertexStatic
{
    public static VertexElement[] ElementsAt(int slot) =>
    [
        new("POSITION", 0, VertexElementFormat.Vector3, 0, slot),
        new("NORMAL", 0, VertexElementFormat.Vector3, 12, slot),
        new("TANGENT", 0, VertexElementFormat.Vector3, 24, slot),
        new("BITANGENT", 0, VertexElementFormat.Vector3, 36, slot),
        new("COLOR", 0, VertexElementFormat.Color, 48, slot),
        new("TEXCOORD", 0, VertexElementFormat.Vector2, 52, slot),
        new("TEXCOORD", 1, VertexElementFormat.Vector2, 60, slot),
        new("TEXCOORD", 2, VertexElementFormat.Vector2, 68, slot),
        new("TEXCOORD", 3, VertexElementFormat.Vector2, 76, slot),
    ];

    public static readonly VertexLayout WithExtraUvs = new([.. ElementsAt(0), .. GrVertexExtraUv.ElementsAt(1)]);

    public static readonly VertexLayout WithInstances = new([.. ElementsAt(0), .. GrVertexInstance.ElementsAt(1)]);

    public Vector3 Position;
    public Vector3 Normal;
    public Vector3 Tangent;
    public Vector3 Bitangent;
    public Color Color;
    public Vector2 Uv0;
    public Vector2 Uv1;
    public Vector2 Uv2;
    public Vector2 Uv3;
}
