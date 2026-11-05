using System;
using System.Collections.Generic;
using System.Numerics;
using Crystal.Graphics;

namespace Gw2.Client.Graphics;

public sealed class GrStaticMeshBatch : IGrInstancedMesh
{
    private readonly ReadOnlyMemory<GrVertexInstance> _instances;

    public GrStaticMeshBatch(GrStaticMesh mesh, ReadOnlyMemory<GrVertexInstance> instances)
    {
        Mesh = mesh;
        _instances = instances;
    }

    public GrStaticMesh Mesh { get; }

    public int InstanceCount => _instances.Length;

    public int MaterialTextureCount => Mesh.MaterialTextureCount;

    public IReadOnlyDictionary<uint, Vector4> MaterialConstants => Mesh.MaterialConstants;

    public GrTextureTransformAnimation? TextureTransforms => Mesh.TextureTransforms;

    public VertexLayout VertexLayout => GrVertexStatic.WithInstances;

    public Texture? MaterialTexture(int resourceIndex) => Mesh.MaterialTexture(resourceIndex);

    public Texture? SamplerTexture(GrArchiveSampler sampler) => ((IGrMaterialMesh)Mesh).SamplerTexture(sampler);

    public void Draw(GraphicsDevice device) => Mesh.DrawInstances(device, _instances.Span);
}
