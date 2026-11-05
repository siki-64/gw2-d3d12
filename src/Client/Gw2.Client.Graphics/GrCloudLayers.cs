using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Crystal;
using Crystal.Graphics;

namespace Gw2.Client.Graphics;

public sealed record GrCloudLayerShape(float Altitude, float CutOut, float Depth, float Extent, float Scale, float FadeEnd, float FadeWidth, Texture2D Texture);

public readonly record struct GrCloudLayerLook(float Brightness, float Density, float Haze, float LightIntensity, Vector2 Velocity);

public readonly record struct GrCloudConstants(
    Vector4 FogColorNearMinusFar,
    Vector4 FogColorFar,
    Vector4 FogParam0,
    Vector4 ShRed,
    Vector4 ShGreen,
    Vector4 ShBlue,
    Vector3 SunDirection,
    Vector3 SunColor,
    float SkyLightIntensity,
    float Intensity);

public sealed class GrCloudLayers : IDisposable
{
    public const int GridCells = 10;
    private const float GridHalfSize = 0.5f;
    private const float EdgeFadePower = 4f;
    private const float ExtentScale = 0.4075f;
    private const float ExtentBias = 0.5925f;

    private readonly GrDevice _device;
    private readonly ShaderProgram _program;
    private readonly GraphicsBuffer _vertices;
    private readonly GraphicsBuffer _indices;
    private readonly int _indexCount;
    private readonly GrCloudLayerShape[] _shapes;
    private readonly Vector2[] _scroll;
    private readonly float _viewDistance;
    private readonly Texture2D _farDepth;

    public GrCloudLayers(GrDevice device, IReadOnlyList<GrCloudLayerShape> shapes, float viewDistance)
    {
        _device = device;
        _program = device.LoadShader("GrCloudLayer").GetProgram("Cloud");
        _shapes = shapes.ToArray();
        _scroll = new Vector2[_shapes.Length];
        _viewDistance = viewDistance;
        _vertices = GrBuffers.Vertex<VertexPositionColorTexture>(device.GraphicsDevice, GridVertices(), "CloudGridVertices");
        var indices = GridIndices();
        _indices = GrBuffers.Index(device.GraphicsDevice, indices, "CloudGridIndices");
        _indexCount = indices.Length;
        _farDepth = new Texture2D(device.GraphicsDevice, new TextureDescription { Width = 1, Height = 1, Format = SurfaceFormat.R32Float, Name = "CloudFarDepth" });
        _farDepth.SetData<float>([1f]);
    }
    public static VertexPositionColorTexture[] GridVertices()
    {
        var vertices = new VertexPositionColorTexture[(GridCells + 1) * (GridCells + 1)];
        var step = 2f * GridHalfSize / GridCells;
        for (var row = 0; row <= GridCells; row++)
        for (var column = 0; column <= GridCells; column++)
        {
            var x = column * step - GridHalfSize;
            var y = row * step - GridHalfSize;
            var radius = Math.Clamp(MathF.Sqrt(x * x + y * y), 0f, GridHalfSize);
            var edgeFade = (byte)(int)((1f - MathF.Pow(radius / GridHalfSize, EdgeFadePower)) * 255f);
            vertices[row * (GridCells + 1) + column] = new(new Vector3(x, y, 0f), new Color(edgeFade, edgeFade, edgeFade, edgeFade), new Vector2((float)column / GridCells, (float)row / GridCells));
        }

        return vertices;
    }

    public static ushort[] GridIndices()
    {
        var indices = new List<ushort>(6 * GridCells * GridCells);
        for (var row = 0; row < GridCells; row++)
        for (var column = 0; column < GridCells; column++)
        {
            var current = row * (GridCells + 1) + column;
            var next = current + GridCells + 1;
            indices.AddRange([(ushort)current, (ushort)(current + 1), (ushort)next, (ushort)(current + 1), (ushort)next, (ushort)(next + 1)]);
        }

        return indices.ToArray();
    }

    public float PlaneScale(GrCloudLayerShape shape) => ExtentFactor(shape) * 2f * _viewDistance;

    private static float ExtentFactor(GrCloudLayerShape shape) => shape.Extent * ExtentScale + ExtentBias;

