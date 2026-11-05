using Gw2.Formats;
using Gw2.Client.Settings;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Gw2.Client.Content;
using Gw2.Client.Scene;
using Gw2.Client.Hud;
using Gw2.Client.World;
using Gw2.Dat.Content;
using Gw2.Dat.Archive;
using Gw2.Client.Graphics;
using Crystal;
using GameTime = Crystal.GameTime;
using Crystal.Graphics;

namespace Gw2.Client;

public sealed partial class Gw2Client
{
    private readonly Dictionary<uint, Gw2ColorLookup?> _colorLookupFiles = [];
    private IReadOnlyList<(uint? ClutFileId, float Weight)> _colorLookupWeights = [];
    private Gw2ColorLookup? _colorLookup;
    private Gw2TerrainLoader? _terrainLoader;
    private Gw2PropLoader? _propLoader;
    private readonly List<(CharacterAttachment Placement, Gw2AttachedModel Drawn)> _attachments = new();
    private Gw2WaterLoader? _water;
    private GrDevice? _device;
    private GrCharacterPass? _characterPass;
    private GrAgentCapsules? _agentCapsules;
    private Gw2AgentNameLabels? _agentNameLabels;
    private Gw2NameplateHealthBars? _nameplateHealthBars;
    private RetainedHud? _retainedHud;
    private readonly List<Gw2AgentScreenAnchor> _agentAnchors = [];
    private readonly List<Gw2SceneCharacter> _sceneCharacters = [];
    private readonly List<Gw2SceneNamedObject> _sceneNamedObjects = [];
    private GrPostProcessCombine? _postProcessCombine;
    private readonly Gw2ScreenOverlay _screenOverlay = new();
    private readonly Gw2EnvironmentDistortion _environmentDistortion = new();
    private GrScreenCopy? _debugViewCopy;
    private GrDebugView _debugView;
    private GrHemicubeSkybox? _skybox;
    private GrHemicubeFaceTextures? _daySky;
    private GrHemicubeFaceTextures? _nightSky;
    private readonly Dictionary<Gw2MapEnvironment, EnvironmentClouds?> _cloudsByEnvironment = new(ReferenceEqualityComparer.Instance);
    private GrSkyCards? _skyCards;
    private GrStarField? _starField;
    private Gw2ParticleFieldScene? _particleFields;
    private float _viewDistance = Gw2MapTerrain.DefaultViewDistance(Gw2MapTerrain.DefaultTilesPerChunk);
    private Gw2MapEnvSkyCard[] _skyCardSources = [];
    private GrSkyCardLook[] _skyCardLooks = [];
    private GrSkyCardConstants _skyCardConstants;
    private Vector2? _lightRayScreenPosition;
    private readonly List<Texture2D> _skyTextures = new();
    private double _terrainUpdateMilliseconds;
    private double _waterAdvanceMilliseconds;
    private double _propUpdateMilliseconds;
    private double _propLevelMilliseconds;

    partial void LoadGraphics()
    {
        _device = new GrDevice(GraphicsDevice)
        {
            DecompiledShaderDirectory = _options.Debug.DecompiledShaderDirectory,
            MaterialShaderCache = new GrMaterialShaderCache(GrMaterialShaderCache.DefaultRootDirectory),
        };
        _characterPass = new GrCharacterPass(_device, _options.Debug.ArchiveShaders ? _archiveMaterials!.Of : null, _options.Graphics.ShadowSettings);
        _characterPass.DynamicShadows = _options.Graphics.DynamicShadows;
        _characterPass.AnisotropicFiltering = _options.Graphics.AnisotropicFiltering;
        _characterPass.ShaderQuality = _options.Graphics.ShaderQuality;
        _characterPass.Reflections = _options.Graphics.Reflections;
        _characterPass.GpuDriven = _options.Graphics.GpuDriven;
        _characterPass.HiZ = _options.Graphics.HiZ;
        _characterPass.ParallelRecording = _options.Graphics.ParallelRecording;
        _characterPass.VolumetricFog = _options.Graphics.VolumetricFog;
        _characterPass.AmbientOcclusion = _options.Graphics.PostProcessing.HasFlag(Gw2PostProcessingOptions.AmbientOcclusion);
        _characterPass.FrameTimer = _frameTimer;
        _characterPass.DrawReflectionSky = (view, projection, eye) =>
        {
            DrawSky(view, projection, eye);
            DrawStarField(view, projection, eye);
            DrawSkyCards(view, projection, eye, 0f);
        };
        SetDebugView(_options.Debug.DebugView);
        _characterPass.AgentCapsules = _agentCapsules = new GrAgentCapsules(_device);
        if (_reader is not null)
        {
            _agentNameLabels = new Gw2AgentNameLabels(_device, _reader);
            _nameplateHealthBars = new Gw2NameplateHealthBars(_device, _reader);
            _retainedHud = new RetainedHud(_device, _reader);
        }
    }

    private void SetDebugView(GrDebugView view)
    {
        _debugView = view;
        _characterPass?.SetDebugView(view);
        if (view is GrDebugView.ShadowCascades or GrDebugView.Normals or GrDebugView.AmbientOcclusion && _device is not null) _debugViewCopy ??= new GrScreenCopy(_device);
    }

