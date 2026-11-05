using System.Numerics;
using System.Runtime.InteropServices;
using Crystal;
using Crystal.Graphics;

namespace Gw2.Client.Graphics;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct GrVertexWater
{
    public static readonly VertexLayout Layout = new(
        new VertexElement("POSITION", 0, VertexElementFormat.Vector3, 0),
        new VertexElement("NORMAL", 0, VertexElementFormat.Vector3, 12),
        new VertexElement("COLOR", 0, VertexElementFormat.Color, 24));

    public Vector3 Position;
    public Vector3 Normal;
    public Color Color;
}
