using System.Numerics;
using Crystal;
using Crystal.Graphics;

namespace Gw2.Client.Graphics;

public readonly record struct GrDepthCaster(IGrMaterialMesh Mesh, Matrix4x4 World, Texture? AlphaMask = null, SamplerState? AlphaMaskSampler = null, BoundingBox? WorldBounds = null, int AlphaMaskResourceIndex = -1, RasterizerState? Rasterizer = null)
{
    public GrDepthCaster WithCurrentAlphaMask() =>
        AlphaMaskResourceIndex < 0 ? this : this with { AlphaMask = Mesh.MaterialTexture(AlphaMaskResourceIndex) };
}