    partial void LoadMapGraphics(Gw2MapBundle map, Gw2MapEnvironment environment, uint environmentMapFileId)
    {
        _characterPass!.SetEnvironmentLighting(Gw2EnvironmentShLighting.FromZones(environment, Gw2EnvironmentZoneWeights.At(environment, _options.Scene.Position), _dayWeight));
        _characterPass.SetPointLights(_pointLightFrames);
        _characterPass.SetSpotLights(_spotLightFrames);
        _characterPass.SetEnvironmentFog(Gw2EnvironmentFog.FromZones(environment, Gw2EnvironmentZoneWeights.At(environment, _options.Scene.Position), _dayWeight));
        _characterPass.SetEnvironmentAmbientOcclusion(Gw2EnvironmentAmbientOcclusion.FromZones(environment, Gw2EnvironmentZoneWeights.At(environment, _options.Scene.Position), _dayWeight));
        if (environment.Effect is not null)
        {
            _device!.RenderScale = Gw2RenderScale.OfQuality((int)_options.Graphics.RenderSampling);
            _postProcessCombine = new GrPostProcessCombine(_device) { SceneClearColor = BackgroundColor, PhysicalBloom = _options.Graphics.PhysicalBloom, Gamma = _options.Graphics.Gamma };
        }
        _viewDistance = map.Terrain?.ViewDistance ?? Gw2MapTerrain.DefaultViewDistance(Gw2MapTerrain.DefaultTilesPerChunk);
        LoadSky(environment);
        _particleFields = new Gw2ParticleFieldScene(_device!, LoadSkyTexture);
        if (_reader!.ReadMapShadow(environmentMapFileId) is { } shadow)
            _characterPass.SetFarShadow(shadow, _viewDistance, _reader.ReadMapExpansionProperties(environmentMapFileId)?.CombinesShadowSamples ?? false);
        if (_options.Debug.ArchiveShaders)
        {
            _terrainLoader = Gw2TerrainLoader.For(GraphicsDevice, _content!, _archiveMaterials!, _characterPass, map);
            if (_login is null) _terrainLoader?.LoadAll(_options.Scene.Position);
            MarkStartup("terrain");
            _water = Gw2WaterLoader.Load(_device!, _reader, map, Gw2OrbitCamera.FarPlane, _dayWeight, _options.Scene.Position, _options.Debug.LowQualityWater);
            if (_water is not null) _water.Water.CullsHiddenWater = !_options.Debug.VanillaWaterGate;
            _characterPass.SetWater(_water?.Water, Gw2OrbitCamera.FarPlane);
            MarkStartup("water");
            _propLoader = new Gw2PropLoader(GraphicsDevice, _content!, _archiveMaterials!, _characterPass, map, _options.Graphics.LodDistance, _options.Graphics.TextureQuality, _highResolutionFiles);
            AttachConfiguredModels(_propLoader);
            if (_login is null) _propLoader.LoadAll(_options.Scene.Position, Gw2OrbitCamera.FarPlane);
            MarkStartup("props");
        }
    }

    private void ReblendEnvironmentAtEye()
    {
        if (_environment is null) return;
        var zoneWeights = ZoneWeightsAtEye();
        _characterPass!.SetEnvironmentLighting(Gw2EnvironmentShLighting.FromZones(_environment, zoneWeights, _dayWeight));
        _characterPass.SetEnvironmentFog(Gw2EnvironmentFog.FromZones(_environment, zoneWeights, _dayWeight));
        _characterPass.SetEnvironmentAmbientOcclusion(Gw2EnvironmentAmbientOcclusion.FromZones(_environment, zoneWeights, _dayWeight));
        _water?.Reblend(_environment, zoneWeights, _dayWeight);
    }

    private Gw2MapEnvSkyDome? SkyDomeAtEye() =>
        _environment is null ? null : Gw2EnvironmentSkyBlend.SkyDomeFromZones(_environment, ZoneWeightsAtEye());

    private IReadOnlyList<Gw2EnvironmentZoneWeight> ZoneWeightsAtEye() => Gw2EnvironmentZoneWeights.At(_environment!, _camera.Eye);

    private void CombineEnvironmentEffect(GrPostProcessCombine combine, float elapsedSeconds)
    {
        var zoneWeights = ZoneWeightsAtEye();
        var baseGrade = _options.Graphics.PostProcessing.HasFlag(Gw2PostProcessingOptions.ColorTint) ? Gw2EnvironmentGrade.FromZones(_environment!, zoneWeights, _dayWeight) : null;
        var bloom = _options.Graphics.PostProcessing.HasFlag(Gw2PostProcessingOptions.Bloom) ? Gw2EnvironmentBloom.FromZones(_environment!, zoneWeights, _dayWeight) : null;
        _screenOverlay.Advance(elapsedSeconds);
        if (bloom is not null) bloom = bloom with { Glow = _screenOverlay.ApplyGlow(bloom.Glow) };
        var postProcessing = _options.Graphics.PostProcessing;
        var chain = new GrPostProcessChain(
            postProcessing.HasFlag(Gw2PostProcessingOptions.LightAdaptation) ? Gw2EnvironmentLightAdaptation.FromZones(_environment!, zoneWeights) : null,
            elapsedSeconds,
            postProcessing.HasFlag(Gw2PostProcessingOptions.DepthBlur) ? Gw2EnvironmentDepthBlur.FromZones(_environment!, zoneWeights, _dayWeight) : null,
            _characterPass?.DepthCopy,
            Gw2OrbitCamera.FarPlane,
            _lightRayScreenPosition is { } sunScreenPosition ? new Vector2(sunScreenPosition.X, sunScreenPosition.Y) : null);
        combine.Distortion = postProcessing.HasFlag(Gw2PostProcessingOptions.Distortion) ? DistortionConstant(elapsedSeconds) : null;
        combine.Combine(_screenOverlay.Apply(baseGrade ?? Gw2EnvironmentGrade.Neutral), bloom, postProcessing.HasFlag(Gw2PostProcessingOptions.ColorGrading) ? ColorLookupAt(zoneWeights) : null, chain);
    }