    public void Draw(Matrix4x4 view, Matrix4x4 projection, Vector3 eye, float elapsedSeconds, IReadOnlyList<GrCloudLayerLook> looks, GrCloudConstants constants, Texture2D? sceneDepth, int screenWidth, int screenHeight, float cameraNear, float cameraFar)
    {
        var parameters = _program.Parameters;
        var viewProjection = view * projection;
        var sunDirection = constants.SunDirection.LengthSquared() >= 1e-12f ? Vector3.Normalize(constants.SunDirection) : Vector3.Zero;
        parameters["CameraPosition"]?.SetValue(new Vector4(eye.X, eye.Y, eye.Z, 1f));
        parameters["FogColorNearMinusFar"]?.SetValue(constants.FogColorNearMinusFar);
        parameters["FogColorFar"]?.SetValue(constants.FogColorFar);
        parameters["FogParam0"]?.SetValue(constants.FogParam0);
        parameters["ScreenDims"]?.SetValue(GrEngineConstants.ScreenDims(screenWidth, screenHeight, cameraNear, cameraFar));
        parameters["TexelOffset"]?.SetValue(GrEngineConstants.TexelOffset);
        parameters["shRed"]?.SetValue(constants.ShRed);
        parameters["shGreen"]?.SetValue(constants.ShGreen);
        parameters["shBlue"]?.SetValue(constants.ShBlue);
        parameters["sunDir"]?.SetValue(new Vector4(sunDirection.X, sunDirection.Y, sunDirection.Z, 0f));
        parameters["ssNoiseDepth"]?.SetValue(sceneDepth ?? _farDepth);
        parameters["ss0Sampler"]?.SetValue(_device.Sampler(SamplerDescription.LinearWrap));
        parameters["ssNoiseDepthSampler"]?.SetValue(_device.Sampler(SamplerDescription.PointClamp));

        var graphicsDevice = _device.GraphicsDevice;
        foreach (var index in FarthestFirst(eye.Z))
        {
            var shape = _shapes[index];
            var look = looks[index];
            var planeScale = PlaneScale(shape);
            _scroll[index] += look.Velocity * elapsedSeconds / planeScale;
            var world = Matrix4x4.CreateScale(planeScale, planeScale, 1f) * Matrix4x4.CreateTranslation(eye.X, eye.Y, -shape.Altitude);
            parameters["World"]?.SetValue(GrMatrix.ToRegisterRows(world));
            parameters["WorldView"]?.SetValue(GrMatrix.ToRegisterRows(world * view));
            parameters["WorldViewProjection"]?.SetValue(GrMatrix.ToRegisterRows(world * viewProjection));
            parameters["uvTrans"]?.SetValue(new Vector4(eye.X / planeScale + _scroll[index].X, eye.Y / planeScale + _scroll[index].Y, ExtentFactor(shape) * shape.Scale, look.Brightness));
            parameters["cldPara"]?.SetValue(new Vector4(shape.Depth, look.Density * constants.Intensity, look.Haze, shape.CutOut));
            parameters["cldParb"]?.SetValue(new Vector4(shape.FadeEnd, shape.FadeEnd + shape.FadeWidth, 0f, 0f));
            parameters["sunClr"]?.SetValue(new Vector4(constants.SunColor.X, constants.SunColor.Y, constants.SunColor.Z, look.LightIntensity * constants.SkyLightIntensity));
            parameters["ss0"]?.SetValue(shape.Texture);
            _device.ApplyPipeline(_program, BlendState.NonPremultiplied, DepthStencilState.None, RasterizerState.CullNone, VertexPositionColorTexture.Layout);
            graphicsDevice.SetVertexBuffer(_vertices);
            graphicsDevice.SetIndexBuffer(_indices);
            graphicsDevice.DrawIndexed(_indexCount);
        }
    }

    private IEnumerable<int> FarthestFirst(float cameraHeight) =>
        Enumerable.Range(0, _shapes.Length).OrderByDescending(index => MathF.Abs(MathF.Abs(cameraHeight) - _shapes[index].Altitude));

    public void Dispose()
    {
        _vertices.Dispose();
        _indices.Dispose();
        _farDepth.Dispose();
    }
}
