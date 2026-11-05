using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Crystal;
using Crystal.Graphics;
using Gw2.Client.Model;

namespace Gw2.Client.Graphics;

public sealed class GrStaticMesh : IGrMaterialMesh, IDisposable
{
    private const int SecondStreamSlot = 1;
    private readonly GrStaticMeshVertices _sharedVertices;
    private readonly GraphicsBuffer _vertices;
    private readonly GraphicsBuffer _extraUvs;
    private readonly GraphicsBuffer _indices;
    private readonly IReadOnlyList<Texture?> _materialTextures;

    public GrStaticMesh(GraphicsDevice device, Gw2Mesh mesh, uint materialFileId, IReadOnlyList<Texture?> materialTextures, IReadOnlyDictionary<uint, Vector4> materialConstants, int[]? indices = null)
        : this(device, new GrStaticMeshVertices(device, mesh), mesh, materialFileId, materialTextures, materialConstants, indices)
    {
    }

    public GrStaticMesh(GraphicsDevice device, GrStaticMeshVertices sharedVertices, Gw2Mesh mesh, uint materialFileId, IReadOnlyList<Texture?> materialTextures, IReadOnlyDictionary<uint, Vector4> materialConstants, int[]? indices = null)
    {
        ArgumentNullException.ThrowIfNull(sharedVertices);
        ArgumentNullException.ThrowIfNull(mesh);
        MaterialConstants = materialConstants;
        MaterialFileId = materialFileId;
        _materialTextures = materialTextures;
        _sharedVertices = sharedVertices;
        _vertices = sharedVertices.Vertices;
        _extraUvs = sharedVertices.ExtraUvs;
        var triangleIndices = indices ?? mesh.Indices;
        _indices = GrBuffers.Index(device, triangleIndices, "StaticMeshIndices");
        IndexCount = triangleIndices.Length;
        LocalMinimum = sharedVertices.LocalMinimum;
        LocalMaximum = sharedVertices.LocalMaximum;
    }

    public Vector3 LocalMinimum { get; }

    public Vector3 LocalMaximum { get; }

    public uint MaterialFileId { get; }

    public bool TwoSided { get; init; }

    public uint DrawFlags { get; init; }

    public int SortBucketOverride { get; init; }

    public IReadOnlyDictionary<uint, Vector4> MaterialConstants { get; }

    public GrTextureTransformAnimation? TextureTransforms { get; init; }

    public int MaterialTextureCount => _materialTextures.Count;

    public VertexLayout VertexLayout => GrVertexStatic.WithExtraUvs;

    public int IndexCount { get; }

    public Texture? MaterialTexture(int resourceIndex) =>
        resourceIndex >= 0 && resourceIndex < _materialTextures.Count ? _materialTextures[resourceIndex] : null;

    public void Draw(GraphicsDevice device)
    {
        device.SetVertexBuffer(_vertices);
        device.SetVertexBuffer(_extraUvs, SecondStreamSlot);
        device.SetIndexBuffer(_indices);
        device.DrawIndexed(IndexCount);
    }

    public void DrawInstances(GraphicsDevice device, GraphicsBuffer instances, int instanceCount)
    {
        device.SetVertexBuffer(_vertices);
        device.SetVertexBuffer(instances, SecondStreamSlot);
        device.SetIndexBuffer(_indices);
        device.DrawIndexedInstanced(IndexCount, instanceCount);
    }

    public void DrawInstances(GraphicsDevice device, ReadOnlySpan<GrVertexInstance> instances)
    {
        device.SetVertexBuffer(_vertices);
        device.SetVertexData(instances, SecondStreamSlot);
        device.SetIndexBuffer(_indices);
        device.DrawIndexedInstanced(IndexCount, instances.Length);
    }

    public void DrawInstancesIndirect(GraphicsDevice device, GraphicsBuffer instances, GraphicsBuffer arguments, long argumentsOffset)
    {
        device.SetVertexBuffer(_vertices);
        device.SetVertexBuffer(instances, SecondStreamSlot);
        device.SetIndexBuffer(_indices);
        device.DrawIndexedInstancedIndirect(arguments, argumentsOffset);
    }

    public void Dispose()
    {
        _sharedVertices.Release();
        _indices.Dispose();
    }
}