    private Vector4 DistortionConstant(float elapsedSeconds)
    {
        _environmentDistortion.Advance(elapsedSeconds);
        var underwater = _water is not null && Gw2EnvironmentDistortion.IsUnderwater(_camera.Eye, _water.SurfaceZ);
        var baseAmount = Gw2EnvironmentDistortion.BaseAmount(_water?.UnderwaterDistortAmount ?? 0f, underwater);
        var viewDirection = Vector3.Normalize(_camera.Target - _camera.Eye);
        var constant = _environmentDistortion.Constant(_screenOverlay.ApplyDistortion(baseAmount), Gw2EnvironmentDistortion.ViewOffset(viewDirection, underwater));
        return new Vector4(constant.X, constant.Y, constant.Z, constant.W);
    }

    private Gw2ColorLookup ColorLookupAt(IReadOnlyList<Gw2EnvironmentZoneWeight> zoneWeights)
    {
        var weights = Gw2EnvironmentColorLookup.WeightsFromZones(_environment!, zoneWeights, _dayWeight);
        if (_colorLookup is not null && weights.SequenceEqual(_colorLookupWeights)) return _colorLookup;
        _colorLookupWeights = weights;
        _colorLookup = Gw2ColorLookup.Blend(weights.Select(weighted => (ColorLookupFile(weighted.ClutFileId), weighted.Weight)).ToList());
        return _colorLookup;
    }

    private Gw2ColorLookup ColorLookupFile(uint? fileId)
    {
        if (fileId is not { } id) return Gw2ColorLookup.Identity;
        if (!_colorLookupFiles.TryGetValue(id, out var lookup)) _colorLookupFiles[id] = lookup = Gw2ColorLookup.FromDds(_reader!.ReadBytes(id));
        return lookup ?? Gw2ColorLookup.Identity;
    }

    partial void DrawScene(GameTime gameTime)
    {
        var projection = _camera.Projection(GraphicsDevice.Viewport.AspectRatio);
        var loaderTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        _terrainLoader?.Update(_camera.Eye);
        _terrainUpdateMilliseconds = ElapsedMillisecondsSince(ref loaderTimestamp);
        ReblendEnvironmentAtEye();
        _water?.Advance(AnimationSeconds(gameTime), _camera.Eye, new Vector2(_camera.Target.X - _camera.Eye.X, _camera.Target.Y - _camera.Eye.Y));
        _waterAdvanceMilliseconds = ElapsedMillisecondsSince(ref loaderTimestamp);
        _propLoader?.Update(_camera.Eye, Gw2OrbitCamera.FarPlane);
        PlaceAttachments();
        _propUpdateMilliseconds = ElapsedMillisecondsSince(ref loaderTimestamp);
        var lodDistanceFactor = Gw2LodDistanceSetting.DistanceFactor(_options.Graphics.LodDistance);
        foreach (var prop in _propLoader?.LoadedProps ?? []) prop.UpdateLevel(_camera.Eye, lodDistanceFactor);
        _propLevelMilliseconds = ElapsedMillisecondsSince(ref loaderTimestamp);
        if (_characterPass is not null) _characterPass.AdvanceScatterFade(_camera.Eye, (float)gameTime.ElapsedGameTime.TotalSeconds);
        _frameTimer?.Mark(GrFrameSection.SceneUpdate);
        if (_postProcessCombine is not null) _postProcessCombine.BeginScene();
        else _device!.SetBackBuffer();
        var animationSeconds = AnimationSeconds(gameTime);
        void DrawSceneSky()
        {
            GraphicsDevice.Clear(BackgroundColor);
            GraphicsDevice.ClearDepthStencil();
            DrawSky(_camera.View, projection, _camera.Eye);
            DrawStarField(_camera.View, projection, _camera.Eye);
            DrawSkyCards(_camera.View, projection, _camera.Eye, animationSeconds);
        }
        if (_characterPass is not null)
        {
            _characterPass.Draw(CharacterWorld, _camera.View, projection, _camera.Eye, _camera.NearPlane, Gw2OrbitCamera.FarPlane, _device!.BoundTargets, DrawSceneSky, _postProcessCombine is { } combine ? combine.CopyScene : null);
        }
        else
        {
            DrawSceneSky();
            _frameTimer?.Mark(GrFrameSection.Sky);
        }
        DrawClouds(projection, AnimationSeconds(gameTime));
        DrawParticleFields(projection, AnimationSeconds(gameTime));
        if (_skyCardLooks.Length != 0) _skyCards?.DrawLensFlares(_camera.View, projection, _camera.Eye, _water?.SurfaceZ ?? float.PositiveInfinity, _skyCardLooks, _skyCardConstants);
        DrawLightRaySources(projection);
        if (_postProcessCombine is not null) CombineEnvironmentEffect(_postProcessCombine, AnimationSeconds(gameTime));
        if (_debugViewCopy is not null && _characterPass?.DebugViewTexture(_debugView) is { } debugViewTexture) _debugViewCopy.Present(debugViewTexture);
        if (_agentNameLabels is not null)
        {
            _agentNameLabels.CollectAnchors(_sceneCharacters, _camera.View * projection, _characterPosition, _targetSelection?.TargetAgentId, new Vector2(GraphicsDevice.BackBufferWidth, GraphicsDevice.BackBufferHeight), _agentAnchors);
            _agentNameLabels.CollectNamedObjectAnchors(_sceneNamedObjects, _camera.View * projection, _characterPosition, new Vector2(GraphicsDevice.BackBufferWidth, GraphicsDevice.BackBufferHeight), _agentAnchors);
            _nameplateHealthBars?.Draw(_agentAnchors, NameplateHealthOf, _characterPass?.SceneDepth);
            _agentNameLabels.Draw(_agentAnchors, _characterPass?.SceneDepth);
        }
        if (_retainedHud is not null && _hudState is { } hudState)
        {
            _device!.SetBackBuffer();
            _retainedHud.Draw(hudState, GraphicsDevice.BackBufferWidth, GraphicsDevice.BackBufferHeight);
        }
    }

