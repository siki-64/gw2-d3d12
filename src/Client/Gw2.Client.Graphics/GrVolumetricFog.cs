using System;
using System.Collections.Generic;
using System.Numerics;
using Crystal.Graphics;
using Gw2EnvironmentFog = Gw2.Dat.Content.Gw2EnvironmentFog;
using Gw2EnvironmentShLighting = Gw2.Dat.Content.Gw2EnvironmentShLighting;
using Gw2MapShadowConstants = Gw2.Dat.Content.Gw2MapShadowConstants;

namespace Gw2.Client.Graphics;

public sealed class GrVolumetricFog : IDisposable
{
    private static readonly BlendState SubtractFromScene = new(new RenderTargetBlendDescription
    {
        BlendEnable = true,
        SourceBlend = Blend.One,
        DestinationBlend = Blend.One,
        BlendOperation = BlendOperation.ReverseSubtract,
        SourceBlendAlpha = Blend.Zero,
        DestinationBlendAlpha = Blend.One,
        BlendOperationAlpha = BlendOperation.Add,
    });

    private const int ShadowNearEngineTexture = 7;
    private const int ShadowEngineTexture = 5;
    private const float SunShareOfFogAcrossSun = 0.35f;
    private const float SunScatteringAnisotropy = 0.6f;
    private const float SunShareOfFogTowardSun = 0.8f;

    private readonly GrDevice _device;
    private readonly GrEngineTextures _engineTextures;
    private readonly ShaderProgram _march;
    private readonly ShaderProgram _composite;
    private Texture2D? _fogLight;
    private Vector4[] _clipToWorldRows = [];
    private Vector3 _eye;

    public GrVolumetricFog(GrDevice device, GrEngineTextures engineTextures)
    {
        _device = device;
        _engineTextures = engineTextures;
        _march = device.LoadShader("GrVolumetricFogMarch").GetProgram("March");
        _composite = device.LoadShader("GrVolumetricFogComposite").GetProgram("ShadowedFog");
    }

    public void March(Texture2D depth, Matrix4x4 view, Matrix4x4 projection, Vector3 eye, Gw2EnvironmentFog fog, Gw2EnvironmentShLighting? lighting, GrShadowMap? shadowMap, IReadOnlyList<GrShadowCascade> cascades, Gw2MapShadowConstants? farShadow)
    {
        var width = Math.Max(depth.Width / 2, 1);
        var height = Math.Max(depth.Height / 2, 1);
        if (_fogLight is null || _fogLight.Width != width || _fogLight.Height != height)
        {
            _fogLight?.Dispose();
            _fogLight = _device.CreateRenderTarget(width, height, SurfaceFormat.R16Float, "VolumetricFogLight");
        }

        var clipToWorld = GrMatrix.ClipToWorld(view, projection);
        _eye = eye;
        var parameters = _march.Parameters;
        parameters["ScreenDimsInv"]?.SetValue(new Vector4(1f / width, 1f / height, 0f, 0f));
        _clipToWorldRows = GrMatrix.ToRegisterRows(clipToWorld);
        parameters["ClipToWorld"]?.SetValue(_clipToWorldRows);
        parameters["WorldCameraPos"]?.SetValue(new Vector4(eye.X, eye.Y, eye.Z, 1f));
        SetFogConstants(parameters, fog);
        parameters["PostLightShSunDir"]?.SetValue(lighting?.ShSunData ?? default);
        parameters["WorldToShadowA"]?.SetValue(farShadow?.WorldToShadowA ?? default);
        parameters["WorldToShadowB"]?.SetValue(farShadow?.WorldToShadowB ?? default);
        parameters["WorldToShadowC"]?.SetValue(farShadow?.WorldToShadowC ?? default);
        GrLightBuffer.SetCascadeConstants(parameters, shadowMap, cascades);
        parameters["ssDepth"]?.SetValue(depth);
        parameters["ssDepthSampler"]?.SetValue(_device.Sampler(SamplerDescription.PointClamp));
        parameters["ssShadowNear"]?.SetValue(_engineTextures.Resolve(ShadowNearEngineTexture));
        parameters["ssShadowNearSampler"]?.SetValue(_device.Sampler(SamplerDescription.PointClamp));
        parameters["ssShadow"]?.SetValue(_engineTextures.Resolve(ShadowEngineTexture));
        parameters["ssShadowSampler"]?.SetValue(_device.Sampler(SamplerDescription.LinearWrap));

        var previousTargets = _device.BoundTargets;
        _device.SetRenderTarget(_fogLight, null);
        Draw(_march, BlendState.Opaque);
        _device.SetRenderTargets(previousTargets);
    }

    public void Composite(Texture2D depth, Gw2EnvironmentFog fog, Gw2EnvironmentShLighting? lighting)
    {
        if (_fogLight is null) return;
        var viewport = _device.GraphicsDevice.Viewport;
        var parameters = _composite.Parameters;
        parameters["ScreenDimsInv"]?.SetValue(new Vector4(1f / viewport.Width, 1f / viewport.Height, 0f, 0f));
        parameters["FogLightTexelSize"]?.SetValue(new Vector4(1f / _fogLight.Width, 1f / _fogLight.Height, 0f, 0f));
        parameters["ClipToWorld"]?.SetValue(_clipToWorldRows);
        parameters["WorldCameraPos"]?.SetValue(new Vector4(_eye.X, _eye.Y, _eye.Z, 1f));
        SetFogConstants(parameters, fog);
        parameters["PostLightShSunDir"]?.SetValue(lighting?.ShSunData ?? default);
        parameters["VolumetricFogSettings"]?.SetValue(new Vector4(SunShareOfFogAcrossSun, SunScatteringAnisotropy, SunShareOfFogTowardSun, 0f));
        parameters["ssDepth"]?.SetValue(depth);
        parameters["ssDepthSampler"]?.SetValue(_device.Sampler(SamplerDescription.PointClamp));
        parameters["ssFogLight"]?.SetValue(_fogLight);
        parameters["ssFogLightSampler"]?.SetValue(_device.Sampler(SamplerDescription.LinearClamp));
        Draw(_composite, SubtractFromScene);
    }

    private void Draw(ShaderProgram program, BlendState blendState)
    {
        _device.ApplyPipeline(program, blendState, DepthStencilState.None, RasterizerState.CullNone, GrFullScreenTriangle.Layout);
        GrFullScreenTriangle.Draw(_device.GraphicsDevice);
    }

    private static void SetFogConstants(ShaderParameterCollection parameters, Gw2EnvironmentFog fog)
    {
        parameters["FogColorNearMinusFar"]?.SetValue(fog.FogColorNearMinusFar);
        parameters["FogColorFar"]?.SetValue(fog.FogColorFar);
        parameters["FogColorHeight"]?.SetValue(fog.FogColorHeight);
        parameters["FogDepthCue"]?.SetValue(fog.FogDepthCue);
        parameters["FogParam0"]?.SetValue(fog.FogParam0);
    }

    public void Dispose() => _fogLight?.Dispose();
}
