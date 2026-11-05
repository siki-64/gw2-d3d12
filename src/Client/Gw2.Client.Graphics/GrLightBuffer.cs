using System;
using System.Collections.Generic;
using System.Numerics;
using Crystal;
using Crystal.Graphics;
using Gw2EnvironmentShLighting = Gw2.Dat.Content.Gw2EnvironmentShLighting;
using Gw2MapShadowConstants = Gw2.Dat.Content.Gw2MapShadowConstants;
using Gw2PointLightFrame = Gw2.Dat.Content.Gw2PointLightFrame;
using Gw2SpotLightFrame = Gw2.Dat.Content.Gw2SpotLightFrame;

namespace Gw2.Client.Graphics;

public sealed class GrLightBuffer : IDisposable
{
    private const int ShadowNearEngineTexture = 7;
    private const int ShadowEngineTexture = 5;

    private static readonly Color NormalSpecClearColor = new(128, 128, 255, 0);

    private readonly GrDevice _device;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly GrEngineTextures _engineTextures;
    private readonly ShaderProgram _accumLightSunSh;
    private readonly ShaderProgram _accumLightSunShOcclusion;
    private readonly ShaderProgram _accumLightPoint;
    private readonly ShaderProgram _accumLightSpot;
    private Texture2D? _normalSpec;
    private Texture2D? _depth;
    private Texture2D? _lightBuffer;

    public GrLightBuffer(GrDevice device, GrEngineTextures engineTextures)
    {
        _device = device;
        _graphicsDevice = device.GraphicsDevice;
        _engineTextures = engineTextures;
        var sunSh = device.LoadShader("GrAccumLightSunSh");
        _accumLightSunSh = sunSh.GetProgram("AccumLightSunSh");
        _accumLightSunShOcclusion = sunSh.GetProgram("AccumLightSunShOcclusion");
        _accumLightPoint = device.LoadShader("GrAccumLightPoint").GetProgram("AccumLightPoint");
        _accumLightSpot = device.LoadShader("GrAccumLightSpot").GetProgram("AccumLightSpot");
        SceneDepth = new GrSceneDepth(device);
    }

    public GrSceneDepth SceneDepth { get; }

    public Texture2D? Depth => _depth;

    public Texture2D? NormalSpec => _normalSpec;

    public Texture2D? Texture => _lightBuffer;

    public void BeginNormalSpec()
    {
        EnsureTargets();
        _device.SetRenderTarget(_normalSpec!, _depth);
        _graphicsDevice.Clear(NormalSpecClearColor);
        _graphicsDevice.ClearDepthStencil();
    }

    private static void SetContactShadowConstants(ShaderParameterCollection parameters, GrContactShadowSettings contactShadows, Matrix4x4 view, Matrix4x4 projection, float cameraNear, float cameraFar)
    {
        Matrix4x4.Invert(projection, out var inverseProjection);
        var worldToView = GrMatrix.ToRegisterRows(view);
        parameters["DepthToViewZ"]?.SetValue(new Vector4(-cameraNear * cameraFar / (cameraFar - cameraNear), -cameraFar / (cameraFar - cameraNear), 0f, 0f));
        parameters["ContactShadowsSettings"]?.SetValue(contactShadows.SettingsConstant);
        parameters["ContactShadowsDebugSettings"]?.SetValue(contactShadows.DebugSettingsConstant);
        parameters["ContactShadowsSettings2"]?.SetValue(contactShadows.Settings2Constant);
        parameters["ContactShadowsWorldToViewA"]?.SetValue(worldToView[0]);
        parameters["ContactShadowsWorldToViewB"]?.SetValue(worldToView[1]);
        parameters["ContactShadowsWorldToViewC"]?.SetValue(worldToView[2]);
        parameters["ContactShadowsProjection"]?.SetValue(MatrixRows(projection));
        parameters["ContactShadowsProjectionInv"]?.SetValue(MatrixRows(inverseProjection));
    }

    private static Vector4[] MatrixRows(Matrix4x4 matrix) =>
    [
        new(matrix.M11, matrix.M12, matrix.M13, matrix.M14),
        new(matrix.M21, matrix.M22, matrix.M23, matrix.M24),
        new(matrix.M31, matrix.M32, matrix.M33, matrix.M34),
        new(matrix.M41, matrix.M42, matrix.M43, matrix.M44),
    ];

