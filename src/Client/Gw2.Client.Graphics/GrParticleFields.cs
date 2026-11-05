using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Crystal;
using Crystal.Graphics;
using Gw2.Dat.Content;

namespace Gw2.Client.Graphics;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct GrParticleFieldVertex(Vector3 position, Color cornerAndPhase, Vector2 scale, Vector2 angleAndRotation, Vector2 frameAndCorner)
{
    public static readonly VertexLayout Layout = new(
        new VertexElement("POSITION", 0, VertexElementFormat.Vector3, 0),
        new VertexElement("COLOR", 0, VertexElementFormat.Color, 12),
        new VertexElement("TEXCOORD", 0, VertexElementFormat.Vector2, 16),
        new VertexElement("TEXCOORD", 1, VertexElementFormat.Vector2, 24),
        new VertexElement("TEXCOORD", 2, VertexElementFormat.Vector2, 32));

    public readonly Vector3 Position = position;
    public readonly Color CornerAndPhase = cornerAndPhase;
    public readonly Vector2 Scale = scale;
    public readonly Vector2 AngleAndRotation = angleAndRotation;
    public readonly Vector2 FrameAndCorner = frameAndCorner;
}

public sealed record GrParticleFieldCutout(Vector4 RowX, Vector4 RowY, Vector4 RowZ);

public sealed record GrParticleFieldDraw(GrParticleField Field, Gw2ParticleFieldConstants Constants, IReadOnlyList<GrParticleFieldCutout> Cutouts);

public sealed class GrParticleField : IDisposable
{
    private const int ShortIndexLimit = ushort.MaxValue + 1;

    private GrParticleField(GraphicsBuffer vertices, GraphicsBuffer indices, int indexCount, Texture2D texture, string programName, BlendState blendState)
    {
        Vertices = vertices;
        Indices = indices;
        IndexCount = indexCount;
        Texture = texture;
        ProgramName = programName;
        BlendState = blendState;
    }

    internal GraphicsBuffer Vertices { get; }

    internal GraphicsBuffer Indices { get; }

    internal int IndexCount { get; }

    internal Texture2D Texture { get; }

    internal string ProgramName { get; }

    internal BlendState BlendState { get; }

    public static GrParticleField? Create(GraphicsDevice graphicsDevice, Gw2MapEnvParticleField field, Texture2D texture)
    {
        ArgumentNullException.ThrowIfNull(field);
        var mesh = Gw2ParticleFieldMesh.Build(field);
        if (mesh.ParticleCount == 0) return null;
        var vertices = Array.ConvertAll(mesh.Vertices, vertex => new GrParticleFieldVertex(
            new Vector3(vertex.Position.X, vertex.Position.Y, vertex.Position.Z),
            new Color(vertex.CornerU, vertex.CornerV, vertex.Phase, vertex.Opacity),
            new Vector2(vertex.Scale.X, vertex.Scale.Y),
            new Vector2(vertex.Angle, vertex.Rotation),
            new Vector2(vertex.StartFrame, vertex.CornerCode)));
        var vertexBuffer = GrBuffers.Vertex<GrParticleFieldVertex>(graphicsDevice, vertices, "ParticleFieldVertices");
        var indexCount = mesh.ParticleCount * Gw2ParticleFieldMesh.QuadIndices.Length;
        var indexBuffer = vertices.Length <= ShortIndexLimit
            ? GrBuffers.Index(graphicsDevice, QuadIndices(mesh.ParticleCount, index => unchecked((ushort)index)), "ParticleFieldIndices")
            : GrBuffers.Index(graphicsDevice, QuadIndices(mesh.ParticleCount, index => index), "ParticleFieldIndices");

        return new GrParticleField(vertexBuffer, indexBuffer, indexCount, texture, ProgramNameOf(field), GrParticleFieldBlendStates.Of(Gw2ParticleFieldFrame.BlendFlags(field)));
    }

    private static T[] QuadIndices<T>(int particleCount, Func<int, T> convert)
    {
        var indices = new T[particleCount * Gw2ParticleFieldMesh.QuadIndices.Length];
        for (var particle = 0; particle < particleCount; particle++)
            for (var corner = 0; corner < Gw2ParticleFieldMesh.QuadIndices.Length; corner++)
                indices[particle * Gw2ParticleFieldMesh.QuadIndices.Length + corner] = convert(particle * Gw2ParticleFieldMesh.VerticesPerParticle + Gw2ParticleFieldMesh.QuadIndices[corner]);
        return indices;
    }

    private static string ProgramNameOf(Gw2MapEnvParticleField field)
    {
        var motion = field.Type switch
        {
            0 => "Streak",
            1 => "Bubble",
            2 => "Sparkle",
            _ => "Ray",
        };
        return (field.Flags & Gw2ParticleFieldFrame.SlabFlag) != 0 ? motion + "Slab" : motion;
    }

    public void Dispose()
    {
        Vertices.Dispose();
        Indices.Dispose();
    }
}

public static class GrParticleFieldBlendStates
{
    private const ColorWriteChannels ColorOnly = ColorWriteChannels.Red | ColorWriteChannels.Green | ColorWriteChannels.Blue;

