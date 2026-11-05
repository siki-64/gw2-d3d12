using System;
using System.Collections.Generic;
using System.Numerics;
using Gw2.Dat.Content;
using Crystal;
using Crystal.Graphics;

namespace Gw2.Client.Graphics;

public delegate (Vector3 Minimum, Vector3 Maximum) GrWaterVisibilityBox(Vector3 eye, float farClip);

public delegate byte GrWaterFollowingAlpha(Vector3 localPosition, Vector3 worldPosition);

public delegate IEnumerable<(Vector3 Minimum, Vector3 Maximum)> GrWaterShownBoxes(Vector3 eye, float farClip);

public delegate void GrWaterFollowingGrid(Vector3 eye, Vector3[] positions, Vector3[] normals, byte[] alphas);

public sealed record GrWaterSurface(bool FollowsEye, Predicate<Vector3> IsBelowWater, GrWaterVisibilityBox VisibilityBox, Vector3[] Positions, byte[] Alphas, int[] Indices, GrWaterFollowingAlpha? FollowingAlpha = null, GrWaterShownBoxes? ShownBoxes = null, GrWaterFollowingGrid? FollowingGrid = null);

public sealed class GrWaterMaterial(uint materialFileId, IReadOnlyList<Texture?> textures)
{
    public uint MaterialFileId { get; } = materialFileId;

    public Gw2BuiltInWaterMaterial? BuiltInMaterial { get; init; }

    public IReadOnlyList<Texture?> Textures { get; } = textures;

    public IReadOnlyDictionary<uint, Vector4> Constants { get; set; } = GrMaterialConstants.None;
}

public sealed class GrWaterMesh : IGrMaterialMesh, IDisposable
{
    private static readonly Vector3 SurfaceNormal = new(0f, 0f, -1f);

    private readonly GraphicsBuffer _vertices;
    private readonly GrVertexWater[] _vertexData;
    private readonly GraphicsBuffer _indices;
    private readonly int _indexCount;
    private Vector3? _followedOffset;
    private Vector3[]? _gridPositions;
    private Vector3[]? _gridNormals;
    private byte[]? _gridAlphas;

    public GrWaterMesh(GraphicsDevice device, GrWaterSurface surface)
    {
        Surface = surface;
        _vertexData = new GrVertexWater[surface.Positions.Length];
        for (var index = 0; index < _vertexData.Length; index++)
        {
            var position = surface.Positions[index];
            _vertexData[index] = new GrVertexWater { Position = new Vector3(position.X, position.Y, position.Z), Normal = SurfaceNormal, Color = new Color((byte)0, (byte)0, (byte)0, surface.Alphas[index]) };
        }
        _vertices = GrBuffers.Vertex<GrVertexWater>(device, _vertexData, "WaterVertices");
        _indices = GrBuffers.Index(device, surface.Indices, "WaterIndices");
        _indexCount = surface.Indices.Length;
    }

    public GrWaterSurface Surface { get; }

    public VertexLayout VertexLayout => GrVertexWater.Layout;

    public GrWaterMaterial? Material { get; set; }

    public Texture? MaterialTexture(int resourceIndex) =>
        Material is { } material && resourceIndex >= 0 && resourceIndex < material.Textures.Count ? material.Textures[resourceIndex] : null;

    public Texture? SamplerTexture(GrArchiveSampler sampler) => MaterialTexture(sampler.Slot);

    public IReadOnlyDictionary<uint, Vector4> MaterialConstants => Material?.Constants ?? GrMaterialConstants.None;

    public void Follow(Vector3 offset)
    {
        if (_followedOffset == offset) return;
        _followedOffset = offset;
        for (var index = 0; index < _vertexData.Length; index++)
        {
            var position = Surface.Positions[index];
            var worldPosition = position + offset;
            _vertexData[index].Position = new Vector3(worldPosition.X, worldPosition.Y, worldPosition.Z);
            if (Surface.FollowingAlpha is { } followingAlpha) _vertexData[index].Color = new Color((byte)0, (byte)0, (byte)0, followingAlpha(position, worldPosition));
        }
        _vertices.SetData<GrVertexWater>(_vertexData);
    }