    private void SetScreenConstants(ShaderParameterCollection parameters, Matrix4x4 clipToWorld, Vector3 eye, float cameraNear, float cameraFar)
    {
        parameters["LightBuffer"]?.SetValue(GrEngineConstants.LightBuffer);
        parameters["TexelOffset"]?.SetValue(GrEngineConstants.TexelOffset);
        parameters["ScreenDimsInv"]?.SetValue(GrEngineConstants.ScreenDims(_lightBuffer!.Width, _lightBuffer.Height, cameraNear, cameraFar));
        parameters["ClipToWorld"]?.SetValue(GrMatrix.ToRegisterRows(clipToWorld));
        parameters["WorldCameraPos"]?.SetValue(new Vector4(eye.X, eye.Y, eye.Z, 1f));
        parameters["ssNormalSpec"]?.SetValue(_normalSpec);
        parameters["ssNormalSpecSampler"]?.SetValue(_device.Sampler(SamplerDescription.PointClamp));
        parameters["ssDepth"]?.SetValue(_depth);
        parameters["ssDepthSampler"]?.SetValue(_device.Sampler(SamplerDescription.PointClamp));
    }

    public void Accumulate(Matrix4x4 view, Matrix4x4 projection, Vector3 eye, float cameraNear, float cameraFar, Gw2EnvironmentShLighting? lighting, GrShadowMap? shadowMap, IReadOnlyList<GrShadowCascade> cascades, Gw2MapShadowConstants? farShadow, IReadOnlyList<Gw2PointLightFrame> pointLights, IReadOnlyList<Gw2SpotLightFrame> spotLights, GrContactShadowSettings contactShadows, Texture2D? occlusion = null, Vector4 postLightConstants = default)
    {
        var viewProjection = view * projection;
        _device.SetRenderTarget(_lightBuffer!, null);
        _graphicsDevice.Clear(Color.Transparent);
        var clipToWorld = GrMatrix.ClipToWorld(view, projection);
        var sunData = lighting?.ShSunData ?? default;
        var program = occlusion is not null ? _accumLightSunShOcclusion : _accumLightSunSh;
        var parameters = program.Parameters;
        SetScreenConstants(parameters, clipToWorld, eye, cameraNear, cameraFar);
        parameters["PostLightShRed"]?.SetValue(lighting?.ShRed ?? default);
        parameters["PostLightShGreen"]?.SetValue(lighting?.ShGreen ?? default);
        parameters["PostLightShBlue"]?.SetValue(lighting?.ShBlue ?? default);
        parameters["PostLightShSun"]?.SetValue(new Vector4(sunData.X, sunData.Y, sunData.Z, 0f));
        parameters["PostLightShSunColor"]?.SetValue(lighting?.ShSunColor ?? default);
        parameters["PostLightShSunDir"]?.SetValue(sunData);
        parameters["ssShadowNear"]?.SetValue(_engineTextures.Resolve(ShadowNearEngineTexture));
        parameters["ssShadowNearSampler"]?.SetValue(_device.Sampler(SamplerDescription.PointClamp));
        parameters["ssShadow"]?.SetValue(_engineTextures.Resolve(ShadowEngineTexture));
        parameters["ssShadowSampler"]?.SetValue(_device.Sampler(SamplerDescription.LinearWrap));
        parameters["WorldToShadowA"]?.SetValue(farShadow?.WorldToShadowA ?? default);
        parameters["WorldToShadowB"]?.SetValue(farShadow?.WorldToShadowB ?? default);
        parameters["WorldToShadowC"]?.SetValue(farShadow?.WorldToShadowC ?? default);
        parameters["WorldToShadowD"]?.SetValue(farShadow?.WorldToShadowD ?? default);
        SetCascadeConstants(parameters, shadowMap, cascades);
        SetContactShadowConstants(parameters, contactShadows, view, projection, cameraNear, cameraFar);
        parameters["ssOcclusion"]?.SetValue(occlusion);
        parameters["ssOcclusionSampler"]?.SetValue(_device.Sampler(SamplerDescription.PointClamp));
        parameters["PostLightConstants"]?.SetValue(postLightConstants);
        _device.ApplyPipeline(program, BlendState.Opaque, DepthStencilState.None, RasterizerState.CullNone, GrFullScreenTriangle.Layout);
        GrFullScreenTriangle.Draw(_graphicsDevice);

        AccumulatePointLights(viewProjection, clipToWorld, eye, cameraNear, cameraFar, pointLights);
        AccumulateSpotLights(viewProjection, clipToWorld, eye, cameraNear, cameraFar, spotLights);
        _device.SetBackBuffer();
    }

