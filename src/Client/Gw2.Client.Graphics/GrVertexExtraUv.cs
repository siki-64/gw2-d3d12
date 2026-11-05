using System.Numerics;
using System.Runtime.InteropServices;
using Crystal.Graphics;

namespace Gw2.Client.Graphics;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct GrVertexExtraUv
{
    public static VertexElement[] ElementsAt(int slot) =>
    [
        new("TEXCOORD", 4, VertexElementFormat.Vector2, 0, slot),
        new("TEXCOORD", 5, VertexElementFormat.Vector2, 8, slot),
        new("TEXCOORD", 6, VertexElementFormat.Vector2, 16, slot),
        new("TEXCOORD", 7, VertexElementFormat.Vector2, 24, slot),
    ];

    public Vector2 Uv4;
    public Vector2 Uv5;
    public Vector2 Uv6;
    public Vector2 Uv7;
}
