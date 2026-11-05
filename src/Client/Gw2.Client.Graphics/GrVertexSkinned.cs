using System.Numerics;
using System.Runtime.InteropServices;
using Crystal.Graphics;

namespace Gw2.Client.Graphics;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct GrVertexSkinned
{
    public static readonly VertexLayout Layout = new(
        new VertexElement("POSITION", 0, VertexElementFormat.Vector3, 0),
        new VertexElement("NORMAL", 0, VertexElementFormat.Vector3, 12),
        new VertexElement("TANGENT", 0, VertexElementFormat.Vector3, 24),
        new VertexElement("BITANGENT", 0, VertexElementFormat.Vector3, 36),
        new VertexElement("TEXCOORD", 0, VertexElementFormat.Vector2, 48),
        new VertexElement("BLENDINDICES", 0, VertexElementFormat.Short4, 56),
        new VertexElement("BLENDWEIGHT", 0, VertexElementFormat.Vector4, 64));

    public Vector3 Position;
    public Vector3 Normal;
    public Vector3 Tangent;
    public Vector3 Bitangent;
    public Vector2 TextureCoordinate;
    public short Bone0;
    public short Bone1;
    public short Bone2;
    public short Bone3;
    public Vector4 Weights;
}