    public void FollowGrid(Vector3 eye)
    {
        _gridPositions ??= new Vector3[_vertexData.Length];
        _gridNormals ??= new Vector3[_vertexData.Length];
        _gridAlphas ??= new byte[_vertexData.Length];
        Surface.FollowingGrid!(eye, _gridPositions, _gridNormals, _gridAlphas);
        for (var index = 0; index < _vertexData.Length; index++)
        {
            var position = _gridPositions[index];
            _vertexData[index].Position = new Vector3(position.X, position.Y, position.Z);
            var normal = _gridNormals[index];
            _vertexData[index].Normal = new Vector3(normal.X, normal.Y, normal.Z);
            _vertexData[index].Color = new Color((byte)0, (byte)0, (byte)0, _gridAlphas[index]);
        }
        _vertices.SetData<GrVertexWater>(_vertexData);
    }

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

public sealed class GrWater : IDisposable
{
    private readonly GraphicsDevice _graphicsDevice;
    private readonly List<GrWaterMesh> _meshes = new();
    private readonly List<(GrWaterMesh Mesh, Matrix4x4 World)> _visible = new();
    private readonly List<Texture> _ownedTextures = new();
    private readonly float _globalSnap;

    public GrWater(GrDevice device, float globalSnap, GrWaterMaterial above, GrWaterMaterial below)
    {
        _graphicsDevice = device.GraphicsDevice;
        _globalSnap = globalSnap;
        Above = above;
        Below = below;
    }

    public GrWaterMaterial Above { get; }

    public GrWaterMaterial Below { get; }

    public bool CullsHiddenWater { get; set; } = true;

    public int SurfaceCount => _meshes.Count;

    public int VisibleSurfaceCount => _visible.Count;

    public void Own(Texture texture) => _ownedTextures.Add(texture);

    public void Add(GrWaterSurface surface)
    {
        if (surface.Indices.Length == 0) return;
        _meshes.Add(new GrWaterMesh(_graphicsDevice, surface));
    }

    public IReadOnlyList<(GrWaterMesh Mesh, Matrix4x4 World)> Visible(Matrix4x4 viewProjection, Vector3 eye, float farClip)
    {
        var frustum = new BoundingFrustum(viewProjection);
        _visible.Clear();
        foreach (var mesh in _meshes)
        {
            var (minimum, maximum) = mesh.Surface.VisibilityBox(eye, farClip);
            if (!Intersects(frustum, minimum, maximum)) continue;
            if (CullsHiddenWater && mesh.Surface.ShownBoxes is { } shownBoxes && !AnyIntersects(frustum, shownBoxes(eye, farClip))) continue;
            mesh.Material = mesh.Surface.IsBelowWater(eye) ? Below : Above;
            if (mesh.Surface.FollowingGrid is not null) mesh.FollowGrid(eye);
            else if (mesh.Surface.FollowsEye) mesh.Follow(new Vector3(MathF.Round(eye.X / _globalSnap) * _globalSnap, MathF.Round(eye.Y / _globalSnap) * _globalSnap, 0f));
            _visible.Add((mesh, Matrix4x4.Identity));
        }
        return _visible;
    }

    public float? ReflectionPlaneZ(Matrix4x4 viewProjection, Vector3 eye, float farClip)
    {
        var visible = Visible(viewProjection, eye, farClip);
        GrWaterMesh? nearest = null;
        var nearestDistance = float.MaxValue;
        foreach (var mesh in _meshes)
        {
            var distance = DistanceToSurface(mesh, eye, farClip);
            if (distance >= nearestDistance) continue;
            nearestDistance = distance;
            nearest = mesh;
        }
        if (nearest is null || nearest.Surface.IsBelowWater(eye)) return null;
        foreach (var (mesh, _) in visible)
            if (mesh == nearest) return SurfaceZ(nearest);
        return null;
    }

    private static bool Intersects(BoundingFrustum frustum, Vector3 minimum, Vector3 maximum) =>
        frustum.Intersects(new BoundingBox(minimum, maximum));

    private static bool AnyIntersects(BoundingFrustum frustum, IEnumerable<(Vector3 Minimum, Vector3 Maximum)> boxes)
    {
        foreach (var (minimum, maximum) in boxes)
            if (Intersects(frustum, minimum, maximum)) return true;
        return false;
    }

    private static float SurfaceZ(GrWaterMesh mesh) => mesh.Surface.Positions[0].Z;

    private static float DistanceToSurface(GrWaterMesh mesh, Vector3 eye, float farClip)
    {
        var (minimum, maximum) = mesh.Surface.VisibilityBox(eye, farClip);
        var outsideX = MathF.Max(0f, MathF.Max(minimum.X - eye.X, eye.X - maximum.X));
        var outsideY = MathF.Max(0f, MathF.Max(minimum.Y - eye.Y, eye.Y - maximum.Y));
        var heightAbove = SurfaceZ(mesh) - eye.Z;
        return MathF.Sqrt(outsideX * outsideX + outsideY * outsideY + heightAbove * heightAbove);
    }

    public void Dispose()
    {
        foreach (var mesh in _meshes) mesh.Dispose();
        foreach (var texture in _ownedTextures) texture.Dispose();
    }
}
