using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Crystal;
using Crystal.Graphics;
using Gw2.Client.Model;

namespace Gw2.Client.Graphics;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct GrVertexEffectParticle
{
    public static readonly VertexLayout Layout = new(
        new VertexElement("POSITION", 0, VertexElementFormat.Vector3, 0),
        new VertexElement("NORMAL", 0, VertexElementFormat.Vector3, 12),
        new VertexElement("COLOR", 0, VertexElementFormat.Vector4, 24),
        new VertexElement("TEXCOORD", 0, VertexElementFormat.Vector2, 40),
        new VertexElement("TEXCOORD", 1, VertexElementFormat.Vector2, 48),
        new VertexElement("TEXCOORD", 2, VertexElementFormat.Vector2, 56),
        new VertexElement("TANGENT", 0, VertexElementFormat.Vector3, 64),
        new VertexElement("BITANGENT", 0, VertexElementFormat.Vector3, 76),
        new VertexElement("TEXCOORD", 3, VertexElementFormat.Vector2, 88));

    public Vector3 Position;
    public Vector3 Normal;
    public Vector4 Color;
    public Vector2 TexCoord;
    public Vector2 SecondTexCoord;
    public Vector2 ThirdTexCoord;
    public Vector3 Tangent;
    public Vector3 Bitangent;
    public Vector2 FourthTexCoord;
}

public sealed class GrEffectParticleCloud(GraphicsDevice device, uint materialFileId, IReadOnlyList<Texture?> materialTextures, IReadOnlyDictionary<uint, Vector4> materialConstants) : IGrMaterialMesh, IDisposable
{
    private const int IndicesPerQuad = 6;
    private const int MinimumQuadCapacity = 64;
    private GraphicsBuffer? _indices;
    private int _quadCapacity;
    private GrVertexEffectParticle[] _staging = [];

    public uint MaterialFileId { get; } = materialFileId;

    public IReadOnlyDictionary<uint, Vector4> MaterialConstants { get; } = materialConstants;

    public int MaterialTextureCount => materialTextures.Count;

    public VertexLayout VertexLayout => GrVertexEffectParticle.Layout;

    public int QuadCount { get; private set; }

    public BoundingBox Bounds { get; private set; }

    public Texture? MaterialTexture(int resourceIndex) =>
        resourceIndex >= 0 && resourceIndex < materialTextures.Count ? materialTextures[resourceIndex] : null;

    public void SetCorners(IReadOnlyList<Gw2ParticleCorner> corners)
    {
        PrepareCorners(corners);
        EnsureQuadIndices();
    }

    public void PrepareCorners(IReadOnlyList<Gw2ParticleCorner> corners)
    {
        QuadCount = corners.Count / Gw2ParticleQuads.CornersPerQuad;
        if (QuadCount == 0) return;
        var vertexCount = QuadCount * Gw2ParticleQuads.CornersPerQuad;
        if (_staging.Length < vertexCount) _staging = new GrVertexEffectParticle[QuadCapacityFor(QuadCount) * Gw2ParticleQuads.CornersPerQuad];
        var minimum = new Vector3(float.MaxValue);
        var maximum = new Vector3(float.MinValue);
        for (var index = 0; index < vertexCount; index++)
        {
            var corner = corners[index];
            _staging[index] = new GrVertexEffectParticle { Position = corner.Position, Normal = corner.Normal, Color = corner.Colour, TexCoord = corner.TexCoord, SecondTexCoord = corner.SecondTexCoord ?? corner.TexCoord, ThirdTexCoord = corner.ThirdTexCoord ?? corner.TexCoord, Tangent = corner.Tangent, Bitangent = corner.Bitangent, FourthTexCoord = corner.FourthTexCoord ?? corner.TexCoord };
            minimum = Vector3.Min(minimum, corner.Position);
            maximum = Vector3.Max(maximum, corner.Position);
        }
        Bounds = new BoundingBox(minimum, maximum);
    }

    public void EnsureQuadIndices()
    {
        if (QuadCount == 0) return;
        EnsureCapacity(QuadCount);
    }

    public void Draw(GraphicsDevice graphicsDevice)
    {
        if (QuadCount == 0 || _indices is null) return;
        graphicsDevice.SetVertexData<GrVertexEffectParticle>(_staging.AsSpan(0, QuadCount * Gw2ParticleQuads.CornersPerQuad));
        graphicsDevice.SetIndexBuffer(_indices);
        graphicsDevice.DrawIndexed(QuadCount * IndicesPerQuad);
    }

    private void EnsureCapacity(int quadCount)
    {
        if (quadCount <= _quadCapacity) return;
        _quadCapacity = Math.Max(QuadCapacityFor(quadCount), _staging.Length / Gw2ParticleQuads.CornersPerQuad);
        _indices?.Dispose();
        _indices = GrBuffers.Index(device, QuadIndices(_quadCapacity), "EffectParticleIndices");
    }

    private int QuadCapacityFor(int quadCount)
    {
        var capacity = Math.Max(MinimumQuadCapacity, _quadCapacity * 2);
        while (capacity < quadCount) capacity *= 2;
        return capacity;
    }

    private static int[] QuadIndices(int quadCount)
    {
        var indices = new int[quadCount * IndicesPerQuad];
        for (var quad = 0; quad < quadCount; quad++)
        {
            var corner = quad * Gw2ParticleQuads.CornersPerQuad;
            var first = quad * IndicesPerQuad;
            indices[first] = corner;
            indices[first + 1] = corner + 1;
            indices[first + 2] = corner + 2;
            indices[first + 3] = corner;
            indices[first + 4] = corner + 2;
            indices[first + 5] = corner + 3;
        }
        return indices;
    }

    public void Dispose()
    {
        _indices?.Dispose();
    }
}
