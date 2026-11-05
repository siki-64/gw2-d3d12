using System;
using System.Collections.Generic;
using System.Numerics;
using Crystal;
using Crystal.Graphics;
using Gw2.Dat.Content;

namespace Gw2.Client.Graphics;

public sealed class GrTerrainChunk : IGrMaterialMesh, IDisposable
{
    private readonly GraphicsBuffer _vertices;
    private readonly GraphicsBuffer _indices;
    private readonly int _indexCount;
    private readonly IReadOnlyList<Texture?> _materialTextures;

    public GrTerrainChunk(GraphicsDevice device, Gw2MapTerrainChunkGeometry geometry, uint materialFileId, IReadOnlyList<Texture?> materialTextures)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        MaterialFileId = materialFileId;
        _materialTextures = materialTextures;
        var vertices = new GrVertexTerrain[geometry.Vertices.Length];
        var positions = new Vector3[vertices.Length];
        for (var index = 0; index < vertices.Length; index++)
        {
            var source = geometry.Vertices[index];
            vertices[index] = new GrVertexTerrain
            {
                Position = new Vector3(source.Position.X, source.Position.Y, source.Position.Z),
                Normal = new Vector3(source.Normal.X, source.Normal.Y, source.Normal.Z),
                Tangent = new Vector3(source.Tangent.X, source.Tangent.Y, source.Tangent.Z),
                Bitangent = new Vector3(source.Bitangent.X, source.Bitangent.Y, source.Bitangent.Z),
                AtlasUv = new Vector2(source.AtlasUv.X, source.AtlasUv.Y),
                SurfaceUv = new Vector2(source.SurfaceUv.X, source.SurfaceUv.Y),
                DetailUv = new Vector2(source.DetailUv.X, source.DetailUv.Y),
            };
            positions[index] = vertices[index].Position;
        }

        _vertices = GrBuffers.Vertex<GrVertexTerrain>(device, vertices, "TerrainChunkVertices");
        _indices = GrBuffers.Index(device, geometry.Indices, "TerrainChunkIndices");
        _indexCount = geometry.Indices.Length;
        Bounds = BoundingBox.CreateFromPoints(positions);
    }

    public BoundingBox Bounds { get; }

    public uint MaterialFileId { get; }

    public int MaterialTextureCount => _materialTextures.Count;

    public VertexLayout VertexLayout => GrVertexTerrain.Layout;

    public Texture? MaterialTexture(int resourceIndex) =>
        resourceIndex >= 0 && resourceIndex < _materialTextures.Count ? _materialTextures[resourceIndex] : null;

    public void Draw(GraphicsDevice device)
    {
        device.SetVertexBuffer(_vertices);
        device.SetIndexBuffer(_indices);
        device.DrawIndexed(_indexCount);
    }

    public void Dispose()
    {
        _vertices.Dispose();
        _indices.Dispose();
    }
}
