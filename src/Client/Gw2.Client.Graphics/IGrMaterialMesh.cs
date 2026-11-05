using System.Collections.Generic;
using System.Numerics;
using Crystal.Graphics;

namespace Gw2.Client.Graphics;

public interface IGrMaterialMesh
{
    Texture? MaterialTexture(int resourceIndex);

    int MaterialTextureCount => int.MaxValue;

    Texture? SamplerTexture(GrArchiveSampler sampler) => MaterialTexture(sampler.ResourceIndex);

    IReadOnlyDictionary<uint, Vector4> MaterialConstants => GrMaterialConstants.None;

    GrTextureTransformAnimation? TextureTransforms => null;

    VertexLayout VertexLayout { get; }

    void Draw(GraphicsDevice device);
}