    private Gw2NameplateHealth? NameplateHealthOf(uint agentId) =>
        _world.Vitals.HealthOf(agentId) is { Maximum: > 0f } health
            ? new Gw2NameplateHealth(health.Current / health.Maximum, _world.LifeState.StateOf(agentId) == Gw2.Client.Net.Game.LifeState.Downed)
            : null;

    private const int DaySkyMode = 0;
    private const int NightSkyMode = 1;

    private void LoadSky(Gw2MapEnvironment environment)
    {
        if (environment.Sky is not { Dome: not null } sky || sky.ModeTextures.Count <= NightSkyMode) return;
        _daySky = LoadSkyMode(sky.ModeTextures[DaySkyMode]);
        _nightSky = LoadSkyMode(sky.ModeTextures[NightSkyMode]);
        if (_daySky is not null && _nightSky is not null) _skybox = new GrHemicubeSkybox(_device!, _viewDistance);
        LoadStarField(sky.StarFile);
        LoadClouds(environment);
        LoadSkyCards(environment);
    }

    private const int CloudFadeAttributes = 2;

    private void LoadClouds(Gw2MapEnvironment environment) => CloudsOf(environment);

    private EnvironmentClouds? CloudsOf(Gw2MapEnvironment environment)
    {
        if (_cloudsByEnvironment.TryGetValue(environment, out var cached)) return cached;
        var shapes = new List<GrCloudLayerShape>();
        var layers = new List<Gw2MapEnvCloudLayer>();
        foreach (var layer in environment.Ambience?.CloudLayers ?? [])
        {
            if (layer.Attributes.Count <= CloudFadeAttributes || LoadSkyTexture(layer.Texture) is not { } texture) continue;
            var fade = layer.Attributes[CloudFadeAttributes];
            shapes.Add(new GrCloudLayerShape(layer.Altitude, layer.CutOut, layer.Depth, layer.Extent, layer.Scale, fade.FadeEnd, fade.FadeWidth, texture));
            layers.Add(layer);
        }

        var clouds = shapes.Count == 0 ? null : new EnvironmentClouds(new GrCloudLayers(_device!, shapes, _viewDistance), layers.ToArray());
        _cloudsByEnvironment[environment] = clouds;
        return clouds;
    }

    private sealed record EnvironmentClouds(GrCloudLayers Layers, Gw2MapEnvCloudLayer[] Sources);

    private void DrawClouds(Matrix4x4 projection, float elapsedSeconds)
    {
        if (_environment is null || SkyDomeAtEye() is not { } dome) return;
        var zoneWeights = ZoneWeightsAtEye();
        var lighting = Gw2EnvironmentShLighting.FromZones(_environment!, ZoneWeightsAtEye(), _dayWeight);
        var sun = Gw2EnvironmentLightBlend.FromZones(_environment!, ZoneWeightsAtEye(), _dayWeight).Lights.FirstOrDefault();
        var fog = Gw2EnvironmentFog.FromZones(_environment!, ZoneWeightsAtEye(), _dayWeight);
        var constants = new GrCloudConstants(
            fog?.FogColorNearMinusFar ?? default,
            fog?.FogColorFar ?? default,
            fog?.FogParam0 ?? default,
            lighting.ShRed,
            lighting.ShGreen,
            lighting.ShBlue,
            sun.Direction,
            sun.ScaledColor,
            Gw2EnvironmentSkyBlend.SkyDome(dome, _dayWeight).LightIntensity,
            1f);
        var viewport = GraphicsDevice.Viewport;
        foreach (var cloudEnvironment in Gw2EnvironmentSkyBlend.CloudEnvironmentsFromZones(_environment, zoneWeights))
        {
            if (CloudsOf(cloudEnvironment.Environment) is not { } clouds) continue;
            var looks = clouds.Sources
                .Select(layer => Gw2EnvironmentSkyBlend.WeightedByEnvironment(Gw2EnvironmentSkyBlend.CloudLayer(layer, _dayWeight)!.Value, cloudEnvironment.Weight))
                .Select(blend => new GrCloudLayerLook(blend.Brightness, blend.Density, blend.Haze, blend.LightIntensity, blend.Velocity)).ToArray();
            clouds.Layers.Draw(_camera.View, projection, _camera.Eye, elapsedSeconds, looks, constants, _characterPass?.SceneDepth, (int)viewport.Width, (int)viewport.Height, _camera.NearPlane, Gw2OrbitCamera.FarPlane);
        }
    }

