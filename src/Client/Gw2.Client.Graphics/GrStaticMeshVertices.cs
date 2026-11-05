using System;
using System.Linq;
using System.Numerics;
using Crystal;
using Crystal.Graphics;
using Gw2.Client.Model;

namespace Gw2.Client.Graphics;

public sealed class GrStaticMeshVertices
{
    private int _references = 1;

    public GrStaticMeshVertices(GraphicsDevice device, Gw2Mesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        var vertices = new GrVertexStatic[mesh.Vertices.Length];
        var extraUvs = new GrVertexExtraUv[mesh.Vertices.Length];
        for (var index = 0; index < vertices.Length; index++)
        {
            var source = mesh.Vertices[index];
            var tangent = new Vector3(source.Tangent.X, source.Tangent.Y, source.Tangent.Z);
            var bitangent = Vector3.Cross(source.Normal, tangent) * (source.Tangent.W < 0f ? -1f : 1f);
            vertices[index] = new GrVertexStatic
            {
                Position = new Vector3(source.Position.X, source.Position.Y, source.Position.Z),
                Normal = new Vector3(source.Normal.X, source.Normal.Y, source.Normal.Z),
                Tangent = new Vector3(tangent.X, tangent.Y, tangent.Z),
                Bitangent = new Vector3(bitangent.X, bitangent.Y, bitangent.Z),
                Color = source.HasColor ? new Color(source.Color.R, source.Color.G, source.Color.B, source.Color.A) : Color.White,
                Uv0 = new Vector2(source.Uv0.X, source.Uv0.Y),
                Uv1 = new Vector2(source.Uv1.X, source.Uv1.Y),
                Uv2 = new Vector2(source.Uv2.X, source.Uv2.Y),
                Uv3 = new Vector2(source.Uv3.X, source.Uv3.Y),
            };
            extraUvs[index] = new GrVertexExtraUv
            {
                Uv4 = new Vector2(source.Uv4.X, source.Uv4.Y),
                Uv5 = new Vector2(source.Uv5.X, source.Uv5.Y),
                Uv6 = new Vector2(source.Uv6.X, source.Uv6.Y),
                Uv7 = new Vector2(source.Uv7.X, source.Uv7.Y),
            };
        }

        Vertices = GrBuffers.Vertex<GrVertexStatic>(device, vertices, "StaticMeshVertices");
        ExtraUvs = GrBuffers.Vertex<GrVertexExtraUv>(device, extraUvs, "StaticMeshExtraUvs");
        var positions = mesh.Vertices.Select(vertex => vertex.Position).DefaultIfEmpty();
        LocalMinimum = mesh.MinBound ?? positions.Aggregate(Vector3.Min);
        LocalMaximum = mesh.MaxBound ?? positions.Aggregate(Vector3.Max);
    }

    public GraphicsBuffer Vertices { get; }

    public GraphicsBuffer ExtraUvs { get; }

    public Vector3 LocalMinimum { get; }

    public Vector3 LocalMaximum { get; }

    public GrStaticMeshVertices Share()
    {
        _references++;
        return this;
    }

    public void Release()
    {
        if (--_references > 0) return;
        Vertices.Dispose();
        ExtraUvs.Dispose();
    }
}
