using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using Crystal;
using Crystal.Graphics;
using Gw2.Dat.Content;

namespace Gw2.Client.Graphics;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct GrVertexSkyCard
{
    public static readonly VertexLayout Layout = new(
        new VertexElement("POSITION", 0, VertexElementFormat.Vector3, 0),
        new VertexElement("TEXCOORD", 0, VertexElementFormat.Vector2, 12),
        new VertexElement("TEXCOORD", 1, VertexElementFormat.Vector2, 20));

    public Vector3 Position;
    public Vector2 NightUv;
    public Vector2 DayUv;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct GrVertexSkyCardMaterial
{
    public static readonly VertexLayout Layout = new(
        new VertexElement("POSITION", 0, VertexElementFormat.Vector3, 0),
        new VertexElement("NORMAL", 0, VertexElementFormat.Vector3, 12),
        new VertexElement("TEXCOORD", 0, VertexElementFormat.Vector2, 24),
        new VertexElement("TEXCOORD", 1, VertexElementFormat.Vector2, 32),
        new VertexElement("TEXCOORD", 2, VertexElementFormat.Vector2, 40));

    public Vector3 Position;
    public Vector3 Normal;
    public Vector2 ColorUv;
    public Vector2 FirstNormalUv;
    public Vector2 SecondNormalUv;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct GrVertexLensFlare
{
    public static readonly VertexLayout Layout = new(
        new VertexElement("POSITION", 0, VertexElementFormat.Vector3, 0),
        new VertexElement("NORMAL", 0, VertexElementFormat.Vector3, 12),
        new VertexElement("COLOR", 0, VertexElementFormat.Vector4, 24),
        new VertexElement("TEXCOORD", 0, VertexElementFormat.Vector2, 40),
        new VertexElement("TEXCOORD", 1, VertexElementFormat.Vector2, 48),
        new VertexElement("TEXCOORD", 2, VertexElementFormat.Vector2, 56));

    public Vector3 Position;
    public Vector3 Normal;
    public Vector4 Color;
    public Vector2 Uv;
    public Vector2 NextUv;
    public Vector2 Blend;
}

public sealed record GrSkyCardLensFlare(Gw2MapEnvLensFlare Flare, GrArchiveShaderMaterial ShaderMaterial, IReadOnlyList<Texture2D?> Textures);

public sealed record GrSkyCardMaterial(GrArchiveShaderMaterial ShaderMaterial, IReadOnlyList<Texture2D?> Textures, float[][] UvRects, IReadOnlyDictionary<uint, Vector4> Constants);

public sealed record GrSkyCardShape(uint Flags, Vector3 Location, float[] NightUv, float[] DayUv, float NightSpeed, float DaySpeed, Texture2D? Day, Texture2D? Night, GrSkyCardMaterial? Material = null, Gw2SkyCardTextureAnimator? TextureAnimator = null, Gw2MapEnvSkyCardFlipbook? Flipbook = null, GrSkyCardLensFlare? DayLensFlare = null, GrSkyCardLensFlare? NightLensFlare = null);

public readonly record struct GrSkyCardLook(float Density, float Brightness, float HazeDensity, float LightIntensity, float MinHaze, Vector2 Scale, float Azimuth, float Latitude);

public readonly record struct GrSkyCardConstants(
    Vector4 FogColorFar,
    Vector4 ShRed,
    Vector4 ShGreen,
    Vector4 ShBlue,
    Vector3 SunDirection,
    Vector3 SunColor,
    float HazeBottom,
    float HazeFalloff,
    float SkyHazeDensity,
    float SkyLightIntensity,
    float Intensity,
    float DayWeight,
    float ClockSeconds = 0f,
    Gw2.Dat.Content.Gw2EnvironmentFog? Fog = null);

public sealed class GrSkyCards : IDisposable
{
    public const uint Additive = 0x1;
    public const uint LightRaySource = 0x2;
    public const uint AimAtLocation = 0x8;
    public const uint AboveWaterOnly = 0x4;
    public const uint UnderwaterOnly = 0x80;
    public const uint ScaleWithDistance = 0x18;
    private const float QuadHalfSize = 1000f;
    private const float MinimumScale = 0.000001f;
    private const float LocationScaleDistance = 25000f;
    private const float OcclusionDiscRadius = 500f;
    private const int OcclusionDiscSides = 8;
    private const float DaySetWeight = 0.5f;
    private static readonly ushort[] QuadIndices = [0, 1, 2, 1, 2, 3];
    private static readonly Vector4 AdditiveColor = Vector4.One;
    private static readonly BlendState AdditiveBlend = new(new RenderTargetBlendDescription
    {
        BlendEnable = true,
        SourceBlend = Blend.One,
        DestinationBlend = Blend.One,
        SourceBlendAlpha = Blend.One,
        DestinationBlendAlpha = Blend.One,
    });
    private static readonly BlendState NoColorWrite = new(new RenderTargetBlendDescription { WriteMask = ColorWriteChannels.None });
    private static readonly BlendState AlphaWriteOnly = new(new RenderTargetBlendDescription { WriteMask = ColorWriteChannels.Alpha });
    private static readonly Vector4 LightRaySourceStencilId = new(8f / 255f);

    private readonly GrDevice _device;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly ShaderProgram _skyCard;
    private readonly ShaderProgram _additiveSkyCard;
    private readonly ShaderProgram _lightRaySourceCard;
    private readonly ShaderProgram _clearLightRaySources;
    private readonly GrDrawFrame _drawFrame = new();
    private readonly GrSkyCardShape[] _shapes;
    private readonly GraphicsBuffer[] _vertices;
    private readonly GraphicsBuffer _indices;
    private readonly Texture2D _transparent;
    private readonly Dictionary<(float Night, float Day), float> _rotation = new();
    private float _flipbookSeconds;
    private readonly GrLensFlareState?[] _lensFlares;
    private readonly GraphicsBuffer _occlusionDisc;
    private readonly GraphicsBuffer _occlusionDiscIndices;

    public GrSkyCards(GrDevice device, IReadOnlyList<GrSkyCardShape> shapes, float viewDistance)
    {
        _device = device;
        _graphicsDevice = device.GraphicsDevice;
        var library = device.LoadShader("GrSkyCard");
        _skyCard = library.GetProgram("SkyCard");
        _additiveSkyCard = library.GetProgram("AdditiveSkyCard");
        _lightRaySourceCard = library.GetProgram("LightRaySourceCard");
        _clearLightRaySources = library.GetProgram("ClearLightRaySources");
        _shapes = shapes.ToArray();
        _vertices = _shapes.Select(shape => shape.Material is { } material
            ? GrBuffers.Vertex<GrVertexSkyCardMaterial>(_graphicsDevice, MaterialQuadVertices(viewDistance, material.UvRects), "SkyCardMaterialVertices")
            : GrBuffers.Vertex<GrVertexSkyCard>(_graphicsDevice, QuadVertices(viewDistance, shape.NightUv, shape.DayUv), "SkyCardVertices")).ToArray();
        _indices = GrBuffers.Index(_graphicsDevice, QuadIndices, "SkyCardIndices");
        _transparent = new Texture2D(_graphicsDevice, new TextureDescription { Width = 1, Height = 1, Name = "SkyCardTransparent" });
        _transparent.SetData<Color>([Color.Transparent]);
        _lensFlares = _shapes.Select(shape => shape.DayLensFlare is null && shape.NightLensFlare is null ? null : new GrLensFlareState(_graphicsDevice, Math.Max(shape.DayLensFlare?.Flare.Atoms.Count ?? 0, shape.NightLensFlare?.Flare.Atoms.Count ?? 0))).ToArray();
        _occlusionDisc = GrBuffers.Vertex<GrVertexSkyCard>(_graphicsDevice, OcclusionDiscVertices(viewDistance), "SkyCardOcclusionDisc");
        _occlusionDiscIndices = GrBuffers.Index(_graphicsDevice, Enumerable.Range(0, OcclusionDiscSides).SelectMany(side => new[] { (ushort)0, (ushort)(side + 1), (ushort)((side + 1) % OcclusionDiscSides + 1) }).ToArray(), "SkyCardOcclusionDiscIndices");
    }

    public static GrVertexSkyCard[] OcclusionDiscVertices(float distance) =>
        new[] { new GrVertexSkyCard { Position = new Vector3(distance, 0f, 0f) } }
            .Concat(Enumerable.Range(0, OcclusionDiscSides).Select(side =>
            {
                var angle = side * MathF.Tau / OcclusionDiscSides;
                return new GrVertexSkyCard { Position = new Vector3(distance, MathF.Cos(angle) * OcclusionDiscRadius, MathF.Sin(angle) * OcclusionDiscRadius) };
            }))
            .ToArray();

    public static GrVertexSkyCard[] QuadVertices(float viewDistance, float[] nightUv, float[] dayUv)
    {
        GrVertexSkyCard Corner(float y, float z, int uIndex, int vIndex) => new()
        {
            Position = OnViewSphere(new Vector3(viewDistance, y, z), viewDistance),
            NightUv = new Vector2(nightUv[uIndex], 1f - nightUv[vIndex]),
            DayUv = new Vector2(dayUv[uIndex], 1f - dayUv[vIndex]),
        };

        return
        [
            Corner(QuadHalfSize, -QuadHalfSize, 0, 2),
            Corner(QuadHalfSize, QuadHalfSize, 0, 3),
            Corner(-QuadHalfSize, -QuadHalfSize, 1, 2),
            Corner(-QuadHalfSize, QuadHalfSize, 1, 3),
        ];
    }

    public static GrVertexSkyCardMaterial[] MaterialQuadVertices(float viewDistance, float[][] uvRects)
    {
        Vector2 Uv(int set, int uIndex, int vIndex) => new(uvRects[set][uIndex], 1f - uvRects[set][vIndex]);

        GrVertexSkyCardMaterial Corner(float y, float z, int uIndex, int vIndex) => new()
        {
            Position = OnViewSphere(new Vector3(viewDistance, y, z), viewDistance),
            ColorUv = Uv(0, uIndex, vIndex),
            FirstNormalUv = Uv(1, uIndex, vIndex),
            SecondNormalUv = Uv(2, uIndex, vIndex),
        };

        return
        [
            Corner(QuadHalfSize, -QuadHalfSize, 0, 2),
            Corner(QuadHalfSize, QuadHalfSize, 0, 3),
            Corner(-QuadHalfSize, -QuadHalfSize, 1, 2),
            Corner(-QuadHalfSize, QuadHalfSize, 1, 3),
        ];
    }

    private static Vector3 OnViewSphere(Vector3 position, float viewDistance)
    {
        var length = position.Length();
        return length > viewDistance ? position * (viewDistance / length) : position;
    }

    public void Advance(float elapsedSeconds, float dayWeight)
    {
        foreach (var key in _shapes.Select(shape => (shape.NightSpeed, shape.DaySpeed)).Distinct())
            _rotation[key] = _rotation.GetValueOrDefault(key) + ((key.DaySpeed - key.NightSpeed) * dayWeight + key.NightSpeed) * elapsedSeconds;
        foreach (var shape in _shapes) shape.TextureAnimator?.Advance(elapsedSeconds);
        _flipbookSeconds += elapsedSeconds;
    }

    public static (float Azimuth, float Latitude) AimAt(Vector3 location, Vector3 eye)
    {
        var toLocation = location - eye;
        var direction = toLocation.LengthSquared() >= 1e-12f ? Vector3.Normalize(toLocation) : Vector3.Zero;
        var latitude = MathF.Asin(-Math.Clamp(direction.Z, -1f, 1f));
        var flat = new Vector2(direction.X, direction.Y);
        flat = flat.LengthSquared() >= 1e-12f ? Vector2.Normalize(flat) : Vector2.Zero;
        var azimuth = MathF.Asin(Math.Clamp(flat.Y, -1f, 1f));
        return (flat.X < 0f ? MathF.PI - azimuth : azimuth, latitude);
    }

    public (float Azimuth, float Latitude) CardAngles(GrSkyCardShape shape, GrSkyCardLook look, Vector3 eye) =>
        (shape.Flags & AimAtLocation) != 0
            ? AimAt(shape.Location, eye)
            : (look.Azimuth + _rotation.GetValueOrDefault((shape.NightSpeed, shape.DaySpeed)), look.Latitude);

    public Matrix4x4 CardWorld(GrSkyCardShape shape, GrSkyCardLook look, Vector3 eye)
    {
        var (azimuth, latitude) = CardAngles(shape, look, eye);
        var scale = look.Scale;
        if ((shape.Flags & ScaleWithDistance) == ScaleWithDistance)
        {
            var distance = (shape.Location - eye).Length();
            scale *= LocationScaleDistance / (distance < MinimumScale ? 1f : distance);
        }

        scale = Vector2.Max(scale, new Vector2(MinimumScale));
        return Matrix4x4.CreateScale(1f, scale.X, scale.Y)
            * Matrix4x4.CreateRotationY(latitude)
            * Matrix4x4.CreateRotationZ(azimuth)
            * Matrix4x4.CreateTranslation(eye);
    }

    private void SetFrameParameters(ShaderProgram program, GrSkyCardConstants constants)
    {
        var parameters = program.Parameters;
        var sunDirection = constants.SunDirection.LengthSquared() >= 1e-12f ? Vector3.Normalize(constants.SunDirection) : Vector3.Zero;
        parameters["FogColorFar"]?.SetValue(constants.FogColorFar);
        parameters["TimeOfDay"]?.SetValue(new Vector4(constants.DayWeight, 0f, 0f, 0f));
        parameters["shRed"]?.SetValue(constants.ShRed);
        parameters["shGreen"]?.SetValue(constants.ShGreen);
        parameters["shBlue"]?.SetValue(constants.ShBlue);
        parameters["sunDir"]?.SetValue(new Vector4(sunDirection.X, sunDirection.Y, sunDirection.Z, 0f));
        parameters["fxclr"]?.SetValue(AdditiveColor);
    }

    private void SetCardParameters(ShaderProgram program, GrSkyCardShape shape, GrSkyCardLook look, Matrix4x4 world, Matrix4x4 viewProjection, GrSkyCardConstants constants)
    {
        var parameters = program.Parameters;
        parameters["World"]?.SetValue(GrMatrix.ToRegisterRows(world));
        parameters["WorldViewProjection"]?.SetValue(GrMatrix.ToRegisterRows(world * viewProjection));
        parameters["skypara"]?.SetValue(new Vector4(constants.HazeBottom, constants.HazeFalloff, look.HazeDensity * constants.SkyHazeDensity, look.MinHaze));
        parameters["skyparb"]?.SetValue(new Vector4(look.Density * constants.Intensity, look.Brightness, 0f, 0f));
        parameters["sunClr"]?.SetValue(new Vector4(constants.SunColor.X, constants.SunColor.Y, constants.SunColor.Z, look.LightIntensity * constants.SkyLightIntensity));
        parameters["ss0"]?.SetValue(shape.Day ?? _transparent);
        parameters["ss1"]?.SetValue(shape.Night ?? _transparent);
        parameters["ss0Sampler"]?.SetValue(_device.Sampler(SamplerDescription.LinearClamp));
        parameters["ss1Sampler"]?.SetValue(_device.Sampler(SamplerDescription.LinearClamp));
    }

    public Vector2? LightRayScreenPosition(Matrix4x4 view, Matrix4x4 projection, Vector3 eye, float waterSurfaceZ, IReadOnlyList<GrSkyCardLook> looks, float intensity)
    {
        Vector2? screenPosition = null;
        var viewProjection = view * projection;
        for (var index = 0; index < _shapes.Length; index++)
        {
            var shape = _shapes[index];
            if ((shape.Flags & LightRaySource) == 0 || !IsShown(shape.Flags, eye.Z, waterSurfaceZ)) continue;
            var look = looks[index];
            var (azimuth, latitude) = CardAngles(shape, look, eye);
            var clip = Vector4.Transform(new Vector4(Gw2SkyCardLensFlare.Position(eye, azimuth, latitude), 1f), viewProjection);
            if (clip.Z > 0f && look.Density * intensity > 0f)
                screenPosition = new Vector2(clip.X / clip.W * 0.5f + 0.5f, 1f - (clip.Y / clip.W * 0.5f + 0.5f));
        }

        return screenPosition;
    }

    public void DrawLightRaySources(Matrix4x4 view, Matrix4x4 projection, Vector3 eye, float waterSurfaceZ, IReadOnlyList<GrSkyCardLook> looks, GrSkyCardConstants constants)
    {
        ClearLightRaySources();
        SetFrameParameters(_lightRaySourceCard, constants);
        _lightRaySourceCard.Parameters["StencilId"]?.SetValue(LightRaySourceStencilId);
        var viewProjection = view * projection;
        for (var index = 0; index < _shapes.Length; index++)
        {
            var shape = _shapes[index];
            if ((shape.Flags & LightRaySource) == 0 || shape.Material is not null || !IsShown(shape.Flags, eye.Z, waterSurfaceZ)) continue;
            var look = looks[index];
            SetCardParameters(_lightRaySourceCard, shape, look, CardWorld(shape, look, eye), viewProjection, constants);
            _device.ApplyPipeline(_lightRaySourceCard, AlphaWriteOnly, DepthStencilState.DepthRead, RasterizerState.CullNone, GrVertexSkyCard.Layout);
            DrawCard(index);
        }
    }

    private void ClearLightRaySources()
    {
        _device.ApplyPipeline(_clearLightRaySources, AlphaWriteOnly, DepthStencilState.None, RasterizerState.CullNone, GrFullScreenTriangle.Layout);
        GrFullScreenTriangle.Draw(_graphicsDevice);
    }

    private void DrawCard(int index)
    {
        _graphicsDevice.SetVertexBuffer(_vertices[index]);
        _graphicsDevice.SetIndexBuffer(_indices);
        _graphicsDevice.DrawIndexed(QuadIndices.Length);
    }

    public void Draw(Matrix4x4 view, Matrix4x4 projection, Vector3 eye, float waterSurfaceZ, IReadOnlyList<GrSkyCardLook> looks, GrSkyCardConstants constants)
    {
        var viewProjection = view * projection;
        SetFrameParameters(_skyCard, constants);
        SetFrameParameters(_additiveSkyCard, constants);

        for (var index = 0; index < _shapes.Length; index++)
        {
            var shape = _shapes[index];
            if (!IsShown(shape.Flags, eye.Z, waterSurfaceZ)) continue;
            var look = looks[index];
            var world = CardWorld(shape, look, eye);
            if (shape.Material is { } material)
            {
                DrawMaterialCard(index, material, look, world, view, viewProjection, eye, constants);
                continue;
            }

            var additive = (shape.Flags & Additive) != 0;
            var program = additive ? _additiveSkyCard : _skyCard;
            SetCardParameters(program, shape, look, world, viewProjection, constants);
            _device.ApplyPipeline(program, additive ? AdditiveBlend : BlendState.NonPremultiplied, DepthStencilState.None, RasterizerState.CullNone, GrVertexSkyCard.Layout);
            DrawCard(index);
        }
    }

    private static readonly (GrDrawConstantId RowA, GrDrawConstantId RowB)[] TexTransformByUvSet =
    [
        (GrDrawConstantId.TexTransform0A, GrDrawConstantId.TexTransform0B),
        (GrDrawConstantId.TexTransform1A, GrDrawConstantId.TexTransform1B),
        (GrDrawConstantId.TexTransform2A, GrDrawConstantId.TexTransform2B),
        (GrDrawConstantId.TexTransform3A, GrDrawConstantId.TexTransform3B),
    ];

    private void AddFlipbook(Dictionary<uint, Vector4> inputs, Gw2MapEnvSkyCardFlipbook? flipbook)
    {
        if (flipbook is null) return;
        var frame = Gw2SkyCardFlipbookFrame.At(flipbook, _flipbookSeconds);
        inputs[Gw2SkyCardFlipbookFrame.CellToken] = new(frame.Cell.X, frame.Cell.Y, frame.Cell.Z, frame.Cell.W);
        inputs[Gw2SkyCardFlipbookFrame.NextCellToken] = new(frame.NextCell.X, frame.NextCell.Y, frame.NextCell.Z, frame.NextCell.W);
        inputs[Gw2SkyCardFlipbookFrame.BlendToken] = new(frame.Blend);
    }

    private static void AddTextureAnimation(Dictionary<uint, Vector4> inputs, Gw2SkyCardTextureAnimator? animator)
    {
        if (animator is null) return;
        var (rowA, rowB) = animator.Transform;
        for (var uvSet = 0; uvSet < Gw2SkyCardTextureAnimator.UvSetCount; uvSet++)
        {
            if (!animator.AppliesTo(uvSet)) continue;
            if (GrDrawConstantIds.TokenOf(TexTransformByUvSet[uvSet].RowA) is { } tokenA) inputs[tokenA] = new(rowA.X, rowA.Y, rowA.Z, rowA.W);
            if (GrDrawConstantIds.TokenOf(TexTransformByUvSet[uvSet].RowB) is { } tokenB) inputs[tokenB] = new(rowB.X, rowB.Y, rowB.Z, rowB.W);
        }
    }

    private void DrawMaterialCard(int index, GrSkyCardMaterial material, GrSkyCardLook look, Matrix4x4 world, Matrix4x4 view, Matrix4x4 viewProjection, Vector3 eye, GrSkyCardConstants constants)
    {
        var sunDirection = constants.SunDirection.LengthSquared() >= 1e-12f ? Vector3.Normalize(constants.SunDirection) : Vector3.Zero;
        var cardValues = new Dictionary<string, Vector4>
        {
            ["skypara"] = new(constants.HazeBottom, constants.HazeFalloff, look.HazeDensity * constants.SkyHazeDensity, look.MinHaze),
            ["skyparb"] = new(look.Density * constants.Intensity, look.Brightness, 0f, 0f),
            ["sunDir"] = new(sunDirection.X, sunDirection.Y, sunDirection.Z, 0f),
            ["sunClr"] = new(constants.SunColor.X, constants.SunColor.Y, constants.SunColor.Z, look.LightIntensity * constants.SkyLightIntensity),
            ["TimeOfDay"] = new(constants.DayWeight, 0f, 0f, 0f),
            ["fxclr"] = AdditiveColor,
        };
        SetDrawFrame(view, viewProjection, eye, constants);
        foreach (var pass in new[] { material.ShaderMaterial }.Concat(material.ShaderMaterial.LaterPasses))
        {
            var inputs = new Dictionary<uint, Vector4>(material.Constants);
            AddTextureAnimation(inputs, _shapes[index].TextureAnimator);
            AddFlipbook(inputs, _shapes[index].Flipbook);
            foreach (var uniform in pass.Uniforms)
                if (cardValues.TryGetValue(uniform.Parameter.Name, out var value))
                    inputs[uniform.Token] = value;
            GrDrawConstants.Apply(pass, _drawFrame, new GrDrawItem(world, Vector4.One, 0, inputs));
            GrDrawTextures.Bind(_device, pass, sampler =>
                !sampler.IsEngineTexture && sampler.ResourceIndex < material.Textures.Count ? material.Textures[sampler.ResourceIndex] ?? _transparent : _transparent, anisotropicFiltering: false);
            ApplyMaterialPipeline(pass, GrVertexSkyCardMaterial.Layout);
            _graphicsDevice.SetVertexBuffer(_vertices[index]);
            _graphicsDevice.SetIndexBuffer(_indices);
            _graphicsDevice.DrawIndexed(QuadIndices.Length);
        }
    }

    private void ApplyMaterialPipeline(GrArchiveShaderMaterial pass, VertexLayout layout)
    {
        var drawState = pass.DrawStateFor(viewDepthTest: false);
        _device.ApplyPipeline(pass.Program, GrBlendStates.From(pass.RenderState, drawState.ColorWrite), GrDrawStates.DepthStencil(drawState), RasterizerState.CullNone, layout);
    }

    private void SetDrawFrame(Matrix4x4 view, Matrix4x4 viewProjection, Vector3 eye, GrSkyCardConstants constants)
    {
        var viewport = _graphicsDevice.Viewport;
        _drawFrame.View = view;
        _drawFrame.ViewProjection = viewProjection;
        _drawFrame.CameraPosition = eye;
        _drawFrame.ViewportWidth = (int)viewport.Width;
        _drawFrame.ViewportHeight = (int)viewport.Height;
        _drawFrame.ClockSeconds = constants.ClockSeconds;
        _drawFrame.Fog = constants.Fog;
    }

    public void DrawLensFlares(Matrix4x4 view, Matrix4x4 projection, Vector3 eye, float waterSurfaceZ, IReadOnlyList<GrSkyCardLook> looks, GrSkyCardConstants constants)
    {
        if (_lensFlares.All(state => state is null)) return;
        Matrix4x4.Invert(view, out var cameraWorld);
        var cameraRight = new Vector3(cameraWorld.M11, cameraWorld.M12, cameraWorld.M13);
        var cameraUp = new Vector3(cameraWorld.M21, cameraWorld.M22, cameraWorld.M23);
        var cameraForward = new Vector3(cameraWorld.M31, cameraWorld.M32, cameraWorld.M33);
        var viewProjection = view * projection;
        SetDrawFrame(view, viewProjection, eye, constants);

        for (var index = 0; index < _shapes.Length; index++)
        {
            if (_lensFlares[index] is not { } state) continue;
            var shape = _shapes[index];
            if (!IsShown(shape.Flags, eye.Z, waterSurfaceZ)) continue;
            if ((constants.DayWeight > DaySetWeight ? shape.DayLensFlare : shape.NightLensFlare) is not { } lensFlare) continue;
            var look = looks[index];
            var (azimuth, latitude) = CardAngles(shape, look, eye);
            var position = Gw2SkyCardLensFlare.Position(eye, azimuth, latitude);
            var visibility = Gw2SkyCardLensFlare.Visibility(state.VisiblePixels, state.TotalPixels, constants.DayWeight, constants.Intensity);
            DrawLensFlare(state, lensFlare, position, visibility, eye, cameraRight, cameraUp, cameraForward);
            QueryOcclusion(state, CardWorld(shape, look, eye) * viewProjection);
        }
    }

    private void DrawLensFlare(GrLensFlareState state, GrSkyCardLensFlare lensFlare, Vector3 position, float visibility, Vector3 eye, Vector3 cameraRight, Vector3 cameraUp, Vector3 cameraForward)
    {
        var flare = lensFlare.Flare;
        if (visibility <= 0f || flare.Atoms.Count == 0) return;
        var built = Gw2SkyCardLensFlare.Build(flare, position, visibility, _flipbookSeconds, eye, cameraRight, cameraUp, cameraForward);
        var vertices = built.Select(vertex => new GrVertexLensFlare
        {
            Position = new Vector3(vertex.Position.X, vertex.Position.Y, vertex.Position.Z),
            Color = new Vector4(vertex.Color.X, vertex.Color.Y, vertex.Color.Z, vertex.Color.W),
            Uv = new Vector2(vertex.Uv.X, vertex.Uv.Y),
            NextUv = new Vector2(vertex.NextUv.X, vertex.NextUv.Y),
            Blend = new Vector2(vertex.Blend, 0f),
        }).ToArray();
        state.Vertices.SetData<GrVertexLensFlare>(vertices);
        var inputs = new Dictionary<uint, Vector4>
        {
            [Gw2SkyCardLensFlare.FadeBandToken] = new(flare.FadeBand.X, flare.FadeBand.Y, 0f, 0f),
            [Gw2SkyCardLensFlare.FlareWorldToken] = new(position.X, position.Y, position.Z, 1f),
        };
        foreach (var pass in new[] { lensFlare.ShaderMaterial }.Concat(lensFlare.ShaderMaterial.LaterPasses))
        {
            GrDrawConstants.Apply(pass, _drawFrame, new GrDrawItem(Matrix4x4.Identity, Vector4.One, 0, inputs));
            GrDrawTextures.Bind(_device, pass, sampler =>
                !sampler.IsEngineTexture && sampler.ResourceIndex < lensFlare.Textures.Count ? lensFlare.Textures[sampler.ResourceIndex] ?? _transparent : _transparent, anisotropicFiltering: false);
            ApplyMaterialPipeline(pass, GrVertexLensFlare.Layout);
            _graphicsDevice.SetVertexBuffer(state.Vertices);
            _graphicsDevice.SetIndexBuffer(state.Indices);
            _graphicsDevice.DrawIndexed(flare.Atoms.Count * Gw2SkyCardLensFlare.IndicesPerAtom);
        }
    }

    private void QueryOcclusion(GrLensFlareState state, Matrix4x4 discWorldViewProjection)
    {
        state.CollectResults();
        if (state.IsQuerying) return;
        var parameters = _skyCard.Parameters;
        parameters["WorldViewProjection"]?.SetValue(GrMatrix.ToRegisterRows(discWorldViewProjection));
        parameters["ss0"]?.SetValue(_transparent);
        parameters["ss1"]?.SetValue(_transparent);
        parameters["ss0Sampler"]?.SetValue(_device.Sampler(SamplerDescription.LinearClamp));
        parameters["ss1Sampler"]?.SetValue(_device.Sampler(SamplerDescription.LinearClamp));
        foreach (var (query, depthStencil) in new[] { (state.TotalQuery, DepthStencilState.None), (state.VisibleQuery, DepthStencilState.DepthRead) })
        {
            _device.ApplyPipeline(_skyCard, NoColorWrite, depthStencil, RasterizerState.CullNone, GrVertexSkyCard.Layout);
            _graphicsDevice.SetVertexBuffer(_occlusionDisc);
            _graphicsDevice.SetIndexBuffer(_occlusionDiscIndices);
            query.Begin();
            _graphicsDevice.DrawIndexed(OcclusionDiscSides * 3);
            query.End();
        }
        state.IsQuerying = true;
    }

    private sealed class GrLensFlareState : IDisposable
    {
        public GrLensFlareState(GraphicsDevice device, int atomCount)
        {
            var quadCount = Math.Max(atomCount, 1);
            Vertices = GrBuffers.Vertex<GrVertexLensFlare>(device, new GrVertexLensFlare[quadCount * Gw2SkyCardLensFlare.VerticesPerAtom], "LensFlareVertices");
            Indices = GrBuffers.Index(device, Gw2SkyCardLensFlare.Indices(quadCount), "LensFlareIndices");
            TotalQuery = new OcclusionQuery(device, "LensFlareTotal");
            VisibleQuery = new OcclusionQuery(device, "LensFlareVisible");
        }

        public GraphicsBuffer Vertices { get; }

        public GraphicsBuffer Indices { get; }

        public OcclusionQuery TotalQuery { get; }

        public OcclusionQuery VisibleQuery { get; }

        public bool IsQuerying { get; set; }

        public long TotalPixels { get; private set; }

        public long VisiblePixels { get; private set; }

        public void CollectResults()
        {
            if (!IsQuerying || !TotalQuery.IsComplete || !VisibleQuery.IsComplete) return;
            TotalPixels = TotalQuery.PixelCount;
            VisiblePixels = VisibleQuery.PixelCount;
            IsQuerying = false;
        }

        public void Dispose()
        {
            Vertices.Dispose();
            Indices.Dispose();
            TotalQuery.Dispose();
            VisibleQuery.Dispose();
        }
    }

    public static bool IsShown(uint flags, float eyeZ, float waterSurfaceZ) =>
        !((flags & AboveWaterOnly) != 0 && eyeZ >= waterSurfaceZ) && !((flags & UnderwaterOnly) != 0 && waterSurfaceZ > eyeZ);

    public void Dispose()
    {
        foreach (var buffer in _vertices) buffer.Dispose();
        _indices.Dispose();
        foreach (var state in _lensFlares) state?.Dispose();
        _occlusionDisc.Dispose();
        _occlusionDiscIndices.Dispose();
        _transparent.Dispose();
    }
}