    private void AccumulatePointLights(Matrix4x4 viewProjection, Matrix4x4 clipToWorld, Vector3 eye, float cameraNear, float cameraFar, IReadOnlyList<Gw2PointLightFrame> pointLights)
    {
        if (pointLights.Count == 0) return;

        var parameters = _accumLightPoint.Parameters;
        SetScreenConstants(parameters, clipToWorld, eye, cameraNear, cameraFar);
        var frustum = new BoundingFrustum(viewProjection);
        foreach (var light in pointLights)
        {
            if (!light.Enabled || light.FarDistance <= 0f) continue;
            var color = light.ScaledColor;
            if (color == Vector3.Zero) continue;
            var center = new Vector3(light.Position.X, light.Position.Y, light.Position.Z);
            if (frustum.Contains(new BoundingSphere(center, light.FarDistance)) == ContainmentType.Disjoint) continue;

            var range = light.FarDistance - light.NearDistance;
            parameters["PointLightPosition"]?.SetValue(new Vector4(center, light.FarDistance));
            parameters["PointLightColor"]?.SetValue(new Vector4(MathF.Abs(color.X), MathF.Abs(color.Y), MathF.Abs(color.Z), range == 0f ? 1f : 1f / range));
            DrawScissored(_accumLightPoint, color, ScreenBoundsOf(viewProjection, light.Position, light.FarDistance));
        }

        _graphicsDevice.ScissorRectangle = FullTarget();
    }

    private void AccumulateSpotLights(Matrix4x4 viewProjection, Matrix4x4 clipToWorld, Vector3 eye, float cameraNear, float cameraFar, IReadOnlyList<Gw2SpotLightFrame> spotLights)
    {
        if (spotLights.Count == 0) return;

        var parameters = _accumLightSpot.Parameters;
        SetScreenConstants(parameters, clipToWorld, eye, cameraNear, cameraFar);
        var frustum = new BoundingFrustum(viewProjection);
        foreach (var light in spotLights)
        {
            if (!light.Enabled || light.FarDistance <= 0f || light.Direction == Vector3.Zero) continue;
            var color = light.ScaledColor;
            if (color == Vector3.Zero) continue;
            var center = new Vector3(light.Position.X, light.Position.Y, light.Position.Z);
            if (frustum.Contains(new BoundingSphere(center, light.FarDistance)) == ContainmentType.Disjoint) continue;

            parameters["SpotLightPosition"]?.SetValue(new Vector4(center, 1f));
            parameters["SpotLightDirection"]?.SetValue(new Vector4(light.Direction.X, light.Direction.Y, light.Direction.Z, light.FarDistance));
            parameters["SpotLightColor"]?.SetValue(new Vector4(MathF.Abs(color.X), MathF.Abs(color.Y), MathF.Abs(color.Z), 1f));
            parameters["SpotLightAttenuation"]?.SetValue(SpotAttenuationOf(light));
            DrawScissored(_accumLightSpot, color, ScreenBoundsOf(viewProjection, light.Position, light.FarDistance));
        }

        _graphicsDevice.ScissorRectangle = FullTarget();
    }

    private void DrawScissored(ShaderProgram program, Vector3 color, Rectangle bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        var blend = color.X > 0f || color.Y > 0f || color.Z > 0f ? GrBlendStates.AdditiveOneOne : GrBlendStates.ReverseSubtractOneOne;
        _graphicsDevice.ScissorRectangle = bounds;
        _device.ApplyPipeline(program, blend, DepthStencilState.None, RasterizerState.CullNone, GrFullScreenTriangle.Layout);
        GrFullScreenTriangle.Draw(_graphicsDevice);
    }

    private Rectangle FullTarget() => new(0, 0, _lightBuffer!.Width, _lightBuffer.Height);