    private static readonly BlendState Additive = new(new RenderTargetBlendDescription
    {
        BlendEnable = true,
        SourceBlend = Blend.SourceAlpha,
        DestinationBlend = Blend.One,
        SourceBlendAlpha = Blend.SourceAlpha,
        DestinationBlendAlpha = Blend.One,
        WriteMask = ColorOnly,
    });

    private static readonly BlendState Subtractive = new(new RenderTargetBlendDescription
    {
        BlendEnable = true,
        SourceBlend = Blend.SourceAlpha,
        DestinationBlend = Blend.One,
        BlendOperation = BlendOperation.ReverseSubtract,
        SourceBlendAlpha = Blend.SourceAlpha,
        DestinationBlendAlpha = Blend.One,
        BlendOperationAlpha = BlendOperation.ReverseSubtract,
        WriteMask = ColorOnly,
    });

    private static readonly BlendState AlphaBlend = new(new RenderTargetBlendDescription
    {
        BlendEnable = true,
        SourceBlend = Blend.SourceAlpha,
        DestinationBlend = Blend.InverseSourceAlpha,
        SourceBlendAlpha = Blend.SourceAlpha,
        DestinationBlendAlpha = Blend.InverseSourceAlpha,
        WriteMask = ColorOnly,
    });

    public static BlendState Of(uint blendFlags) =>
        (blendFlags & Gw2ParticleFieldFrame.AdditiveFlag) != 0 ? Additive
        : (blendFlags & Gw2ParticleFieldFrame.SubtractiveFlag) != 0 ? Subtractive
        : AlphaBlend;
}

public sealed class GrParticleFieldPass
{
    private const int RowsPerCutout = 3;

    private readonly GrDevice _device;
    private readonly ShaderLibrary _library;
    private readonly Vector4[] _cutoutRows = new Vector4[Gw2ParticleFieldFrame.MaximumCutouts * RowsPerCutout];

    public GrParticleFieldPass(GrDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
        _library = device.LoadShader("GrParticleField");
    }

    public void Draw(IReadOnlyList<GrParticleFieldDraw> draws, Matrix4x4 view, Matrix4x4 projection, Vector3 eye, Texture2D depthCopy, float cameraNear, float cameraFar)
    {
        ArgumentNullException.ThrowIfNull(draws);
        if (draws.Count == 0) return;
        var graphicsDevice = _device.GraphicsDevice;
        var viewport = graphicsDevice.Viewport;
        var viewProjectionRows = GrMatrix.ToRegisterRows(view * projection);
        var viewRows = GrMatrix.ToRegisterRows(view);
        foreach (var draw in draws)
        {
            var program = _library.GetProgram(draw.Field.ProgramName);
            var parameters = program.Parameters;
            parameters["CameraPosition"]?.SetValue(new Vector4(eye.X, eye.Y, eye.Z, 1f));
            parameters["ViewProjection"]?.SetValue(viewProjectionRows);
            parameters["View"]?.SetValue(viewRows);
            parameters["ScreenDims"]?.SetValue(GrEngineConstants.ScreenDims((int)viewport.Width, (int)viewport.Height, cameraNear, cameraFar));
            parameters["ss12"]?.SetValue(depthCopy);
            parameters["ss12Sampler"]?.SetValue(_device.Sampler(SamplerDescription.PointClamp));
            parameters["ss00"]?.SetValue(draw.Field.Texture);
            parameters["ss00Sampler"]?.SetValue(_device.Sampler(SamplerDescription.LinearClamp));
            SetConstants(parameters, draw.Constants);
            SetCutouts(parameters, draw.Cutouts);
            _device.ApplyPipeline(program, draw.Field.BlendState, DepthStencilState.DepthRead, RasterizerState.CullNone, GrParticleFieldVertex.Layout);
            graphicsDevice.SetVertexBuffer(draw.Field.Vertices);
            graphicsDevice.SetIndexBuffer(draw.Field.Indices);
            graphicsDevice.DrawIndexed(draw.Field.IndexCount);
        }
    }

    private static void SetConstants(ShaderParameterCollection parameters, Gw2ParticleFieldConstants constants)
    {
        parameters["fldprta"]?.SetValue(constants.A);
        parameters["fldprtb"]?.SetValue(constants.B);
        parameters["fldprtc"]?.SetValue(constants.C);
        parameters["fldprtd"]?.SetValue(constants.D);
        parameters["fldprte"]?.SetValue(constants.E);
        parameters["fldprtf"]?.SetValue(constants.F);
        parameters["fldprtg"]?.SetValue(constants.G);
    }

    private void SetCutouts(ShaderParameterCollection parameters, IReadOnlyList<GrParticleFieldCutout> cutouts)
    {
        Array.Clear(_cutoutRows);
        var count = Math.Min(cutouts.Count, Gw2ParticleFieldFrame.MaximumCutouts);
        for (var index = 0; index < count; index++)
        {
            _cutoutRows[index * RowsPerCutout] = cutouts[index].RowX;
            _cutoutRows[index * RowsPerCutout + 1] = cutouts[index].RowY;
            _cutoutRows[index * RowsPerCutout + 2] = cutouts[index].RowZ;
        }

        parameters["transformCount"]?.SetValue(new Vector4(count));
        parameters["CutoutRows"]?.SetValue(_cutoutRows);
    }
}
