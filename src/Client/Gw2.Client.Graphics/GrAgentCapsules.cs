using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Crystal.Graphics;

namespace Gw2.Client.Graphics;

public sealed class GrAgentCapsules : IDisposable
{
    private const int Segments = 16;
    private const int HemisphereRings = 6;
    private const int InstanceSlot = 1;

    private static readonly VertexLayout Layout = new(
        new VertexElement("POSITION", 0, VertexElementFormat.Vector3, 0),
        new VertexElement("NORMAL", 0, VertexElementFormat.Vector3, 12),
        new VertexElement("TEXCOORD", 0, VertexElementFormat.Vector2, 24),
        GrVertexInstance.ElementsAt(InstanceSlot)[0],
        GrVertexInstance.ElementsAt(InstanceSlot)[1],
        GrVertexInstance.ElementsAt(InstanceSlot)[2],
        GrVertexInstance.ElementsAt(InstanceSlot)[3]);

    private readonly GrDevice _device;
    private readonly ShaderProgram _program;
    private readonly Dictionary<CapsuleShape, CapsuleMesh> _meshes = [];
    private readonly Dictionary<(CapsuleShape Shape, Vector4 Color), List<GrVertexInstance>> _instances = [];

    public GrAgentCapsules(GrDevice device)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _program = device.LoadShader("GrDebugSurface").GetProgram("InstancedClay");
    }

    public void Clear()
    {
        foreach (var instances in _instances.Values) instances.Clear();
    }

    public void Add(Vector3 bottomInGameUnits, float heightInGameUnits, float radiusInGameUnits, Vector4 color)
    {
        if (radiusInGameUnits <= 0f || heightInGameUnits <= 0f) return;
        var key = (new CapsuleShape(heightInGameUnits, radiusInGameUnits), color);
        if (!_instances.TryGetValue(key, out var instances)) _instances[key] = instances = [];
        instances.Add(GrVertexInstance.From(Matrix4x4.CreateTranslation(bottomInGameUnits), Vector4.One));
    }

    public void Draw(Matrix4x4 view, Matrix4x4 viewProjection, Vector4 sunDirection)
    {
        var graphicsDevice = _device.GraphicsDevice;
        _program.Parameters["ViewProjection"]?.SetValue(viewProjection);
        _program.Parameters["View"]?.SetValue(view);
        _program.Parameters["SunDirection"]?.SetValue(sunDirection);
        foreach (var ((shape, color), instances) in _instances)
        {
            if (instances.Count == 0) continue;
            if (!_meshes.TryGetValue(shape, out var mesh)) _meshes[shape] = mesh = CapsuleMesh.Build(graphicsDevice, shape);
            _program.Parameters["ModelColor"]?.SetValue(color);
            _device.ApplyPipeline(_program, BlendState.Opaque, DepthStencilState.Default, RasterizerState.CullNone, Layout);
            graphicsDevice.SetVertexBuffer(mesh.Vertices);
            graphicsDevice.SetVertexData<GrVertexInstance>(CollectionsMarshal.AsSpan(instances), InstanceSlot);
            graphicsDevice.SetIndexBuffer(mesh.Indices);
            graphicsDevice.DrawIndexedInstanced(mesh.IndexCount, instances.Count);
        }
    }

    public void Dispose()
    {
        foreach (var mesh in _meshes.Values)
        {
            mesh.Vertices.Dispose();
            mesh.Indices.Dispose();
        }
        _meshes.Clear();
    }

    private readonly record struct CapsuleShape(float Height, float Radius);

    private sealed record CapsuleMesh(GraphicsBuffer Vertices, GraphicsBuffer Indices, int IndexCount)
    {
        internal static CapsuleMesh Build(GraphicsDevice device, CapsuleShape shape)
        {
            var radius = shape.Radius;
            var lowerCenter = radius;
            var upperCenter = MathF.Max(shape.Height - radius, radius);
            const int ringCount = 2 * HemisphereRings + 2;
            const int ringVertexCount = Segments + 1;
            var vertices = new VertexPositionNormalTexture[ringCount * ringVertexCount];
            for (var ring = 0; ring < ringCount; ring++)
            {
                var isLower = ring <= HemisphereRings;
                var latitude = isLower
                    ? -MathF.PI / 2f + ring * (MathF.PI / 2f) / HemisphereRings
                    : (ring - HemisphereRings - 1) * (MathF.PI / 2f) / HemisphereRings;
                var heightAboveBottom = (isLower ? lowerCenter : upperCenter) + radius * MathF.Sin(latitude);
                var ringRadius = radius * MathF.Cos(latitude);
                for (var segment = 0; segment < ringVertexCount; segment++)
                {
                    var longitude = segment * 2f * MathF.PI / Segments;
                    var (sin, cos) = MathF.SinCos(longitude);
                    var normal = new Vector3(MathF.Cos(latitude) * cos, MathF.Cos(latitude) * sin, -MathF.Sin(latitude));
                    vertices[ring * ringVertexCount + segment] = new VertexPositionNormalTexture(
                        new Vector3(ringRadius * cos, ringRadius * sin, -heightAboveBottom),
                        normal,
                        new Vector2((float)segment / Segments, (float)ring / (ringCount - 1)));
                }
            }
            var indices = new ushort[(ringCount - 1) * Segments * 6];
            var next = 0;
            for (var ring = 0; ring < ringCount - 1; ring++)
                for (var segment = 0; segment < Segments; segment++)
                {
                    var corner = (ushort)(ring * ringVertexCount + segment);
                    var above = (ushort)(corner + ringVertexCount);
                    indices[next++] = corner;
                    indices[next++] = above;
                    indices[next++] = (ushort)(corner + 1);
                    indices[next++] = (ushort)(corner + 1);
                    indices[next++] = above;
                    indices[next++] = (ushort)(above + 1);
                }
            return new CapsuleMesh(
                GrBuffers.Vertex<VertexPositionNormalTexture>(device, vertices, "AgentCapsuleVertices"),
                GrBuffers.Index(device, indices, "AgentCapsuleIndices"),
                indices.Length);
        }
    }
}