    private void DrawParticleFields(Matrix4x4 projection, float elapsedSeconds)
    {
        if (_particleFields is null || _characterPass?.DepthCopy is not { } depthCopy) return;
        var forward = Vector3.Normalize(_camera.Target - _camera.Eye);
        _particleFields.Draw(_environment!, ZoneWeightsAtEye(), _camera.View, projection, _camera.Eye, forward, _water?.SurfaceZ ?? float.PositiveInfinity, _dayWeight, elapsedSeconds, depthCopy, _camera.NearPlane, Gw2OrbitCamera.FarPlane);
    }

    private void LoadSkyCards(Gw2MapEnvironment environment)
    {
        var shapes = new List<GrSkyCardShape>();
        var cards = new List<Gw2MapEnvSkyCard>();
        foreach (var card in environment.SkyCards)
        {
            if (card.Day is not { } day || card.Night is not { } night) continue;
            var dayTexture = LoadSkyTexture(day.Texture);
            var nightTexture = LoadSkyTexture(night.Texture);
            var material = LoadSkyCardMaterial(card.Material, (card.Flags & GrSkyCards.Additive) != 0);
            if (dayTexture is null && nightTexture is null && material is null) continue;
            shapes.Add(new GrSkyCardShape(card.Flags, card.Location, night.TextureUV, day.TextureUV, night.Speed, day.Speed, dayTexture, nightTexture, material, Gw2SkyCardTextureAnimator.Animates(card.Flags, card.Material?.TextureAnimation) ? new Gw2SkyCardTextureAnimator(card.Material!.TextureAnimation!) : null, Gw2SkyCardFlipbookFrame.Animates(card.Flags, card.Material?.Flipbook) ? card.Material!.Flipbook : null, LoadLensFlare(day.LensFlare), LoadLensFlare(night.LensFlare)));
            cards.Add(card);
        }

        if (shapes.Count == 0) return;
        _skyCardSources = cards.ToArray();
        _skyCards = new GrSkyCards(_device!, shapes, _viewDistance);
    }

    private GrSkyCardMaterial? LoadSkyCardMaterial(Gw2MapEnvSkyCardMaterial? material, bool additive)
    {
        if (material is null || material.Textures.Count == 0 || !material.MaterialFile.TryGetFileId(out var materialFileId)) return null;
        if (_reader!.ReadMaterial(materialFileId) is not { } amat) return null;
        var effectKey = additive ? Gw2AmatEffectKeys.SkyCardAdditive : Gw2AmatEffectKeys.Default;
        if (GrArchiveShaderMaterial.Create(_device!, amat, effectKey, shaderQuality: _options.Graphics.ShaderQuality) is not { } shaderMaterial) return null;
        var constants = material.Constants
            .Where(constant => constant.Value.Length >= 4)
            .GroupBy(constant => constant.Token)
            .ToDictionary(group => group.Key, group => new Vector4(group.Last().Value[0], group.Last().Value[1], group.Last().Value[2], group.Last().Value[3]));
        var textures = material.Textures.Select(texture => LoadSkyTexture(texture.File)).ToArray();
        var uvRects = Gw2SkyCardMaterialUvSets.Of(material.Textures.Select(texture => texture.TextureUV).ToArray());
        return new GrSkyCardMaterial(shaderMaterial, textures, uvRects, constants);
    }

    private GrSkyCardLensFlare? LoadLensFlare(Gw2MapEnvLensFlare? flare)
    {
        if (flare is null || flare.Atoms.Count == 0 || !flare.MaterialFile.TryGetFileId(out var materialFileId)) return null;
        if (_reader!.ReadMaterial(materialFileId) is not { } amat) return null;
        if (GrArchiveShaderMaterial.Create(_device!, amat, Gw2AmatEffectKeys.Default, shaderQuality: _options.Graphics.ShaderQuality) is not { } shaderMaterial) return null;
        return new GrSkyCardLensFlare(flare, shaderMaterial, flare.Textures.Select(LoadSkyTexture).ToArray());
    }

    private void DrawSkyCards(Matrix4x4 view, Matrix4x4 projection, Vector3 eye, float elapsedSeconds)
    {
        if (_skyCards is null || SkyDomeAtEye() is not { } dome) return;
        _skyCards.Advance(elapsedSeconds, _dayWeight);
        var looks = _skyCardSources.Select(card => Gw2EnvironmentSkyBlend.SkyCard(card, _dayWeight)!.Value)
            .Select(blend => new GrSkyCardLook(blend.Density, blend.Brightness, blend.HazeDensity, blend.LightIntensity, blend.MinHaze, blend.Scale, blend.Azimuth, blend.Latitude)).ToArray();
        var sky = Gw2EnvironmentSkyBlend.SkyDome(dome, _dayWeight);
        var lighting = Gw2EnvironmentShLighting.FromZones(_environment!, ZoneWeightsAtEye(), _dayWeight);
        var sun = Gw2EnvironmentLightBlend.FromZones(_environment!, ZoneWeightsAtEye(), _dayWeight).Lights.FirstOrDefault();
        var fog = Gw2EnvironmentFog.FromZones(_environment!, ZoneWeightsAtEye(), _dayWeight);
        var constants = new GrSkyCardConstants(
            fog?.FogColorFar ?? default,
            lighting.ShRed,
            lighting.ShGreen,
            lighting.ShBlue,
            sun.Direction,
            sun.ScaledColor,
            sky.HazeBottom,
            sky.HazeFalloff,
            sky.HazeDensity,
            sky.LightIntensity,
            1f,
            _dayWeight,
            GrEngineConstants.ClockSeconds(DateTime.UtcNow),
            fog);
        _skyCardLooks = looks;
        _skyCardConstants = constants;
        _skyCards.Draw(view, projection, eye, _water?.SurfaceZ ?? float.PositiveInfinity, looks, constants);
    }

