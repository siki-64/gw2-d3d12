using System;
using System.Numerics;
using Crystal.Graphics;

namespace Gw2.Client.Graphics;

public enum GrHemicubeFace
{
    North,
    East,
    South,
    West,
    Top,
}

public readonly record struct GrHemicubeFaceTextures(Texture2D NorthEast, Texture2D SouthWest, Texture2D Top)
{
    public Texture2D Of(GrHemicubeFace face) => face switch
    {
        GrHemicubeFace.North or GrHemicubeFace.East => NorthEast,
        GrHemicubeFace.South or GrHemicubeFace.West => SouthWest,
        _ => Top,
    };
}

public readonly record struct GrSkyConstants(Vector4 FogColorFar, Vector4 ShRed, Vector4 ShGreen, Vector4 ShBlue, Vector4 SkyParameters, Vector4 SunDirection, Vector4 SunColor);

public sealed class GrHemicubeSkybox : IDisposable
{
    public const float DomeScale = 100f;
    private const float FaceSizeOfViewDistance = 0.4f;
    private const float TexelGutter = 10f;
    private const float HorizonV = 0.5f;
    private const float BelowHorizonDepth = 1f;
    private static readonly ushort[] SideIndices = [0, 1, 2, 0, 2, 3, 1, 2, 4, 1, 4, 5];
    private static readonly ushort[] TopIndices = [0, 1, 2, 0, 2, 3];

    private readonly GrDevice _device;
    private readonly ShaderProgram _program;
    private readonly GraphicsBuffer[] _faceVertices;
    private readonly GraphicsBuffer _sideIndexBuffer;
    private readonly GraphicsBuffer _topIndexBuffer;

    public GrHemicubeSkybox(GrDevice device, float viewDistance)
    {
        _device = device;
        _program = device.LoadShader("GrHemicubeSkybox").GetProgram("Sky");
        var faceSize = viewDistance * FaceSizeOfViewDistance;
        var gutter = TexelGutter / faceSize;
        _faceVertices = new GraphicsBuffer[Enum.GetValues<GrHemicubeFace>().Length];
        foreach (var face in Enum.GetValues<GrHemicubeFace>())
            _faceVertices[(int)face] = GrBuffers.Vertex<VertexPositionTexture>(device.GraphicsDevice, FaceVertices(face, faceSize, gutter), $"Hemicube{face}Vertices");

        _sideIndexBuffer = GrBuffers.Index(device.GraphicsDevice, SideIndices, "HemicubeSideIndices");
        _topIndexBuffer = GrBuffers.Index(device.GraphicsDevice, TopIndices, "HemicubeTopIndices");
    }

    public static VertexPositionTexture[] FaceVertices(GrHemicubeFace face, float size, float gutter)
    {
        var low = gutter;
        var high = 1f - gutter;
        return face switch
        {
            GrHemicubeFace.North => Side(
                new(-size, size), new(size, size),
                new(high, high), new(high, HorizonV), new(low, HorizonV), new(low, high), size),
            GrHemicubeFace.East => Side(
                new(size, size), new(size, -size),
                new(low, low), new(low, HorizonV), new(high, HorizonV), new(high, low), size),
            GrHemicubeFace.South => Side(
                new(size, -size), new(-size, -size),
                new(high, high), new(high, HorizonV), new(low, HorizonV), new(low, high), size),
            GrHemicubeFace.West => Side(
                new(-size, -size), new(-size, size),
                new(low, low), new(low, HorizonV), new(high, HorizonV), new(high, low), size),
            _ =>
            [
                new(new Vector3(-size, size, -size), new Vector2(low, high)),
                new(new Vector3(size, size, -size), new Vector2(high, high)),
                new(new Vector3(size, -size, -size), new Vector2(high, low)),
                new(new Vector3(-size, -size, -size), new Vector2(low, low)),
            ],
        };
    }

    private static VertexPositionTexture[] Side(Vector2 start, Vector2 end, Vector2 startTop, Vector2 startHorizon, Vector2 endHorizon, Vector2 endTop, float size) =>
    [
        new(new Vector3(start, -size), startTop),
        new(new Vector3(start, 0f), startHorizon),
        new(new Vector3(end, 0f), endHorizon),
        new(new Vector3(end, -size), endTop),
        new(new Vector3(end, BelowHorizonDepth), endHorizon),
        new(new Vector3(start, BelowHorizonDepth), startHorizon),
    ];

    public void Draw(Matrix4x4 view, Matrix4x4 projection, Vector3 eye, float verticalOffset, GrHemicubeFaceTextures from, GrHemicubeFaceTextures toward, GrSkyConstants constants)
    {
        var world = Matrix4x4.CreateScale(DomeScale) * Matrix4x4.CreateTranslation(eye.X, eye.Y, -verticalOffset);
        var viewProjection = view * projection;
        var parameters = _program.Parameters;
        parameters["CameraPosition"]?.SetValue(new Vector4(eye.X, eye.Y, eye.Z, 1f));
        parameters["ViewProjection"]?.SetValue(GrMatrix.ToRegisterRows(viewProjection));
        parameters["World"]?.SetValue(GrMatrix.ToRegisterRows(world));
        parameters["WorldViewProjection"]?.SetValue(GrMatrix.ToRegisterRows(world * viewProjection));
        parameters["FogColorFar"]?.SetValue(constants.FogColorFar);
        parameters["shRed"]?.SetValue(constants.ShRed);
        parameters["shGreen"]?.SetValue(constants.ShGreen);
        parameters["shBlue"]?.SetValue(constants.ShBlue);
        parameters["skypara"]?.SetValue(constants.SkyParameters);
        parameters["sunDir"]?.SetValue(constants.SunDirection);
        parameters["sunClr"]?.SetValue(constants.SunColor);
        parameters["ss00Sampler"]?.SetValue(_device.Sampler(SamplerDescription.LinearClamp));
        parameters["ss10Sampler"]?.SetValue(_device.Sampler(SamplerDescription.LinearClamp));

        var graphicsDevice = _device.GraphicsDevice;
        foreach (var face in Enum.GetValues<GrHemicubeFace>())
        {
            parameters["ss00"]?.SetValue(toward.Of(face));
            parameters["ss10"]?.SetValue(from.Of(face));
            var top = face == GrHemicubeFace.Top;
            _device.ApplyPipeline(_program, BlendState.Opaque, DepthStencilState.None, RasterizerState.CullNone, VertexPositionTexture.Layout);
            graphicsDevice.SetVertexBuffer(_faceVertices[(int)face]);
            graphicsDevice.SetIndexBuffer(top ? _topIndexBuffer : _sideIndexBuffer);
            graphicsDevice.DrawIndexed(top ? TopIndices.Length : SideIndices.Length);
        }
    }

    public void Dispose()
    {
        foreach (var vertices in _faceVertices) vertices.Dispose();
        _sideIndexBuffer.Dispose();
        _topIndexBuffer.Dispose();
    }
}
