using System.Numerics;
using System.Runtime.InteropServices;
using Crystal.Graphics;

namespace Gw2.Client.Graphics;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct GrVertexTerrain
{
    public static readonly VertexLayout Layout = new(
        new VertexElement("POSITION", 0, VertexElementFormat.Vector3, 0),
        new VertexElement("NORMAL", 0, VertexElementFormat.Vector3, 12),
        new VertexElement("TANGENT", 0, VertexElementFormat.Vector3, 24),
        new VertexElement("BITANGENT", 0, VertexElementFormat.Vector3, 36),
        new VertexElement("TEXCOORD", 0, VertexElementFormat.Vector2, 48),
        new VertexElement("TEXCOORD", 1, VertexElementFormat.Vector2, 56),
        new VertexElement("TEXCOORD", 2, VertexElementFormat.Vector2, 64));

    public Vector3 Position;
    public Vector3 Normal;
    public Vector3 Tangent;
    public Vector3 Bitangent;
    public Vector2 AtlasUv;
    public Vector2 SurfaceUv;
    public Vector2 DetailUv;
}
