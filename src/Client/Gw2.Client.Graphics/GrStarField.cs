using System;
using System.Numerics;
using Crystal;
using Crystal.Graphics;
using Gw2.Dat.Content;

namespace Gw2.Client.Graphics;

public readonly record struct GrStarFieldConstants(float HazeBottom, float HazeFalloff, float HazeDensity, float StarDensity, Vector4 Time);

public sealed class GrStarField : IDisposable
{
    private readonly GrDevice _device;
    private readonly ShaderProgram _program;
    private readonly Texture2D _atlas;
    private readonly GraphicsBuffer _vertices;
    private readonly GraphicsBuffer _indices;
    private readonly int _indexCount;

    public GrStarField(GrDevice device, Gw2StarFieldMesh mesh, Texture2D atlas)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(mesh);
        _device = device;
        _program = device.LoadShader("GrStarField").GetProgram("StarField");
        _atlas = atlas ?? throw new ArgumentNullException(nameof(atlas));
        var vertices = Array.ConvertAll(mesh.Vertices, vertex => new VertexPositionColorTexture(
            new Vector3(vertex.Position.X, vertex.Position.Y, vertex.Position.Z),
            Color.FromPacked(vertex.Color),
            new Vector2(vertex.TextureCoordinate.X, vertex.TextureCoordinate.Y)));
        _vertices = GrBuffers.Vertex<VertexPositionColorTexture>(device.GraphicsDevice, vertices, "StarFieldVertices");
        _indices = GrBuffers.Index(device.GraphicsDevice, Array.ConvertAll(mesh.Indices, index => (ushort)index), "StarFieldIndices");
        _indexCount = mesh.Indices.Length;
    }

    public void Draw(Matrix4x4 view, Matrix4x4 projection, Vector3 eye, GrStarFieldConstants constants)
    {
        if (constants.StarDensity == 0f) return;
        var world = Matrix4x4.CreateTranslation(eye);
        var parameters = _program.Parameters;
        parameters["Time"]?.SetValue(constants.Time);
        parameters["World"]?.SetValue(GrMatrix.ToRegisterRows(world));
        parameters["WorldViewProjection"]?.SetValue(GrMatrix.ToRegisterRows(world * view * projection));
        parameters["skypara"]?.SetValue(new Vector4(constants.HazeBottom, constants.HazeFalloff, constants.HazeDensity, constants.StarDensity));
        parameters["ss00"]?.SetValue(_atlas);
        parameters["ss00Sampler"]?.SetValue(_device.Sampler(SamplerDescription.LinearClamp));

        var graphicsDevice = _device.GraphicsDevice;
        _device.ApplyPipeline(_program, GrBlendStates.AdditiveOneOne, DepthStencilState.None, RasterizerState.CullNone, VertexPositionColorTexture.Layout);
        graphicsDevice.SetVertexBuffer(_vertices);
        graphicsDevice.SetIndexBuffer(_indices);
        graphicsDevice.DrawIndexed(_indexCount);
    }

    public void Dispose()
    {
        _vertices.Dispose();
        _indices.Dispose();
    }
}