    private void DrawLightRaySources(Matrix4x4 projection)
    {
        _lightRayScreenPosition = null;
        if (!_options.Graphics.PostProcessing.HasFlag(Gw2PostProcessingOptions.LightRays) || _skyCards is null || _skyCardLooks.Length == 0) return;
        var waterSurfaceZ = _water?.SurfaceZ ?? float.PositiveInfinity;
        _lightRayScreenPosition = _skyCards.LightRayScreenPosition(_camera.View, projection, _camera.Eye, waterSurfaceZ, _skyCardLooks, _skyCardConstants.Intensity);
        if (_lightRayScreenPosition is not null) _skyCards.DrawLightRaySources(_camera.View, projection, _camera.Eye, waterSurfaceZ, _skyCardLooks, _skyCardConstants);
    }

    private void LoadStarField(PackFileReference starFile)
    {
        if (!starFile.TryGetFileId(out var fileId) || Gw2StarField.TryParse(_reader!.ReadDocument(fileId)) is not { } stars || LoadSkyTexture(stars.Texture) is not { } atlas) return;
        _starField = new GrStarField(_device!, Gw2StarFieldMesh.Build(stars, _viewDistance * Gw2StarFieldMesh.RadiusOfViewDistance), atlas);
    }

    private void DrawStarField(Matrix4x4 view, Matrix4x4 projection, Vector3 eye)
    {
        if (_starField is null || SkyDomeAtEye() is not { } dome) return;
        var sky = Gw2EnvironmentSkyBlend.SkyDome(dome, _dayWeight);
        var time = GrEngineConstants.Time(GrEngineConstants.ClockSeconds(DateTime.UtcNow));
        _starField.Draw(view, projection, eye, new GrStarFieldConstants(sky.HazeBottom, sky.HazeFalloff, sky.HazeDensity, sky.StarDensity, time));
    }

    private GrHemicubeFaceTextures? LoadSkyMode(Gw2MapEnvSkyTex mode)
    {
        var northEast = LoadSkyTexture(mode.NorthEast);
        var southWest = LoadSkyTexture(mode.SouthWest);
        var top = LoadSkyTexture(mode.Top);
        return northEast is null || southWest is null || top is null ? null : new GrHemicubeFaceTextures(northEast, southWest, top);
    }

    private Texture2D? LoadSkyTexture(PackFileReference file)
    {
        if (!file.TryGetFileId(out var baseFileId)) return null;
        var fileId = _highResolutionFiles?.HighResolutionOf(baseFileId) ?? baseFileId;
        if (_reader!.ReadTexture(fileId) is not { } asset || GrTextureUpload.FromAsset(GraphicsDevice, asset) is not { } texture) return null;
        _skyTextures.Add(texture);
        return texture;
    }

    private void DrawSky(Matrix4x4 view, Matrix4x4 projection, Vector3 eye)
    {
        if (_skybox is null || SkyDomeAtEye() is not { } dome) return;
        var blend = Gw2EnvironmentSkyBlend.SkyDome(dome, _dayWeight);
        var lighting = Gw2EnvironmentShLighting.FromZones(_environment!, ZoneWeightsAtEye(), _dayWeight);
        var sun = Gw2EnvironmentLightBlend.FromZones(_environment!, ZoneWeightsAtEye(), _dayWeight).Lights.FirstOrDefault();
        var fog = Gw2EnvironmentFog.FromZones(_environment!, ZoneWeightsAtEye(), _dayWeight);
        var sunTravel = sun.Direction;
        var constants = new GrSkyConstants(
            fog?.FogColorFar ?? default,
            lighting.ShRed,
            lighting.ShGreen,
            lighting.ShBlue,
            new Vector4(blend.HazeBottom, blend.HazeFalloff, blend.HazeDensity, _dayWeight),
            new Vector4(sunTravel.X, sunTravel.Y, sunTravel.Z, blend.Brightness),
            new Vector4(sun.ScaledColor.X, sun.ScaledColor.Y, sun.ScaledColor.Z, blend.LightIntensity));
        _skybox.Draw(view, projection, eye, dome.VerticalOffset, _nightSky!.Value, _daySky!.Value, constants);
    }

    private void AttachConfiguredModels(Gw2PropLoader propLoader)
    {
        _attachments.Clear();
        if (_character is null) return;
        foreach (var option in _options.Content.Attachments.Concat(SkinAttachmentOptions()))
            _attachments.Add((_character.Attach(option.ModelFileId, option.SocketBoneName, option.GripBoneName), propLoader.AttachModel(option.ModelFileId)));
    }

