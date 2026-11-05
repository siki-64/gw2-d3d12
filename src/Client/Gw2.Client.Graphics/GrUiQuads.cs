using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Crystal;
using Crystal.Graphics;

namespace Gw2.Client.Graphics;

public readonly record struct GrUiTextureTransform(Vector3 RowU, Vector3 RowV)
{
    public static GrUiTextureTransform Identity { get; } = new(new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f));

    public static GrUiTextureTransform QuarterTurn { get; } = new(new Vector3(0f, -1f, 1f), new Vector3(1f, 0f, 0f));

    public Vector2 Apply(Vector2 uv) => new(RowU.X * uv.X + RowU.Y * uv.Y + RowU.Z, RowV.X * uv.X + RowV.Y * uv.Y + RowV.Z);
}

public sealed record GrUiQuad(
    GrScreenRect ScreenRect,
    int UiShader,
    Texture2D?[] Textures,
    GrUiTextureTransform[] TextureTransforms,
    Vector4 ModelColor,
    Vector4 Control);

public readonly record struct GrScreenRect(float Left, float Top, float Right, float Bottom);

public sealed class GrUiQuads : IDisposable
{
    private const int VerticesPerQuad = 6;

    private static readonly VertexLayout Layout = new(
        new VertexElement("POSITION", 0, VertexElementFormat.Vector3, 0),
        new VertexElement("TEXCOORD", 0, VertexElementFormat.Vector2, 12),
        new VertexElement("TEXCOORD", 1, VertexElementFormat.Vector2, 20),
        new VertexElement("TEXCOORD", 2, VertexElementFormat.Vector2, 28),
        new VertexElement("TEXCOORD", 3, VertexElementFormat.Vector2, 36));

    private static readonly string[] TextureParameterNames = ["Texture0", "Texture1", "Texture2", "Texture3"];
    private static readonly Vector2[] CornerUvs =
    [
        new(0f, 0f), new(1f, 0f), new(0f, 1f),
        new(0f, 1f), new(1f, 0f), new(1f, 1f),
    ];

    private readonly GrDevice _device;
    private readonly ShaderProgram _program;
    private readonly Texture2D _white;
    private readonly UiVertex[] _vertices = new UiVertex[VerticesPerQuad];

    public GrUiQuads(GrDevice device)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _program = device.LoadShader("GrUiMaterial").GetProgram("UiMaterial");
        _white = new Texture2D(device.GraphicsDevice, new TextureDescription { Width = 1, Height = 1 });
        _white.SetData<Color>([Color.White]);
    }

    public void SetClip(GrScreenRect clip)
    {
        var left = (int)MathF.Floor(clip.Left);
        var top = (int)MathF.Floor(clip.Top);
        _device.GraphicsDevice.ScissorRectangle = new Rectangle(left, top, Math.Max(0, (int)MathF.Ceiling(clip.Right) - left), Math.Max(0, (int)MathF.Ceiling(clip.Bottom) - top));
    }

    public void Draw(GrUiQuad quad) => Draw(quad, null, 0f);

    public void Draw(GrUiQuad quad, Texture2D? sceneDepth, float depth)
    {
        var graphicsDevice = _device.GraphicsDevice;
        var screenWidth = (float)graphicsDevice.BackBufferWidth;
        var screenHeight = (float)graphicsDevice.BackBufferHeight;
        var parameters = _program.Parameters;
        var sampler = _device.Sampler(SamplerDescription.LinearClamp);
        for (var slot = 0; slot < TextureParameterNames.Length; slot++)
            parameters[TextureParameterNames[slot]]?.SetValue(slot < quad.Textures.Length && quad.Textures[slot] is { } texture ? texture : _white);
        parameters["SceneDepth"]?.SetValue(sceneDepth ?? _white);
        parameters["TextureSampler"]?.SetValue(sampler);
        parameters["ModelColor"]?.SetValue(quad.ModelColor);
        parameters["Control"]?.SetValue(quad.Control);
        parameters["UiShader"]?.SetValue((uint)quad.UiShader);
        parameters["AlphaReference"]?.SetValue(0f);
        parameters["SceneDepthTest"]?.SetValue(sceneDepth is null ? Vector4.Zero : new Vector4(sceneDepth.Width / screenWidth, sceneDepth.Height / screenHeight, depth, 1f));

        var rect = quad.ScreenRect;
        for (var corner = 0; corner < VerticesPerQuad; corner++)
        {
            var uv = CornerUvs[corner];
            var x = rect.Left + uv.X * (rect.Right - rect.Left);
            var y = rect.Top + uv.Y * (rect.Bottom - rect.Top);
            _vertices[corner] = new UiVertex(
                new Vector3(x / screenWidth * 2f - 1f, 1f - y / screenHeight * 2f, 0f),
                TransformedUv(quad, 0, uv), TransformedUv(quad, 1, uv), TransformedUv(quad, 2, uv), TransformedUv(quad, 3, uv));
        }

        _device.ApplyPipeline(_program, BlendStateOf(quad.UiShader), DepthStencilState.None, RasterizerState.CullNone, Layout);
        graphicsDevice.SetVertexData<UiVertex>(_vertices);
        graphicsDevice.Draw(VerticesPerQuad);
    }

    private static BlendState BlendStateOf(int uiShader) =>
        GrUiMaterialCombine.ForUiShader(uiShader) is { RenderStateFlags: GrUiMaterialCombine.ModulateTwiceRenderState } modulateTwice
            ? GrBlendStates.From(modulateTwice.RenderState)
            : BlendState.NonPremultiplied;

    public void ResetClip()
    {
        var graphicsDevice = _device.GraphicsDevice;
        graphicsDevice.ScissorRectangle = new Rectangle(0, 0, graphicsDevice.BackBufferWidth, graphicsDevice.BackBufferHeight);
    }

    private static Vector2 TransformedUv(GrUiQuad quad, int slot, Vector2 uv) =>
        slot < quad.TextureTransforms.Length ? quad.TextureTransforms[slot].Apply(uv) : uv;

    public void Dispose() => _white.Dispose();

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct UiVertex(Vector3 Position, Vector2 Uv0, Vector2 Uv1, Vector2 Uv2, Vector2 Uv3);
}