    public static Vector4 SpotAttenuationOf(Gw2SpotLightFrame light)
    {
        const float MinimumRange = 0.001f;
        var cosInner = MathF.Cos(light.InnerAngle);
        var cosOuter = MathF.Cos(light.OuterAngle);
        var coneRange = MathF.Max(cosInner - cosOuter, MinimumRange);
        var distanceRange = MathF.Max(light.FarDistance - light.NearDistance, MinimumRange);
        return new Vector4(1f / coneRange, -cosOuter / coneRange, -1f / distanceRange, light.FarDistance / distanceRange);
    }

    private Rectangle ScreenBoundsOf(Matrix4x4 viewProjection, Vector3 center, float radius)
    {
        var width = _lightBuffer!.Width;
        var height = _lightBuffer.Height;
        var minimum = new Vector2(float.MaxValue);
        var maximum = new Vector2(float.MinValue);
        for (var corner = 0; corner < 8; corner++)
        {
            var offset = new Vector3((corner & 1) == 0 ? -radius : radius, (corner & 2) == 0 ? -radius : radius, (corner & 4) == 0 ? -radius : radius);
            var clip = Vector4.Transform(new Vector4(center + offset, 1f), viewProjection);
            if (clip.W <= 0f) return new Rectangle(0, 0, width, height);
            var screen = new Vector2((clip.X / clip.W * 0.5f + 0.5f) * width, (0.5f - clip.Y / clip.W * 0.5f) * height);
            minimum = Vector2.Min(minimum, screen);
            maximum = Vector2.Max(maximum, screen);
        }

        var left = Math.Clamp((int)MathF.Floor(minimum.X), 0, width);
        var top = Math.Clamp((int)MathF.Floor(minimum.Y), 0, height);
        var right = Math.Clamp((int)MathF.Ceiling(maximum.X), 0, width);
        var bottom = Math.Clamp((int)MathF.Ceiling(maximum.Y), 0, height);
        return new Rectangle(left, top, right - left, bottom - top);
    }

    internal static void SetCascadeConstants(ShaderParameterCollection parameters, GrShadowMap? shadowMap, IReadOnlyList<GrShadowCascade> cascades)
    {
        if (shadowMap is null || cascades.Count == 0)
        {
            parameters["ShadowMapConfiguration"]?.SetValue(GrShadowConstants.NoCascades);
            return;
        }

        var worldToShadow = new Vector4[GrShadowConstants.MaximumCascades * 4];
        var centers = new Vector4[GrShadowConstants.MaximumCascades];
        var radii = new float[GrShadowConstants.MaximumCascades];
        var biases = new float[GrShadowConstants.MaximumCascades];
        for (var index = 0; index < cascades.Count; index++)
        {
            GrMatrix.ToRegisterRows(cascades[index].WorldToShadow).CopyTo(worldToShadow, index * 4);
            var center = cascades[index].BoundSphereCenter;
            centers[index] = new Vector4(center.X, center.Y, center.Z, 0f);
            radii[index] = cascades[index].BoundSphereRadius;
            biases[index] = cascades[index].Bias;
        }

        parameters["ShadowMapConfiguration"]?.SetValue(GrShadowConstants.Configuration(cascades.Count, shadowMap.Texture.Width, shadowMap.Texture.Height));
        parameters["ShadowMapCascadeWorldToShadow"]?.SetValue(worldToShadow);
        parameters["ShadowMapCascadeBoundSphereCenters"]?.SetValue(centers);
        parameters["ShadowMapCascadeSplitRadii"]?.SetValue(new Vector4(radii[0], radii[1], radii[2], radii[3]));
        parameters["ShadowBias"]?.SetValue(new Vector4(biases[0], biases[1], biases[2], biases[3]));
    }

    public void EnsureTargets()
    {
        var (width, height) = _device.RenderTargetSize();
        if (_normalSpec is { } existing && existing.Width == width && existing.Height == height) return;
        DisposeTargets();
        _normalSpec = _device.CreateRenderTarget(width, height, SurfaceFormat.Rgba8Unorm, "NormalSpec", clearColor: NormalSpecClearColor);
        _depth = _device.CreateDepthTarget(width, height, "SceneDepth");
        _lightBuffer = _device.CreateRenderTarget(width, height, SurfaceFormat.Rgba16Float, "LightBuffer");
    }

    private void DisposeTargets()
    {
        _depth?.Dispose();
        _normalSpec?.Dispose();
        _lightBuffer?.Dispose();
    }

    public void Dispose() => DisposeTargets();
}