    private IEnumerable<Gw2AttachmentOption> SkinAttachmentOptions()
    {
        if (_options.Content.SkinAttachments.Count == 0) return [];
        var skins = new Gw2Skins(_reader!.ContentIndex, SkinContentLayout.Current).All().ToDictionary(skin => skin.DataId);
        var options = new List<Gw2AttachmentOption>();
        foreach (var option in _options.Content.SkinAttachments)
        {
            if (!skins.TryGetValue(option.SkinId, out var skin) || skin.DefaultModelFileId is not { } modelFileId)
                throw new InvalidOperationException($"Skin {option.SkinId} has no model.");
            var socket = option.Stowed ? EquipmentSockets.StowedBone(skin, 0) : EquipmentSockets.HeldBone(skin, 0);
            if (socket is null) throw new InvalidOperationException($"Skin {option.SkinId} has no socket.");
            options.Add(new Gw2AttachmentOption(modelFileId, socket, EquipmentSockets.GripBone(socket)));
        }
        return options;
    }

    private Vector3? SurfaceBetween(Vector3 near, Vector3 far)
    {
        if (_havokWorld?.CastGameRay(near, far) is { } hit) return hit.Position;
        if (_propLoader?.Ground?.HeightAt(new Vector2(near.X, near.Y)) is not { } ground) return null;
        var position = new Vector3(near.X, near.Y, ground);
        return ground >= MathF.Min(near.Z, far.Z) && ground <= MathF.Max(near.Z, far.Z) ? position : null;
    }

    private Gw2.Client.Model.Gw2ParticleCamera ParticleCamera()
    {
        var forward = Vector3.Normalize(_camera.Target - _camera.Eye);
        var right = Vector3.Normalize(new Vector3(_camera.View.M11, _camera.View.M21, _camera.View.M31));
        var up = Vector3.Normalize(new Vector3(_camera.View.M12, _camera.View.M22, _camera.View.M32));
        return new Gw2.Client.Model.Gw2ParticleCamera(_camera.Eye, right, up, forward);
    }

    private void PlaceAttachments()
    {
        _propLoader?.AnimateProps();
        if (_propLoader is null) return;
        PlaceKeyframedAgentModels(_propLoader);
        PlaceSpeciesModels(_propLoader);
        SyncOwnWeapons(_propLoader);
        if (_attachments.Count == 0 && _weaponAttachments.Count == 0 && _keyframedAgentModels.Count == 0 && _speciesModels.Count == 0) return;
        foreach (var (placement, drawn) in _attachments) drawn.World = placement.World * CharacterWorld;
        foreach (var (placement, drawn) in _weaponAttachments) drawn.World = placement.World * CharacterWorld;
        _propLoader.PlaceAttachedModels();
    }

    private readonly Dictionary<uint, (uint ModelFileId, Gw2AttachedModel Drawn)> _keyframedAgentModels = [];
    private readonly HashSet<uint> _presentKeyframedAgents = [];

    private void PlaceKeyframedAgentModels(Gw2PropLoader propLoader)
    {
        _presentKeyframedAgents.Clear();
        foreach (var agent in _world.Agents.Keyframed)
        {
            if (agent.ModelFileId is not { } modelFileId) continue;
            _presentKeyframedAgents.Add(agent.AgentId);
            if (!_keyframedAgentModels.TryGetValue(agent.AgentId, out var entry) || entry.ModelFileId != modelFileId)
            {
                if (entry.Drawn is not null) propLoader.DetachModel(entry.Drawn);
                entry = (modelFileId, propLoader.AttachModel(modelFileId));
                _keyframedAgentModels[agent.AgentId] = entry;
            }
            entry.Drawn.World = Matrix4x4.CreateScale(agent.Scale) * Matrix4x4.CreateFromQuaternion(agent.Rotation) * Matrix4x4.CreateTranslation(agent.PositionInGameUnits);
        }
        foreach (var agentId in _keyframedAgentModels.Keys.Where(agentId => !_presentKeyframedAgents.Contains(agentId)).ToArray())
        {
            propLoader.DetachModel(_keyframedAgentModels[agentId].Drawn);
            _keyframedAgentModels.Remove(agentId);
        }
    }

    private readonly Dictionary<uint, (uint ModelFileId, Gw2AttachedModel Drawn)> _speciesModels = [];
    private readonly HashSet<uint> _presentSpeciesAgents = [];

    private void PlaceSpeciesModels(Gw2PropLoader propLoader)
    {
        _presentSpeciesAgents.Clear();
        foreach (var character in _sceneCharacters)
        {
            if (character.IsControlled || _world.CompositeIdentities.IdentityOf(character.AgentId) is not null || !_world.Agents.TryGet(character.AgentId, out var agent) || _names.SpeciesModelFileIdOf(agent) is not { } modelFileId) continue;
            _presentSpeciesAgents.Add(character.AgentId);
            if (!_speciesModels.TryGetValue(character.AgentId, out var entry) || entry.ModelFileId != modelFileId)
            {
                if (entry.Drawn is not null) propLoader.DetachModel(entry.Drawn);
                entry = (modelFileId, propLoader.AttachModel(modelFileId));
                _speciesModels[character.AgentId] = entry;
            }
            var yaw = character.Facing.X != 0f || character.Facing.Y != 0f ? MathF.Atan2(character.Facing.Y, character.Facing.X) : CharacterModelForwardYaw;
            entry.Drawn.World = Matrix4x4.CreateRotationZ(yaw - CharacterModelForwardYaw) * Matrix4x4.CreateTranslation(character.PositionInGameUnits);
        }
        foreach (var agentId in _speciesModels.Keys.Where(agentId => !_presentSpeciesAgents.Contains(agentId)).ToArray())
        {
            propLoader.DetachModel(_speciesModels[agentId].Drawn);
            _speciesModels.Remove(agentId);
        }
    }

