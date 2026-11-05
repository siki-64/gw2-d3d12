using System;
using System.Collections.Generic;
using System.Numerics;
using Gw2.Client.Model;
using Crystal.Graphics;

namespace Gw2.Client.Graphics;

public sealed class GrSkinnedMesh : IGrMaterialMesh, IDisposable
{
    private const int InfluencesPerVertex = 4;

    private readonly Vector3[] _bindPositions;
    private readonly Vector3[] _bindNormals;
    private readonly Vector3[] _bindTangents;
    private readonly Vector3[] _bindBitangents;
    private readonly int[] _skeletonBones;
    private readonly float[] _weights;
    private readonly GrVertexPositionTangentFrameTexture[] _skinned;
    private readonly GraphicsBuffer _indices;
    private readonly int _indexCount;
    private readonly (Vector3 Center, Vector3 Extent, int SkeletonBone)[] _boneBounds;
    private readonly IReadOnlyList<Gw2MorphTarget> _morphTargets;
    private readonly Vector3[] _morphedPositions;
    private readonly Vector3[] _morphedNormals;
    private readonly GraphicsBuffer _skinnedOnGpuVertices;

    private GrSkinnedMesh(GraphicsDevice device, Gw2Mesh mesh, Func<long, int> skeletonBoneOf, Texture2D atlas, Texture2D normalAtlas)
    {
        Atlas = atlas;
        NormalAtlas = normalAtlas;
        var vertexCount = mesh.Vertices.Length;
        _bindPositions = new Vector3[vertexCount];
        _bindNormals = new Vector3[vertexCount];
        _bindTangents = new Vector3[vertexCount];
        _bindBitangents = new Vector3[vertexCount];
        _skeletonBones = new int[vertexCount * InfluencesPerVertex];
        _weights = new float[vertexCount * InfluencesPerVertex];
        _skinned = new GrVertexPositionTangentFrameTexture[vertexCount];
        _morphTargets = mesh.MorphTargets;
        _morphedPositions = _morphTargets.Count > 0 ? new Vector3[vertexCount] : [];
        _morphedNormals = _morphTargets.Count > 0 ? new Vector3[vertexCount] : [];

        for (var vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
        {
            var vertex = mesh.Vertices[vertexIndex];
            _bindPositions[vertexIndex] = vertex.Position;
            _bindNormals[vertexIndex] = vertex.Normal;
            _bindTangents[vertexIndex] = new Vector3(vertex.Tangent.X, vertex.Tangent.Y, vertex.Tangent.Z);
            _bindBitangents[vertexIndex] = Vector3.Cross(vertex.Normal, _bindTangents[vertexIndex]) * (vertex.Tangent.W < 0f ? -1f : 1f);
            _skinned[vertexIndex].TextureCoordinate = new Vector2(vertex.Uv0.X, vertex.Uv0.Y);
            MapInfluences(mesh, vertex, vertexIndex * InfluencesPerVertex, skeletonBoneOf);
        }

        _skinnedOnGpuVertices = GrBuffers.Vertex<GrVertexSkinned>(device, SkinnedOnGpuVertices(), "SkinnedMeshBindVertices");
        _indices = GrBuffers.Index(device, mesh.Indices, "SkinnedMeshIndices");
        _indexCount = mesh.Indices.Length;
        _boneBounds = new (Vector3, Vector3, int)[mesh.Bounds.Count];
        for (var boundIndex = 0; boundIndex < _boneBounds.Length; boundIndex++)
        {
            var bound = mesh.Bounds[boundIndex];
            var bone = boundIndex < mesh.BoneBindings.Count ? skeletonBoneOf(mesh.BoneBindings[boundIndex]) : -1;
            _boneBounds[boundIndex] = (bound.Center, bound.BoxExtent, bone);
        }
        UpdateBounds([]);
    }

    public static GrSkinnedMesh Upload(GraphicsDevice device, Gw2Mesh mesh, Func<long, int> skeletonBoneOf, Texture2D atlas, Texture2D normalAtlas) =>
        new(device, mesh, skeletonBoneOf, atlas, normalAtlas);

    public Texture2D Atlas { get; }

    public Texture2D NormalAtlas { get; }

    public bool HasBounds => _boneBounds.Length != 0;

    public Vector3 LocalMinimum { get; private set; }

    public Vector3 LocalMaximum { get; private set; }

    public VertexLayout VertexLayout => IsSkinnedOnGpu ? GrVertexSkinned.Layout : GrVertexPositionTangentFrameTexture.Layout;

    public bool IsSkinnedOnGpu { get; private set; }

    public int PaletteOffset { get; private set; }

    public int PaletteBoneCount { get; private set; }

    public void Pose(Matrix4x4[] palette, int paletteOffset, IReadOnlyDictionary<long, float>? morphWeights)
    {
        if (morphWeights is not null && _morphTargets.Count > 0 && HasActiveMorph(morphWeights))
        {
            Skin(palette, morphWeights);
            return;
        }
        IsSkinnedOnGpu = true;
        PaletteOffset = paletteOffset;
        PaletteBoneCount = palette.Length;
        UpdateBounds(palette);
    }

    private bool HasActiveMorph(IReadOnlyDictionary<long, float> morphWeights)
    {
        foreach (var target in _morphTargets)
            if (morphWeights.TryGetValue(target.Mesh, out var weight) && MathF.Abs(weight) >= 1e-6f) return true;
        return false;
    }

    private GrVertexSkinned[] SkinnedOnGpuVertices()
    {
        var vertices = new GrVertexSkinned[_skinned.Length];
        for (var vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
        {
            var influence = vertexIndex * InfluencesPerVertex;
            vertices[vertexIndex] = new GrVertexSkinned
            {
                Position = _bindPositions[vertexIndex],
                Normal = _bindNormals[vertexIndex],
                Tangent = _bindTangents[vertexIndex],
                Bitangent = _bindBitangents[vertexIndex],
                TextureCoordinate = _skinned[vertexIndex].TextureCoordinate,
                Bone0 = (short)_skeletonBones[influence],
                Bone1 = (short)_skeletonBones[influence + 1],
                Bone2 = (short)_skeletonBones[influence + 2],
                Bone3 = (short)_skeletonBones[influence + 3],
                Weights = new Vector4(_weights[influence], _weights[influence + 1], _weights[influence + 2], _weights[influence + 3]),
            };
        }
        return vertices;
    }

    public Texture? MaterialTexture(int resourceIndex) => resourceIndex switch
    {
        0 => Atlas,
        1 => NormalAtlas,
        _ => null,
    };

    public Vector4 TintMultiply { get; set; } = Vector4.One;

    public Vector4 TintOffset { get; set; }

    public Vector4 TintContrast { get; set; } = new(0.5f);

    public float TintOpacity { get; set; } = 1f;

    public void Skin(Matrix4x4[] palette) => Skin(palette, null);

    public void Skin(Matrix4x4[] palette, IReadOnlyDictionary<long, float>? morphWeights)
    {
        IsSkinnedOnGpu = false;
        var positions = _bindPositions;
        var normals = _bindNormals;
        if (morphWeights is not null && _morphTargets.Count > 0 && Morph(morphWeights))
        {
            positions = _morphedPositions;
            normals = _morphedNormals;
        }
        for (var vertexIndex = 0; vertexIndex < _skinned.Length; vertexIndex++)
        {
            var position = Vector3.Zero;
            var normal = Vector3.Zero;
            var tangent = Vector3.Zero;
            var bitangent = Vector3.Zero;
            var influence = vertexIndex * InfluencesPerVertex;
            for (var slot = 0; slot < InfluencesPerVertex; slot++)
            {
                var weight = _weights[influence + slot];
                var bone = _skeletonBones[influence + slot];
                if (weight <= 0f || bone < 0 || bone >= palette.Length) continue;
                position += weight * Vector3.Transform(positions[vertexIndex], palette[bone]);
                normal += weight * Vector3.TransformNormal(normals[vertexIndex], palette[bone]);
                tangent += weight * Vector3.TransformNormal(_bindTangents[vertexIndex], palette[bone]);
                bitangent += weight * Vector3.TransformNormal(_bindBitangents[vertexIndex], palette[bone]);
            }

            _skinned[vertexIndex].Position = new Vector3(position.X, position.Y, position.Z);
            _skinned[vertexIndex].Normal = new Vector3(normal.X, normal.Y, normal.Z);
            _skinned[vertexIndex].Tangent = new Vector3(tangent.X, tangent.Y, tangent.Z);
            _skinned[vertexIndex].Bitangent = new Vector3(bitangent.X, bitangent.Y, bitangent.Z);
        }

        UpdateBounds(palette);
    }

    private bool Morph(IReadOnlyDictionary<long, float> morphWeights)
    {
        var morphed = false;
        foreach (var target in _morphTargets)
        {
            if (!morphWeights.TryGetValue(target.Mesh, out var weight) || MathF.Abs(weight) < 1e-6f) continue;
            if (!morphed)
            {
                _bindPositions.CopyTo(_morphedPositions, 0);
                _bindNormals.CopyTo(_morphedNormals, 0);
                morphed = true;
            }
            foreach (var vertex in target.Positions)
                if (vertex.Index < _morphedPositions.Length) _morphedPositions[vertex.Index] += weight * vertex.Position;
            foreach (var vertex in target.Normals)
                if (vertex.Index < _morphedNormals.Length) _morphedNormals[vertex.Index] += weight * vertex.Position;
        }
        return morphed;
    }

    public void Draw(GraphicsDevice device)
    {
        if (IsSkinnedOnGpu) device.SetVertexBuffer(_skinnedOnGpuVertices);
        else device.SetVertexData<GrVertexPositionTangentFrameTexture>(_skinned);
        device.SetIndexBuffer(_indices);
        device.DrawIndexed(_indexCount);
    }

    public void Dispose()
    {
        _indices.Dispose();
        _skinnedOnGpuVertices.Dispose();
    }

    private void UpdateBounds(Matrix4x4[] palette)
    {
        var minimum = new Vector3(float.MaxValue);
        var maximum = new Vector3(float.MinValue);
        foreach (var (center, extent, bone) in _boneBounds)
        {
            var boneMinimum = center - extent;
            var boneMaximum = center + extent;
            if (bone >= 0 && bone < palette.Length)
            {
                var posed = GrWorldBounds.Of(boneMinimum, boneMaximum, palette[bone]);
                boneMinimum = new Vector3(posed.Min.X, posed.Min.Y, posed.Min.Z);
                boneMaximum = new Vector3(posed.Max.X, posed.Max.Y, posed.Max.Z);
            }
            minimum = Vector3.Min(minimum, boneMinimum);
            maximum = Vector3.Max(maximum, boneMaximum);
        }
        LocalMinimum = HasBounds ? minimum : Vector3.Zero;
        LocalMaximum = HasBounds ? maximum : Vector3.Zero;
    }

    private void MapInfluences(Gw2Mesh mesh, Gw2Vertex vertex, int influence, Func<long, int> skeletonBoneOf)
    {
        var total = 0f;
        for (var slot = 0; slot < InfluencesPerVertex; slot++)
        {
            var paletteIndex = vertex.BoneIndex(slot);
            var bone = vertex.HasSkin && paletteIndex < mesh.BoneBindings.Count ? skeletonBoneOf(mesh.BoneBindings[paletteIndex]) : -1;
            var weight = bone >= 0 ? vertex.BoneWeight(slot) : 0f;
            _skeletonBones[influence + slot] = bone;
            _weights[influence + slot] = weight;
            total += weight;
        }

        if (total > 1e-6f)
        {
            for (var slot = 0; slot < InfluencesPerVertex; slot++) _weights[influence + slot] /= total;
            return;
        }

        var rigidBone = mesh.BoneBindings.Count > 0 ? skeletonBoneOf(mesh.BoneBindings[0]) : -1;
        _skeletonBones[influence] = rigidBone >= 0 ? rigidBone : 0;
        _weights[influence] = 1f;
    }
}
