using System.Collections.Generic;
using System.Numerics;
using Crystal.Graphics;

namespace Gw2.Client.Graphics;

public sealed class GrStaticMeshIndirectBatch : IGrInstancedMesh
{
    private readonly GraphicsBuffer _rows;
    private readonly GraphicsBuffer _arguments;
    private readonly long _argumentsOffset;

    public GrStaticMeshIndirectBatch(GrStaticMesh mesh, GraphicsBuffer rows, GraphicsBuffer arguments, long argumentsOffset)
    {
        Mesh = mesh;
        _rows = rows;
        _arguments = arguments;
        _argumentsOffset = argumentsOffset;
    }

    public GrStaticMesh Mesh { get; }

    public int MaterialTextureCount => Mesh.MaterialTextureCount;

    public IReadOnlyDictionary<uint, Vector4> MaterialConstants => Mesh.MaterialConstants;

    public GrTextureTransformAnimation? TextureTransforms => Mesh.TextureTransforms;

    public VertexLayout VertexLayout => GrVertexStatic.WithInstances;

    public Texture? MaterialTexture(int resourceIndex) => Mesh.MaterialTexture(resourceIndex);

    public Texture? SamplerTexture(GrArchiveSampler sampler) => ((IGrMaterialMesh)Mesh).SamplerTexture(sampler);

    public void Draw(GraphicsDevice device) => Mesh.DrawInstancesIndirect(device, _rows, _arguments, _argumentsOffset);
}