    private const float CharacterModelForwardYaw = -MathF.PI / 2f;

    private Matrix4x4 CharacterWorld =>
        Matrix4x4.CreateFromQuaternion(_character?.ModelRotation ?? Quaternion.Identity)
        * Matrix4x4.CreateRotationZ(CharacterFacingYaw - CharacterModelForwardYaw)
        * Matrix4x4.CreateTranslation(DrawnCharacterPosition);

    private float CharacterFacingYaw => _ownFacing is { } facing && (facing.X != 0f || facing.Y != 0f) ? MathF.Atan2(facing.Y, facing.X) : CharacterModelForwardYaw;

    partial void UnloadGraphics()
    {
        _characterPass?.Dispose();
        _agentCapsules?.Dispose();
        _agentNameLabels?.Dispose();
        _nameplateHealthBars?.Dispose();
        _retainedHud?.Dispose();
        _debugViewCopy?.Dispose();
        UnloadMapGraphics();
        _device?.Dispose();
    }

    partial void UnloadMapGraphics()
    {
        ClearPlayerModels();
        _characterPass?.ClearMap();
        _skybox?.Dispose();
        _skybox = null;
        _daySky = null;
        _nightSky = null;
        foreach (var clouds in _cloudsByEnvironment.Values) clouds?.Layers.Dispose();
        _cloudsByEnvironment.Clear();
        _starField?.Dispose();
        _starField = null;
        _particleFields?.Dispose();
        _particleFields = null;
        _skyCards?.Dispose();
        _skyCards = null;
        _skyCardSources = [];
        foreach (var texture in _skyTextures) texture.Dispose();
        _skyTextures.Clear();
        _postProcessCombine?.Dispose();
        _postProcessCombine = null;
        _terrainLoader?.Dispose();
        _terrainLoader = null;
        _water?.Dispose();
        _water = null;
        DetachOwnWeapons(_propLoader);
        _propLoader?.Dispose();
        _propLoader = null;
        _attachments.Clear();
        _keyframedAgentModels.Clear();
        _speciesModels.Clear();
        _colorLookup = null;
    }

    partial void SkinCharacter() => _characterPass!.Skin(_character!.SkinPalette);

    partial void UploadCharacter(ref string uploadSummary)
    {
        var atlas = _character!.Atlas is { } image ? _characterPass!.UploadAtlas(image.Width, image.Height, image.Pixels) : null;
        var normalAtlas = _character.NormalAtlas is { } normals ? _characterPass!.UploadAtlas(normals.Width, normals.Height, normals.Pixels) : null;
        _characterPass!.ClearCharacterMeshes();
        foreach (var part in _character.Parts) _characterPass!.Add(part, _character.Skeleton!, atlas, normalAtlas);
        var (eyeRed, eyeGreen, eyeBlue) = _character.EyeColor.ToColumnConstants();
        _characterPass!.SetEyeColor(eyeRed, eyeGreen, eyeBlue);
        _characterPass.Skin(_character.SkinPalette);
        uploadSummary = $", {_characterPass.MeshCount} meshes";
    }

    partial void StreamWorldAroundEye()
    {
        _terrainLoader?.Update(_camera.Eye);
        _propLoader?.Update(_camera.Eye, Gw2OrbitCamera.FarPlane);
    }

    partial void DescribeStreaming(List<string> lines)
    {
        if (_terrainLoader is not null) lines.Add($"Terrain chunks  {_terrainLoader.ResidentChunkCount}{(_terrainLoader.IsSettled ? "" : " +")}");
        if (_propLoader is not null) lines.Add($"Props  {_propLoader.LoadedProps.Count()}, models {_propLoader.ResidentModelCount}, textures {_propLoader.ResidentTextureCount}{(_propLoader.IsSettled ? "" : " +")}");
    }

    partial void CheckStreamingSettled(ref bool settled) =>
        settled = (_propLoader?.IsSettled ?? true) && (_terrainLoader?.IsSettled ?? true);

    partial void DescribeFlightStreaming(ref string description) =>
        description = $" | chunks {_terrainLoader?.ResidentChunkCount ?? 0} | props {_propLoader?.LoadedProps.Count() ?? 0} | models {_propLoader?.ResidentModelCount ?? 0} | textures {_propLoader?.ResidentTextureCount ?? 0} | high-res {_propLoader?.HighResolutionTextureCount ?? 0}";

    partial void DescribeSlowFrameStreaming(ref string description)
    {
        var terrain = _terrainLoader;
        var props = _propLoader;
        description = string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $" | terrain {_terrainUpdateMilliseconds:F1} ({terrain?.LastUploadedChunks ?? 0} chunks, {terrain?.LastUploadedTextures ?? 0} textures) | water {_waterAdvanceMilliseconds:F1} | props {_propUpdateMilliseconds:F1} (activate {props?.LastActivateMilliseconds ?? 0:F1}, {props?.LastUploadedTextures ?? 0} textures, {props?.LastUploadedMeshes ?? 0} meshes, {props?.LastLoadedProps ?? 0} props, {props?.LastLoadedBlocks ?? 0} blocks, longest {props?.LastLongestStepKind} {props?.LastLongestStepMilliseconds ?? 0:F1}) | lod {_propLevelMilliseconds:F1}");
    }

    private static double ElapsedMillisecondsSince(ref long timestamp)
    {
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(timestamp, now).TotalMilliseconds;
        timestamp = now;
        return elapsed;
    }
}
