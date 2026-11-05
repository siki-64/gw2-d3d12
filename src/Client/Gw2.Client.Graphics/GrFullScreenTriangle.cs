using System.Numerics;
using Crystal.Graphics;

namespace Gw2.Client.Graphics;

public static class GrFullScreenTriangle
{
    public static readonly VertexLayout Layout = new(new VertexElement("POSITION", 0, VertexElementFormat.Vector3, 0));

    private static readonly Vector3[] Vertices =
    [
        new(-1f, -1f, 0f),
        new(-1f, 3f, 0f),
        new(3f, -1f, 0f),
    ];

    public static void Draw(GraphicsDevice graphicsDevice)
    {
        graphicsDevice.SetVertexData<Vector3>(Vertices);
        graphicsDevice.Draw(Vertices.Length);
    }
}
