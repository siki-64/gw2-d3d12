using System.Collections.Generic;
using System.Numerics;
using Crystal;

namespace Gw2.Client.Graphics;

public sealed record GrSceneProp(GrStaticMesh Mesh, Matrix4x4 World, IReadOnlyDictionary<uint, Vector4> Constants, Vector4 ModelColor, GrArchiveShaderMaterial? Material, GrArchiveShaderMaterial? NormalSpecMaterial, GrPropLodState LodState, int LodLevel)
{
    public BoundingBox WorldBounds { get; } = GrWorldBounds.Of(Mesh.LocalMinimum, Mesh.LocalMaximum, World);

    public GrDepthCaster? DepthCaster { get; set; }

    public GrArchiveShaderMaterial? GpuNormalSpecMaterial { get; init; }

    public GrArchiveShaderMaterial? GpuMainMaterial { get; init; }

    public bool CastsShadowOnGpu { get; init; }

    public bool DrawsOnGpu => GpuNormalSpecMaterial is not null || GpuMainMaterial is not null;
}
