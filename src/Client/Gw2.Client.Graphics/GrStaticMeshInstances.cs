using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Crystal;
using Crystal.Graphics;
using Gw2.Dat.Content;

namespace Gw2.Client.Graphics;

public sealed class GrStaticMeshInstances : IGrInstancedMesh, IDisposable
{
    private readonly GrStaticMesh _mesh;
    private readonly GraphicsBuffer _instances;

    public GrStaticMeshInstances(GraphicsDevice device, GrStaticMesh mesh, IReadOnlyList<Matrix4x4> worlds, IReadOnlyList<uint> colors, Gw2ZoneScatterFade fade, uint materialFileId = 0)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(worlds);
        _mesh = mesh;
        InstanceCount = worlds.Count;
        Fade = fade;
        MaterialFileId = materialFileId != 0 ? materialFileId : mesh.MaterialFileId;
        Bounds = GrWorldBounds.Union(worlds.Select(world => GrWorldBounds.Of(mesh.LocalMinimum, mesh.LocalMaximum, world)));
        _instances = GrBuffers.Vertex<GrVertexInstance>(device, worlds.Select((world, index) => GrVertexInstance.From(world, colors[index])).ToArray(), "ScatterInstances");
    }

    public int InstanceCount { get; }

    public BoundingBox Bounds { get; }

    public Gw2ZoneScatterFade Fade { get; }

    public uint MaterialFileId { get; }

    public bool TwoSided => _mesh.TwoSided;

    public uint DrawFlags => _mesh.DrawFlags;

    public int SortBucketOverride => _mesh.SortBucketOverride;

    public IReadOnlyDictionary<uint, Vector4> MaterialConstants => _mesh.MaterialConstants;

    public GrTextureTransformAnimation? TextureTransforms => _mesh.TextureTransforms;

    public int MaterialTextureCount => _mesh.MaterialTextureCount;

    public VertexLayout VertexLayout => GrVertexStatic.WithInstances;

    public Texture? MaterialTexture(int resourceIndex) => _mesh.MaterialTexture(resourceIndex);

    public void Draw(GraphicsDevice device) => _mesh.DrawInstances(device, _instances, InstanceCount);

    public void Dispose() => _instances.Dispose();
}
