using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Gw2.Client.Model;
using Gw2AmatEffectKeys = Gw2.Dat.Content.Gw2AmatEffectKeys;
using Gw2AmatMaterial = Gw2.Dat.Content.Gw2AmatMaterial;
using Gw2BuiltInWaterMaterial = Gw2.Dat.Content.Gw2BuiltInWaterMaterial;
using Gw2EnvironmentAmbientOcclusion = Gw2.Dat.Content.Gw2EnvironmentAmbientOcclusion;
using Gw2EnvironmentFog = Gw2.Dat.Content.Gw2EnvironmentFog;
using Gw2EnvironmentShLighting = Gw2.Dat.Content.Gw2EnvironmentShLighting;
using Gw2ModelMaterialDraw = Gw2.Dat.Content.Gw2ModelMaterialDraw;
using Gw2ModelSkeleton = Gw2.Dat.Content.Gw2ModelSkeleton;
using Gw2SortBuckets = Gw2.Dat.Content.Gw2SortBuckets;
using Gw2SortType = Gw2.Dat.Content.Gw2SortType;
using Crystal;
using Crystal.Graphics;

namespace Gw2.Client.Graphics;

public sealed class GrCharacterPass : IDisposable
{
    private const int LightBufferEngineTexture = 2;
    private const string WaterReflectionSamplerName = "ssReflect";
    private const int ShadowDepthAlphaMaskSlot = 0;
    private const uint FadeColorToken = 0x30463360;
    private const int AlbedoSlot = 0;
    private static readonly Color DebugSwapClearColor = new(0, 0, 128, 0);
    private static readonly Vector4 WireColor = new(0f, 0f, 0f, 1f);
    private static readonly RasterizerState WireRasterizerState = RasterizerState.Wireframe with { DepthBias = -167 };
    private readonly GrDevice _device;
    private readonly ShaderProgram _character;
    private readonly ShaderProgram _skinnedCharacter;
    private readonly List<Vector4> _agentPaletteColumns = [];
    private bool _agentPalettesUploaded;
    private GraphicsBuffer? _agentPaletteBuffer;
    private int _agentPaletteCapacity;
    private float _passClockSeconds;
    private readonly List<GrSkinnedMesh> _meshes = new();
    private readonly List<GrSkinnedMesh> _agentSkinnedMeshes = new();
    private readonly List<GrEffectParticleCloud> _effectParticleClouds = new();
    private readonly List<Texture2D> _atlases = new();
    private readonly Texture2D _blankAtlas;
    private readonly Texture2D _flatNormalAtlas;
    private readonly GrEngineTextures _engineTextures;
    private readonly Func<uint, Gw2AmatMaterial?>? _archiveMaterialOf;
    private readonly ConcurrentDictionary<(uint MaterialFileId, uint DrawFlags, int SortBucketOverride), GrArchiveShaderMaterial?> _archiveMaterials = new();
    private readonly Dictionary<GrSkinnedMesh, GrArchiveShaderMaterial> _archiveMaterialByMesh = new();
    private readonly ConcurrentDictionary<(uint MaterialFileId, uint DrawFlags, int SortBucketOverride), GrArchiveShaderMaterial?> _normalSpecMaterials = new();
    private readonly Dictionary<Gw2BuiltInWaterMaterial, GrArchiveShaderMaterial> _builtInMaterials = new();
    private readonly Dictionary<GrSkinnedMesh, GrArchiveShaderMaterial> _normalSpecMaterialByMesh = new();
    private readonly ConcurrentDictionary<(uint MaterialFileId, uint DrawFlags, int SortBucketOverride), GrArchiveShaderMaterial?> _instancedMaterials = new();
    private readonly ConcurrentDictionary<(uint MaterialFileId, uint DrawFlags, int SortBucketOverride), GrArchiveShaderMaterial?> _reflectionMaterials = new();
    private readonly ConcurrentDictionary<(uint MaterialFileId, uint DrawFlags, int SortBucketOverride), GrArchiveShaderMaterial?> _instancedReflectionMaterials = new();
    private readonly ConcurrentDictionary<(uint MaterialFileId, uint DrawFlags, int SortBucketOverride), GrArchiveShaderMaterial?> _instancedNormalSpecMaterials = new();
    private readonly ConcurrentDictionary<(uint MaterialFileId, uint DrawFlags, int SortBucketOverride), GrArchiveShaderMaterial?> _instancedFadingMaterials = new();
    private readonly ConcurrentDictionary<(uint MaterialFileId, uint DrawFlags, int SortBucketOverride), GrArchiveShaderMaterial?> _shadowDepthMaterials = new();
    private readonly List<(GrStaticMeshInstances Instances, GrArchiveShaderMaterial? Material, GrArchiveShaderMaterial? FadingMaterial, GrArchiveShaderMaterial? NormalSpecMaterial)> _propInstances = new();
    private readonly GrLightBuffer _lightBuffer;
    private readonly GrScreenCopy _linearDepthCopy;
    private GrDebugView _debugView;
    private ShaderLibrary? _debugSurface;

    public GrReflections Reflections { get; set; } = GrReflections.TerrainAndSky;

    public GrFrameProfiler? FrameTimer { get; set; }

    public GrAgentCapsules? AgentCapsules { get; set; }

    public bool ParallelRecording { get; set; } = true;

    private Gw2EnvironmentFog? _environmentFog;
    private GrVolumetricFog? _volumetricFog;
    private GrAmbientOcclusion? _ambientOcclusion;
    private Gw2EnvironmentAmbientOcclusion _environmentAmbientOcclusion = Gw2EnvironmentAmbientOcclusion.WithoutEnvironment;
    private bool _volumetricFogMarched;
    private (Vector4 Red, Vector4 Green, Vector4 Blue) _eyeColor = (Vector4.UnitX, Vector4.UnitY, Vector4.UnitZ);
    private float _cameraNear;
    private float _cameraFar;
    private GrShadowMap _shadowMap;
    private readonly GrDepthCasterBatcher _depthCasterBatcher;
    private readonly Dictionary<IReadOnlyDictionary<uint, Vector4>, IReadOnlyDictionary<uint, Vector4>> _internedConstants = new(GrMaterialConstantsComparer.Instance);
    private Gw2EnvironmentShLighting? _environmentLighting;
    private IReadOnlyList<Gw2.Dat.Content.Gw2PointLightFrame> _pointLights = [];
    private IReadOnlyList<Gw2.Dat.Content.Gw2SpotLightFrame> _spotLights = [];
    private GrFarShadowMap? _farShadowMap;
    private (GrWater? Water, float FarClip) _water;
    private GrWaterReflection? _waterReflection;
    private readonly List<(GrTerrainChunk Chunk, GrArchiveShaderMaterial? Material, GrArchiveShaderMaterial? NormalSpecMaterial)> _terrainChunks = new();
    private readonly List<GrSceneProp> _props = new();
    private const int CameraCullSlot = 0;
    private const int OpaqueCullSlot = 1;
    private const int FirstCascadeCullSlot = 2;
    private GrGpuScene? _gpuScene;
    private GrHiZ? _hiZ;
    private Matrix4x4? _hiZViewProjection;
    private readonly List<int> _gpuVisibleCasterGroups = [];
    private readonly List<GrDepthCaster> _gpuDepthCasters = [];
    private readonly Dictionary<GrStaticMesh, GrDepthCaster> _gpuDepthCasterByMesh = new(ReferenceEqualityComparer.Instance);
    private readonly GrPassRecorder _normalSpecPass;
    private readonly GrPassRecorder _shadowPass;
    private readonly GrPassRecorder _lightingPass;
    private readonly GrPassRecorder _skyPass;
    private readonly GrPassRecorder _reflectionPass;
    private readonly GrPassRecorder _opaquePass;
    private readonly GrPassRecorder _laterPass;
    private readonly GrPassRecorder[] _passesInExecutionOrder;
    private readonly GraphicsCommandList[] _commandListsInExecutionOrder;
    private GrSceneView _sceneView;
    private GrTargetBinding _sceneTargets;
    private bool _drawsLightBuffer;
    private bool _rendersShadows;
    private GrShadowCascade[] _cascades = [];
    private GrWaterReflectionView? _waterReflectionView;
    private Action? _drawSky;

    public GrCharacterPass(GrDevice device, Func<uint, Gw2AmatMaterial?>? archiveMaterialOf = null, GrShadowSettings? shadowSettings = null)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _archiveMaterialOf = archiveMaterialOf;
        _character = device.LoadShader("GrCharacter").GetProgram("Character");
        _skinnedCharacter = device.LoadShader("GrCharacter").GetProgram("SkinnedCharacter");
        _blankAtlas = new Texture2D(device.GraphicsDevice, new TextureDescription { Width = 1, Height = 1, Name = "CharacterBlankAtlas" });
        _blankAtlas.SetData<Color>([Color.White]);
        _flatNormalAtlas = new Texture2D(device.GraphicsDevice, new TextureDescription { Width = 1, Height = 1, Name = "CharacterFlatNormalAtlas" });
        _flatNormalAtlas.SetData<Color>([new Color(128, 128, 255, 255)]);
        _engineTextures = new GrEngineTextures(device.GraphicsDevice);
        _lightBuffer = new GrLightBuffer(device, _engineTextures);
        _linearDepthCopy = new GrScreenCopy(device);
        _shadowMap = new GrShadowMap(device, shadowSettings ?? GrShadowSettings.Defaults);
        _normalSpecPass = CreatePass(GrFrameSection.NormalSpec, recordsOnList: true);
        _shadowPass = CreatePass(GrFrameSection.Shadows, recordsOnList: true);
        _lightingPass = CreatePass(GrFrameSection.Lighting, recordsOnList: true);
        _skyPass = CreatePass(GrFrameSection.Sky, recordsOnList: true);
        _reflectionPass = CreatePass(GrFrameSection.Reflection, recordsOnList: true);
        _opaquePass = CreatePass(GrFrameSection.Opaque, recordsOnList: true);
        _laterPass = CreatePass(GrFrameSection.Water, recordsOnList: false);
        _passesInExecutionOrder = [_normalSpecPass, _shadowPass, _lightingPass, _skyPass, _reflectionPass, _opaquePass];
        _commandListsInExecutionOrder = Array.ConvertAll(_passesInExecutionOrder, pass => pass.CommandList!);
        _depthCasterBatcher = new GrDepthCasterBatcher(_shadowPass.InstanceStream);
    }

    private GrPassRecorder CreatePass(GrFrameSection section, bool recordsOnList)
    {
        var pass = new GrPassRecorder(section, recordsOnList ? new GraphicsCommandList(_device.GraphicsDevice, section.ToString()) : null);
        pass.TextureOfSampler = sampler => TextureOfSampler(pass, sampler);
        return pass;
    }

    public bool AnisotropicFiltering { get; set; } = true;

    public bool DynamicShadows { get; set; } = true;

    public GrShadowSettings ShadowSettings
    {
        get => _shadowMap.Settings;
        set
        {
            if (value == _shadowMap.Settings) return;
            _shadowMap.Dispose();
            _shadowMap = new GrShadowMap(_device, value);
        }
    }

    public bool VolumetricFog
    {
        get => _volumetricFog is not null;
        set
        {
            if (value == VolumetricFog) return;
            if (value) _volumetricFog = new GrVolumetricFog(_device, _engineTextures);
            else
            {
                _volumetricFog!.Dispose();
                _volumetricFog = null;
            }
        }
    }

    public bool AmbientOcclusion
    {
        get => _ambientOcclusion is not null;
        set
        {
            if (value == AmbientOcclusion) return;
            if (value) _ambientOcclusion = new GrAmbientOcclusion(_device);
            else
            {
                _ambientOcclusion!.Dispose();
                _ambientOcclusion = null;
            }
        }
    }

    public bool GpuDriven
    {
        get => _gpuScene is not null;
        set
        {
            if (value == GpuDriven) return;
            if (_props.Count != 0) throw new InvalidOperationException("GPU-driven rendering is chosen before props are added.");
            _gpuScene?.Dispose();
            _gpuScene = value ? new GrGpuScene(_device) : null;
            if (_gpuScene is null) HiZ = false;
        }
    }

    public bool HiZ
    {
        get => _hiZ is not null;
        set
        {
            if (value == HiZ) return;
            _hiZ?.Dispose();
            _hiZ = value && _gpuScene is not null ? new GrHiZ(_device) : null;
        }
    }

    public Gw2.Dat.Content.Gw2ShaderQuality ShaderQuality { get; set; } = Gw2.Dat.Content.Gw2ShaderQuality.High;

    public int MeshCount => _meshes.Count;

    public void SetPointLights(IReadOnlyList<Gw2.Dat.Content.Gw2PointLightFrame> pointLights) => _pointLights = pointLights;

    public void SetSpotLights(IReadOnlyList<Gw2.Dat.Content.Gw2SpotLightFrame> spotLights) => _spotLights = spotLights;

    public void SetEnvironmentLighting(Gw2EnvironmentShLighting? environmentLighting) => _environmentLighting = environmentLighting;

    public void SetEyeColor(Vector4 red, Vector4 green, Vector4 blue) => _eyeColor = (red, green, blue);

    public void SetFarShadow(Gw2.Dat.Content.Gw2MapShadow shadow, float depthRange, bool combinesShadowSamples)
    {
        _farShadowMap?.Dispose();
        _farShadowMap = new GrFarShadowMap(_device, shadow, GrShadowSettings.Defaults.BakedShadowResolution, depthRange, combinesShadowSamples);
        _engineTextures.FarShadow = _farShadowMap.ShadowTexture;
    }

    public void SetEnvironmentFog(Gw2EnvironmentFog? environmentFog) => _environmentFog = environmentFog;

    public void SetEnvironmentAmbientOcclusion(Gw2EnvironmentAmbientOcclusion environmentAmbientOcclusion) => _environmentAmbientOcclusion = environmentAmbientOcclusion;

    public Action<Matrix4x4, Matrix4x4, Vector3>? DrawReflectionSky { get; set; }

    public void SetWater(GrWater? water, float farClip) => _water = (water, farClip);

    public Texture2D UploadAtlas(int width, int height, byte[] rgbaPixels)
    {
        var atlas = new Texture2D(_device.GraphicsDevice, new TextureDescription { Width = width, Height = height, Name = "CharacterAtlas" });
        atlas.SetData<byte>(rgbaPixels);
        _atlases.Add(atlas);
        return atlas;
    }

    public void Add(Gw2Model model, Gw2ModelSkeleton skeleton, Texture2D? atlas, Texture2D? normalAtlas)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        var skeletonBones = new Dictionary<long, int>();
        int SkeletonBoneOf(long token)
        {
            if (!skeletonBones.TryGetValue(token, out var bone))
                skeletonBones[token] = bone = skeleton.FindBoneIndex(unchecked((ulong)token));
            return bone;
        }

        foreach (var mesh in model.Meshes)
        {
            if (!mesh.IsRenderable || mesh.LodLevel != 0) continue;
            var uploaded = GrSkinnedMesh.Upload(_device.GraphicsDevice, mesh, SkeletonBoneOf, atlas ?? _blankAtlas, normalAtlas ?? _flatNormalAtlas);
            _meshes.Add(uploaded);
            if (model.Materials.ElementAtOrDefault((int)mesh.MaterialIndex) is not { MaterialFileId: { } materialFileId } modelMaterial) continue;
            var drawFlags = Gw2ModelMaterialDraw.DrawFlagsOf(modelMaterial.MaterialFlags);
            var sortBucketOverride = Gw2ModelMaterialDraw.SortBucketOverrideOf(modelMaterial.SortOrder);
            if (ArchiveMaterial(_archiveMaterials, materialFileId, Gw2AmatEffectKeys.Default, drawFlags: drawFlags, sortBucketOverride: sortBucketOverride) is { } archiveMaterial)
                _archiveMaterialByMesh[uploaded] = archiveMaterial;
            if (ArchiveMaterial(_normalSpecMaterials, materialFileId, Gw2AmatEffectKeys.NormalSpecDepth, drawFlags: drawFlags, sortBucketOverride: sortBucketOverride) is { } normalSpecMaterial)
                _normalSpecMaterialByMesh[uploaded] = normalSpecMaterial;
        }
    }

    private GrArchiveShaderMaterial? ArchiveMaterial(ConcurrentDictionary<(uint MaterialFileId, uint DrawFlags, int SortBucketOverride), GrArchiveShaderMaterial?> cache, uint materialFileId, ulong effectKey, uint vertexShaderVariant = GrArchiveShaderMaterial.StaticVertexShaderVariant, uint drawFlags = 0, int sortBucketOverride = 0)
    {
        if (_archiveMaterialOf is null) return null;
        var key = (materialFileId, drawFlags, sortBucketOverride);
        if (cache.TryGetValue(key, out var material)) return material;
        lock (cache)
        {
            if (!cache.TryGetValue(key, out material))
                cache[key] = material = _archiveMaterialOf(materialFileId) is { } amat ? GrArchiveShaderMaterial.Create(_device, amat, effectKey, vertexShaderVariant, ShaderQuality, drawFlags, sortBucketOverride) : null;
            return material;
        }
    }

    private GrArchiveShaderMaterial? InstancedMaterial(ConcurrentDictionary<(uint MaterialFileId, uint DrawFlags, int SortBucketOverride), GrArchiveShaderMaterial?> cache, GrStaticMesh mesh, ulong effectKey) =>
        ArchiveMaterial(cache, mesh.MaterialFileId, effectKey, GrArchiveShaderMaterial.InstancedVertexShaderVariant, mesh.DrawFlags, mesh.SortBucketOverride);

    private GrArchiveShaderMaterial? WaterMaterial(GrWaterMaterial material)
    {
        if (material.BuiltInMaterial is not { } builtIn) return ArchiveMaterial(_archiveMaterials, material.MaterialFileId, Gw2AmatEffectKeys.Default);
        if (!_builtInMaterials.TryGetValue(builtIn, out var created))
            _builtInMaterials[builtIn] = created = GrArchiveShaderMaterial.FromBuiltIn(_device, builtIn);
        return created;
    }
    private GrArchiveShaderMaterial ReflectionMaterial(uint materialFileId, GrArchiveShaderMaterial mainMaterial) =>
        ArchiveMaterial(_reflectionMaterials, materialFileId, Gw2AmatEffectKeys.Reflection, drawFlags: mainMaterial.DrawFlags, sortBucketOverride: mainMaterial.SortBucketOverride) ?? mainMaterial;

    private GrArchiveShaderMaterial InstancedReflectionMaterial(uint materialFileId, GrArchiveShaderMaterial mainMaterial) =>
        ArchiveMaterial(_instancedReflectionMaterials, materialFileId, Gw2AmatEffectKeys.Reflection, GrArchiveShaderMaterial.InstancedVertexShaderVariant, mainMaterial.DrawFlags, mainMaterial.SortBucketOverride) ?? mainMaterial;

    public void AddTerrainChunk(GrTerrainChunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        _terrainChunks.Add((chunk, ArchiveMaterial(_archiveMaterials, chunk.MaterialFileId, Gw2AmatEffectKeys.Default), ArchiveMaterial(_normalSpecMaterials, chunk.MaterialFileId, Gw2AmatEffectKeys.NormalSpecDepth)));
    }

    public void RemoveTerrainChunk(GrTerrainChunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        _terrainChunks.RemoveAll(terrain => ReferenceEquals(terrain.Chunk, chunk));
        chunk.Dispose();
    }

    public int TerrainChunkCount => _terrainChunks.Count;

    public void AddProp(GrStaticMesh mesh, Matrix4x4 world, IReadOnlyDictionary<uint, Vector4> constants, Vector4 modelColor, GrPropLodState lodState, int lodLevel, bool constantsChangeEachFrame = false, bool placedEachFrame = false)
    {
        if (!constantsChangeEachFrame) constants = Interned(constants);
        var drawsThroughGpuScene = _gpuScene is not null && !placedEachFrame;
        ArgumentNullException.ThrowIfNull(mesh);
        var material = ArchiveMaterial(_archiveMaterials, mesh.MaterialFileId, Gw2AmatEffectKeys.Default, drawFlags: mesh.DrawFlags, sortBucketOverride: mesh.SortBucketOverride);
        var normalSpecMaterial = ArchiveMaterial(_normalSpecMaterials, mesh.MaterialFileId, Gw2AmatEffectKeys.NormalSpecDepth, drawFlags: mesh.DrawFlags, sortBucketOverride: mesh.SortBucketOverride);
        var prop = new GrSceneProp(mesh, world, constants, modelColor, material, normalSpecMaterial, lodState, lodLevel)
        {
            GpuNormalSpecMaterial = drawsThroughGpuScene && normalSpecMaterial is { RenderState.IsBlended: false } ? InstancedMaterial(_instancedNormalSpecMaterials, mesh, Gw2AmatEffectKeys.NormalSpecDepth) : null,
            GpuMainMaterial = drawsThroughGpuScene && DrawsInAnyOrder(material) ? InstancedMaterial(_instancedMaterials, mesh, Gw2AmatEffectKeys.Default) : null,
            CastsShadowOnGpu = drawsThroughGpuScene && normalSpecMaterial is { RenderState.IsBlended: false },
        };
        _props.Add(prop);
        if (drawsThroughGpuScene) _gpuScene!.Add(prop);
    }

    private IReadOnlyDictionary<uint, Vector4> Interned(IReadOnlyDictionary<uint, Vector4> constants)
    {
        if (_internedConstants.TryGetValue(constants, out var interned)) return interned;
        _internedConstants.Add(constants, constants);
        return constants;
    }

    private static bool DrawsInAnyOrder(GrArchiveShaderMaterial? material) =>
        material is { SortBucket: Gw2SortBuckets.Default, RenderState.IsBlended: false, LaterPasses.Count: 0 }
        && material.DrawState is { DepthTest: Gw2.Dat.Content.Gw2DepthTest.LessEqual, DepthWrite: true };

    public void SetEffectParticleClouds(IEnumerable<GrEffectParticleCloud> clouds)
    {
        _effectParticleClouds.Clear();
        _effectParticleClouds.AddRange(clouds);
    }

    public Task PrepareInstancedMaterialsAsync(uint materialFileId, uint drawFlags, int sortBucketOverride) => Task.Run(() =>
    {
        ArchiveMaterial(_instancedMaterials, materialFileId, Gw2AmatEffectKeys.Default, GrArchiveShaderMaterial.InstancedVertexShaderVariant, drawFlags, sortBucketOverride);
        ArchiveMaterial(_instancedFadingMaterials, materialFileId, Gw2AmatEffectKeys.Fading, GrArchiveShaderMaterial.InstancedVertexShaderVariant, drawFlags, sortBucketOverride);
        ArchiveMaterial(_instancedNormalSpecMaterials, materialFileId, Gw2AmatEffectKeys.NormalSpecDepth, GrArchiveShaderMaterial.InstancedVertexShaderVariant, drawFlags, sortBucketOverride);
    });

    public void AddPropInstances(GrStaticMeshInstances instances)
    {
        ArgumentNullException.ThrowIfNull(instances);
        _propInstances.Add((instances,
            ArchiveMaterial(_instancedMaterials, instances.MaterialFileId, Gw2AmatEffectKeys.Default, GrArchiveShaderMaterial.InstancedVertexShaderVariant, instances.DrawFlags, instances.SortBucketOverride),
            ArchiveMaterial(_instancedFadingMaterials, instances.MaterialFileId, Gw2AmatEffectKeys.Fading, GrArchiveShaderMaterial.InstancedVertexShaderVariant, instances.DrawFlags, instances.SortBucketOverride),
            ArchiveMaterial(_instancedNormalSpecMaterials, instances.MaterialFileId, Gw2AmatEffectKeys.NormalSpecDepth, GrArchiveShaderMaterial.InstancedVertexShaderVariant, instances.DrawFlags, instances.SortBucketOverride)));
    }

    public void RemoveProps(IReadOnlySet<GrPropLodState> lodStates)
    {
        ArgumentNullException.ThrowIfNull(lodStates);
        if (lodStates.Count == 0) return;
        _props.RemoveAll(prop => lodStates.Contains(prop.LodState));
        _gpuScene?.Remove(lodStates);
    }

    public void RemovePropInstances(GrStaticMeshInstances instances)
    {
        _propInstances.RemoveAll(entry => ReferenceEquals(entry.Instances, instances));
        _fadeColorConstantsByInstances.Remove(instances);
        _depthCasterByInstances.Remove(instances);
    }

    public void ClearPropInstances()
    {
        _propInstances.Clear();
        _fadeColorConstantsByInstances.Clear();
        _depthCasterByInstances.Clear();
    }

    public int PropInstanceGroupCount => _propInstances.Count;

    public void ClearMap()
    {
        foreach (var terrain in _terrainChunks) terrain.Chunk.Dispose();
        _terrainChunks.Clear();
        _gpuScene?.Remove(_props.Select(prop => prop.LodState).ToHashSet());
        _props.Clear();
        ClearPropInstances();
        _farShadowMap?.Dispose();
        _farShadowMap = null;
        _engineTextures.FarShadow = null;
        _water = (null, 0f);
        _pointLights = [];
        _spotLights = [];
    }

    public void AdvanceScatterFade(Vector3 eye, float elapsedSeconds)
    {
        foreach (var (instances, _, _, _) in _propInstances) instances.Fade.Advance(eye, elapsedSeconds);
    }

    private IEnumerable<(GrStaticMeshInstances Instances, GrArchiveShaderMaterial? Material, GrArchiveShaderMaterial? NormalSpecMaterial)> VisiblePropInstances(GrCullFrustum frustum) =>
        FadedInPropInstances.Where(entry => frustum.Intersects(entry.Instances.Bounds));

    private IEnumerable<(GrStaticMeshInstances Instances, GrArchiveShaderMaterial? Material, GrArchiveShaderMaterial? NormalSpecMaterial)> FadedInPropInstances =>
        _propInstances
            .Where(entry => entry.Instances.Fade.IsVisible)
            .Select(entry => (entry.Instances, entry.Instances.Fade.IsFading ? entry.FadingMaterial ?? entry.Material : entry.Material, entry.NormalSpecMaterial));

    private readonly Dictionary<GrStaticMeshInstances, Dictionary<uint, Vector4>> _fadeColorConstantsByInstances = new(ReferenceEqualityComparer.Instance);

    private void UpdateFadeColors()
    {
        foreach (var (instances, _, _, _) in _propInstances)
        {
            if (!instances.Fade.IsVisible) continue;
            if (!_fadeColorConstantsByInstances.TryGetValue(instances, out var constants))
                _fadeColorConstantsByInstances[instances] = constants = new Dictionary<uint, Vector4>(instances.MaterialConstants);
            constants[FadeColorToken] = new Vector4(1f, 1f, 1f, instances.Fade.Value);
        }
    }

    private IReadOnlyDictionary<uint, Vector4> WithFadeColor(GrStaticMeshInstances instances) => _fadeColorConstantsByInstances[instances];

    private IEnumerable<GrSceneProp> PropsAtCurrentLevel =>
        _props.Where(prop => prop.LodState.Level == prop.LodLevel);

    private IEnumerable<GrSceneProp> PropsInView(GrCullFrustum frustum) =>
        PropsAtCurrentLevel.Where(prop => frustum.Intersects(prop.WorldBounds));

    private static BoundingBox PointBounds(Vector3 point) =>
        new(new Vector3(point.X, point.Y, point.Z), new Vector3(point.X, point.Y, point.Z));

    private static BoundingBox CharacterBounds(GrSkinnedMesh mesh, Matrix4x4 world) =>
        mesh.HasBounds ? GrWorldBounds.Of(mesh.LocalMinimum, mesh.LocalMaximum, world) : PointBounds(world.Translation);

    public int PropMeshCount => _props.Count;

    private readonly List<GrDepthCaster> _depthCasters = [];
    private readonly Dictionary<GrStaticMeshInstances, GrDepthCaster> _depthCasterByInstances = new(ReferenceEqualityComparer.Instance);

    private List<GrDepthCaster> DepthCasters(Matrix4x4 characterWorld)
    {
        _depthCasters.Clear();
        foreach (var mesh in _normalSpecMaterialByMesh.Keys) _depthCasters.Add(new GrDepthCaster(mesh, characterWorld));
        foreach (var (chunk, _, normalSpecMaterial) in _terrainChunks)
            if (normalSpecMaterial is not null) _depthCasters.Add(new GrDepthCaster(chunk, Matrix4x4.Identity, WorldBounds: chunk.Bounds, Rasterizer: RasterizerState.CullBack));
        foreach (var prop in _props)
            if (!prop.CastsShadowOnGpu && prop.LodState.Level == prop.LodLevel && prop.NormalSpecMaterial is { RenderState.IsBlended: false })
                _depthCasters.Add((prop.DepthCaster ??= AlphaMaskedCaster(prop.Mesh, prop.World, prop.Mesh.MaterialFileId, prop.WorldBounds)).WithCurrentAlphaMask());
        foreach (var (instances, _, _, normalSpecMaterial) in _propInstances)
            if (instances.Fade.IsVisible && normalSpecMaterial is { RenderState.IsBlended: false })
            {
                if (!_depthCasterByInstances.TryGetValue(instances, out var caster))
                    _depthCasterByInstances[instances] = caster = AlphaMaskedCaster(instances, Matrix4x4.Identity, instances.MaterialFileId, instances.Bounds);
                _depthCasters.Add(caster.WithCurrentAlphaMask());
            }
        return _depthCasters;
    }

    private GrDepthCaster AlphaMaskedCaster(IGrMaterialMesh mesh, Matrix4x4 world, uint materialFileId, BoundingBox worldBounds)
    {
        if (ArchiveMaterial(_shadowDepthMaterials, materialFileId, Gw2AmatEffectKeys.ShadowDepth)?.Samplers.FirstOrDefault(sampler => sampler.Slot == ShadowDepthAlphaMaskSlot && !sampler.IsEngineTexture) is not { } alphaMask)
            return new GrDepthCaster(mesh, world, WorldBounds: worldBounds);
        return mesh.MaterialTexture(alphaMask.ResourceIndex) is { } texture
            ? new GrDepthCaster(mesh, world, texture, _device.Sampler(GrSamplerStates.From(alphaMask.State)), worldBounds, alphaMask.ResourceIndex)
            : new GrDepthCaster(mesh, world, WorldBounds: worldBounds);
    }

    private IEnumerable<(GrTerrainChunk Chunk, GrArchiveShaderMaterial? Material, GrArchiveShaderMaterial? NormalSpecMaterial)> TerrainChunksInView(GrCullFrustum frustum) =>
        _terrainChunks.Where(terrain => frustum.Intersects(terrain.Chunk.Bounds));

    public Texture2D? SceneDepth => _lightBuffer.Depth;

    public Texture2D? DepthCopy => _engineTextures.DepthCopy;

    public Texture2D? DebugViewTexture(GrDebugView view) => view switch
    {
        GrDebugView.ShadowCascades => _engineTextures.NearShadow,
        GrDebugView.Normals => _lightBuffer.NormalSpec,
        GrDebugView.AmbientOcclusion => _ambientOcclusion?.Occlusion,
        _ => null,
    };

    public GrSkinnedMesh AddAgentSkinnedMesh(Gw2Mesh mesh, Gw2ModelSkeleton skeleton, Texture2D? atlas, Texture2D? normalAtlas)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        var uploaded = GrSkinnedMesh.Upload(_device.GraphicsDevice, mesh, token => skeleton.FindBoneIndex(unchecked((ulong)token)), atlas ?? _blankAtlas, normalAtlas ?? _flatNormalAtlas);
        _agentSkinnedMeshes.Add(uploaded);
        return uploaded;
    }

    public int AppendAgentPalette(Matrix4x4[] palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        if (_agentPalettesUploaded)
        {
            _agentPaletteColumns.Clear();
            _agentPalettesUploaded = false;
        }
        var offset = _agentPaletteColumns.Count / PaletteColumnsPerBone;
        foreach (var bone in palette)
        {
            _agentPaletteColumns.Add(new Vector4(bone.M11, bone.M21, bone.M31, bone.M41));
            _agentPaletteColumns.Add(new Vector4(bone.M12, bone.M22, bone.M32, bone.M42));
            _agentPaletteColumns.Add(new Vector4(bone.M13, bone.M23, bone.M33, bone.M43));
        }
        return offset;
    }

    private const int PaletteColumnsPerBone = 3;

    private void UploadAgentPalettes()
    {
        if (_agentPalettesUploaded || _agentPaletteColumns.Count == 0) return;
        if (_agentPaletteBuffer is null || _agentPaletteCapacity < _agentPaletteColumns.Count)
        {
            _agentPaletteBuffer?.Dispose();
            _agentPaletteCapacity = Math.Max(_agentPaletteColumns.Count * 2, 4096);
            _agentPaletteBuffer = new GraphicsBuffer(_device.GraphicsDevice, new BufferDescription
            {
                Size = (long)_agentPaletteCapacity * 16,
                Stride = 16,
                Usage = BufferUsage.Structured,
                Name = "AgentSkinningPalettes",
            });
        }
        _agentPaletteBuffer.SetData<Vector4>(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_agentPaletteColumns));
        _agentPalettesUploaded = true;
    }

    public void ClearCharacterMeshes()
    {
        foreach (var mesh in _meshes)
        {
            _archiveMaterialByMesh.Remove(mesh);
            _normalSpecMaterialByMesh.Remove(mesh);
            mesh.Dispose();
        }
        _meshes.Clear();
    }

    public void RemoveAgentSkinnedMesh(GrSkinnedMesh mesh)
    {
        if (_agentSkinnedMeshes.Remove(mesh)) mesh.Dispose();
    }

    public void Skin(Matrix4x4[] palette)
    {
        foreach (var mesh in _meshes) mesh.Skin(palette);
    }

    public void Draw(Matrix4x4 world, Matrix4x4 view, Matrix4x4 projection, Vector3 eye, float cameraNear, float cameraFar, GrTargetBinding sceneTargets, Action drawSky, Func<Texture2D>? copyMainColor = null)
    {
        ArgumentNullException.ThrowIfNull(drawSky);
        var stepStarted = Stopwatch.GetTimestamp();
        Prepare(world, view, projection, eye, cameraNear, cameraFar, sceneTargets);
        GrSceneRecordingTimes.PrepareMilliseconds += ElapsedStepMilliseconds(ref stepStarted);
        _drawSky = drawSky;
        var normalSpec = Start(_normalSpecPass, RecordNormalSpec);
        var shadows = Start(_shadowPass, RecordShadows);
        var opaque = Start(_opaquePass, RecordOpaque);
        RecordOn(_skyPass, RecordSky);
        var reflection = Start(_reflectionPass, RecordReflection);
        RecordOn(_lightingPass, RecordLighting);
        GrSceneRecordingTimes.MainThreadPassesMilliseconds += ElapsedStepMilliseconds(ref stepStarted);
        Task.WaitAll(normalSpec, shadows, opaque, reflection);
        GrSceneRecordingTimes.WaitMilliseconds += ElapsedStepMilliseconds(ref stepStarted);
        _drawSky = null;
        _device.GraphicsDevice.Execute(_commandListsInExecutionOrder);
        GrSceneRecordingTimes.SubmitMilliseconds += ElapsedStepMilliseconds(ref stepStarted);
        DrawAfterOpaque(copyMainColor);
    }

    private static double ElapsedStepMilliseconds(ref long started)
    {
        var now = Stopwatch.GetTimestamp();
        var elapsed = Stopwatch.GetElapsedTime(started, now).TotalMilliseconds;
        started = now;
        return elapsed;
    }

    private Task Start(GrPassRecorder pass, Action<GrPassRecorder> record)
    {
        if (ParallelRecording) return Task.Run(() => RecordOn(pass, record));
        RecordOn(pass, record);
        return Task.CompletedTask;
    }

    private void RecordOn(GrPassRecorder pass, Action<GrPassRecorder> record)
    {
        var start = Stopwatch.GetTimestamp();
        using (_device.Record(pass.CommandList!))
        {
            FrameTimer?.BeginRecorded(pass.Section);
            record(pass);
            FrameTimer?.EndRecorded(pass.Section, Stopwatch.GetElapsedTime(start).TotalSeconds);
        }
    }

    private void Prepare(Matrix4x4 world, Matrix4x4 view, Matrix4x4 projection, Vector3 eye, float cameraNear, float cameraFar, GrTargetBinding sceneTargets)
    {
        var viewProjection = view * projection;
        _sceneView = new GrSceneView(world, view, projection, viewProjection, eye);
        _sceneTargets = sceneTargets;
        UploadAgentPalettes();
        _passClockSeconds = GrEngineConstants.ClockSeconds(DateTime.UtcNow);
        _cameraNear = cameraNear;
        _cameraFar = cameraFar;
        var stepStarted = Stopwatch.GetTimestamp();
        _gpuScene?.BeginFrame(FirstCascadeCullSlot, FirstCascadeCullSlot + _shadowMap.Settings.CascadeCount);
        GrSceneRecordingTimes.PrepareGpuSceneMilliseconds += ElapsedStepMilliseconds(ref stepStarted);
        _farShadowMap?.Update(viewProjection, eye);
        GrSceneRecordingTimes.PrepareFarShadowMilliseconds += ElapsedStepMilliseconds(ref stepStarted);
        UpdateFadeColors();
        GrSceneRecordingTimes.PrepareFadeColorsMilliseconds += ElapsedStepMilliseconds(ref stepStarted);
        _engineTextures.ScreenCopy = null;
        _hiZViewProjection = null;
        _volumetricFogMarched = false;
        _rendersShadows = false;
        _cascades = [];
        _drawsLightBuffer = _normalSpecMaterialByMesh.Count != 0 || _terrainChunks.Count != 0 || _props.Count != 0 || _propInstances.Count != 0;
        if (_drawsLightBuffer)
        {
            _lightBuffer.EnsureTargets();
            var depth = _lightBuffer.Depth!;
            if (_hiZ is not null)
            {
                _hiZ.EnsurePyramid(depth.Width, depth.Height);
                _hiZViewProjection = viewProjection;
            }
            if (DynamicShadows && _environmentLighting is { } lighting)
            {
                var towardSun = new Vector3(lighting.ShSunData.X, lighting.ShSunData.Y, lighting.ShSunData.Z);
                _cascades = GrShadowCascades.Fit(_shadowMap.Settings, view, projection, cameraNear, cameraFar, towardSun);
                _rendersShadows = true;
            }
            _engineTextures.NearShadow = _cascades.Length > 0 ? _shadowMap.Texture : null;
            _engineTextures.DepthCopy = _linearDepthCopy.LinearDepthTarget(depth.Width, depth.Height);
            _volumetricFogMarched = _volumetricFog is not null && _debugView == GrDebugView.None && _environmentFog is not null;
        }
        GrSceneRecordingTimes.PrepareLightBufferMilliseconds += ElapsedStepMilliseconds(ref stepStarted);
        _waterReflectionView = PrepareWaterReflection(view, projection);
        GrSceneRecordingTimes.PrepareWaterReflectionMilliseconds += ElapsedStepMilliseconds(ref stepStarted);
    }

    private void RecordNormalSpec(GrPassRecorder pass)
    {
        if (!_drawsLightBuffer) return;
        var (world, view, _, viewProjection, eye) = _sceneView;
        pass.Begin(GrCullFrustum.Camera(viewProjection), drawsMainPass: false);
        _lightBuffer.BeginNormalSpec();
        foreach (var (mesh, material) in _normalSpecMaterialByMesh.OrderBy(entry => entry.Value.RenderState.IsBlended))
            DrawWithArchiveShaders(pass, mesh, material, world, view, viewProjection, eye);
        foreach (var (chunk, _, normalSpecMaterial) in TerrainChunksInView(pass.CameraFrustum))
            if (normalSpecMaterial is not null)
                DrawWithArchiveShaders(pass, chunk, normalSpecMaterial, Matrix4x4.Identity, view, viewProjection, eye);
        foreach (var worlds in _normalSpecWorldsByGroup.Values) worlds.Clear();
        foreach (var prop in _props)
            if (prop.GpuNormalSpecMaterial is null && prop.NormalSpecMaterial is not null && prop.LodState.Level == prop.LodLevel && pass.CameraFrustum.Intersects(prop.WorldBounds))
            {
                var key = (prop.Mesh, prop.NormalSpecMaterial, prop.Constants, prop.ModelColor);
                if (!_normalSpecWorldsByGroup.TryGetValue(key, out var worlds)) _normalSpecWorldsByGroup[key] = worlds = [];
                worlds.Add(prop.World);
            }
        DrawGpuNormalSpecGroups(pass, view, viewProjection, eye);
        foreach (var blended in (ReadOnlySpan<bool>)[false, true])
            foreach (var (key, worlds) in _normalSpecWorldsByGroup)
                if (worlds.Count > 0 && key.Material.RenderState.IsBlended == blended)
                    DrawNormalSpecGroup(pass, key.Mesh, key.Material, key.Constants, key.ModelColor, worlds, view, viewProjection, eye);
        foreach (var (instances, _, normalSpecMaterial) in VisiblePropInstances(pass.CameraFrustum).Where(entry => entry.NormalSpecMaterial is not null).OrderBy(entry => entry.NormalSpecMaterial!.RenderState.IsBlended))
            DrawWithArchiveShaders(pass, instances, normalSpecMaterial!, Matrix4x4.Identity, view, viewProjection, eye);
        if (_hiZ is not null) _hiZ.Build(_lightBuffer.Depth!);
    }

    private void RecordShadows(GrPassRecorder pass)
    {
        if (!_rendersShadows) return;
        pass.Begin(GrCullFrustum.Camera(_sceneView.ViewProjection), drawsMainPass: false);
        _shadowMap.Render(_cascades, _lightBuffer.SceneDepth, pass.Constants, DepthCasters(_sceneView.World), _depthCasterBatcher, _gpuScene is null ? null : GpuDepthCasters);
    }

    private void RecordLighting(GrPassRecorder pass)
    {
        if (!_drawsLightBuffer) return;
        var (_, view, projection, _, eye) = _sceneView;
        var depth = _lightBuffer.Depth!;
        var linearDepth = _linearDepthCopy.LinearDepth(depth, _cameraNear, _cameraFar);
        var occlusion = _ambientOcclusion is not null && _environmentAmbientOcclusion.IsActive && _lightBuffer.NormalSpec is { } normalSpec
            ? _ambientOcclusion.Render(linearDepth, normalSpec, view, _cameraFar, _environmentAmbientOcclusion)
            : null;
        var shadowMap = _cascades.Length > 0 ? _shadowMap : null;
        _lightBuffer.Accumulate(view, projection, eye, _cameraNear, _cameraFar, _environmentLighting, shadowMap, _cascades, _farShadowMap?.Constants, _pointLights, _spotLights, _shadowMap.Settings.ContactShadows, occlusion, GrAmbientOcclusion.PostLightConstantsOf(_environmentAmbientOcclusion));
        if (_volumetricFogMarched)
            _volumetricFog!.March(depth, view, projection, eye, _environmentFog!, _environmentLighting, shadowMap, _cascades, _farShadowMap?.Constants);
    }

    private void RecordSky(GrPassRecorder pass)
    {
        _device.SetRenderTargets(_sceneTargets);
        _drawSky!();
    }

    private void RecordReflection(GrPassRecorder pass)
    {
        if (_waterReflectionView is not { } reflection) return;
        var graphicsDevice = _device.GraphicsDevice;
        pass.Begin(GrCullFrustum.Camera(reflection.ViewProjection), drawsMainPass: false);
        pass.DrawsReflection = true;
        _waterReflection!.Bind();
        graphicsDevice.Clear(_environmentFog?.FogColorFar ?? default);
        graphicsDevice.ClearDepthStencil();
        DrawReflectionSky?.Invoke(reflection.View, _sceneView.Projection, reflection.Eye);
        var rasterizerState = pass.RasterizerState;
        pass.RasterizerState = RasterizerState.CullFront;
        foreach (var (chunk, material, _) in TerrainChunksInView(pass.CameraFrustum))
            if (material is not null)
                DrawMaterial(pass, chunk, ReflectionMaterial(chunk.MaterialFileId, material), Matrix4x4.Identity, reflection.View, reflection.ViewProjection, reflection.Eye);
        pass.RasterizerState = rasterizerState;
        if (Reflections == GrReflections.All)
        {
            pass.CullsMirrored = true;
            DrawReflectionProps(pass, reflection.View, reflection.ViewProjection, reflection.Eye);
            pass.CullsMirrored = false;
        }
    }

    private void RecordOpaque(GrPassRecorder pass)
    {
        pass.Begin(GrCullFrustum.Camera(_sceneView.ViewProjection), drawsMainPass: true);
        _device.SetRenderTargets(_sceneTargets);
        if (_debugView == GrDebugView.Overdraw) _device.GraphicsDevice.Clear(DebugSwapClearColor);
        var stepStarted = Stopwatch.GetTimestamp();
        var draws = SortedDraws(pass);
        _opaqueDraws.Clear();
        _nonOpaqueDraws.Clear();
        _distortionDraws.Clear();
        foreach (var draw in draws)
            (draw.Bucket == Gw2SortBuckets.Distortion ? _distortionDraws : Gw2SortBuckets.IsNonOpaque(draw.Bucket) ? _nonOpaqueDraws : _opaqueDraws).Add(draw);
        var firstDefaultBucket = _opaqueDraws.FindIndex(draw => draw.Bucket >= Gw2SortBuckets.Default);
        if (firstDefaultBucket < 0) firstDefaultBucket = _opaqueDraws.Count;
        GrSceneRecordingTimes.OpaqueSortMilliseconds += ElapsedStepMilliseconds(ref stepStarted);
        DrawSorted(pass, _opaqueDraws, 0, firstDefaultBucket);
        GrSceneRecordingTimes.OpaqueEarlyBucketsMilliseconds += ElapsedStepMilliseconds(ref stepStarted);
        DrawGpuMainGroups(pass);
        GrSceneRecordingTimes.OpaqueGpuGroupsMilliseconds += ElapsedStepMilliseconds(ref stepStarted);
        for (var index = firstDefaultBucket; index < _opaqueDraws.Count; index++)
            GrSceneRecordingTimes.OpaqueDefaultBucketDrawCounts[(int)_opaqueDraws[index].Kind]++;
        DrawSorted(pass, _opaqueDraws, firstDefaultBucket, _opaqueDraws.Count);
        GrSceneRecordingTimes.OpaqueDefaultBucketsMilliseconds += ElapsedStepMilliseconds(ref stepStarted);
    }

    private void DrawAfterOpaque(Func<Texture2D>? copyMainColor)
    {
        var fogStarted = Stopwatch.GetTimestamp();
        _device.SetRenderTargets(_sceneTargets);
        if (_volumetricFogMarched && _environmentFog is { } fog && _lightBuffer.Depth is { } depth)
            _volumetricFog!.Composite(depth, fog, _environmentLighting);
        GrSceneRecordingTimes.FogCompositeMilliseconds += ElapsedStepMilliseconds(ref fogStarted);
        _volumetricFogMarched = false;
        FrameTimer?.Mark(GrFrameSection.SceneRecording);
        var pass = _laterPass;
        var (_, view, _, viewProjection, eye) = _sceneView;
        pass.Begin(GrCullFrustum.Camera(viewProjection), drawsMainPass: true);
        AgentCapsules?.Draw(view, viewProjection, _environmentLighting?.ShSunData ?? Vector4.UnitZ);
        DrawWater(pass, view, viewProjection, eye, copyMainColor);
        FrameTimer?.Mark(GrFrameSection.Water);
        if (copyMainColor is not null && _nonOpaqueDraws.Count != 0) _engineTextures.ScreenCopy = copyMainColor();
        DrawSorted(pass, _nonOpaqueDraws);
        if (copyMainColor is not null && _distortionDraws.Count != 0) _engineTextures.ScreenCopy = copyMainColor();
        DrawSorted(pass, _distortionDraws);
        FrameTimer?.Mark(GrFrameSection.Translucent);
        _engineTextures.ScreenCopy = null;
    }

    public void SetDebugView(GrDebugView view)
    {
        _debugView = view;
        if (DebugSurfaceTechniqueOf(view) is not null) _debugSurface ??= _device.LoadShader("GrDebugSurface");
    }

    private static string? DebugSurfaceTechniqueOf(GrDebugView view) => view switch
    {
        GrDebugView.Overdraw => "Overdraw",
        GrDebugView.Clay => "Clay",
        GrDebugView.Lighting => "Lighting",
        GrDebugView.TexelDensity => "TexelDensity",
        GrDebugView.Albedo => "Albedo",
        GrDebugView.Wireframe => "Wire",
        _ => null,
    };

    private void DrawWater(GrPassRecorder pass, Matrix4x4 view, Matrix4x4 viewProjection, Vector3 eye, Func<Texture2D>? copyMainColor)
    {
        if (_water.Water is not { } water) return;
        var visible = water.Visible(viewProjection, eye, _water.FarClip);
        if (visible.Count == 0) return;
        if (copyMainColor is not null) _engineTextures.ScreenCopy = copyMainColor();
        pass.ActiveWaterReflection = _waterReflectionView?.Target;
        pass.DepthStencilState = DepthStencilState.Default;
        foreach (var (mesh, world) in visible)
            if (mesh.Material is { } material && WaterMaterial(material) is { } archiveMaterial)
                DrawMaterial(pass, mesh, archiveMaterial, world, view, viewProjection, eye);
        pass.DepthStencilState = DepthStencilState.Default;
        _engineTextures.ScreenCopy = null;
        pass.ActiveWaterReflection = null;
    }

    private GrWaterReflectionView? PrepareWaterReflection(Matrix4x4 view, Matrix4x4 projection)
    {
        if (_water.Water is not { } water || _debugView != GrDebugView.None || Reflections == GrReflections.None) return null;
        Matrix4x4.Invert(view, out var camera);
        var eye = new Vector3(camera.M41, camera.M42, camera.M43);
        if (water.ReflectionPlaneZ(view * projection, eye, _water.FarClip) is not { } planeZ) return null;
        var (mirroredView, mirroredEye) = GrWaterReflection.MirroredView(view, planeZ);
        var mirroredViewProjection = mirroredView * GrWaterReflection.ObliqueProjection(projection, mirroredView, planeZ);
        _waterReflection ??= new GrWaterReflection(_device);
        var viewport = _device.GraphicsDevice.Viewport;
        var target = _waterReflection.EnsureTargets((int)viewport.Width, (int)viewport.Height);
        return new GrWaterReflectionView(target, mirroredView, mirroredViewProjection, mirroredEye);
    }

    private void DrawWithArchiveShaders(GrPassRecorder pass, IGrMaterialMesh mesh, GrArchiveShaderMaterial material, Matrix4x4 world, Matrix4x4 view, Matrix4x4 viewProjection, Vector3 eye, IReadOnlyDictionary<uint, Vector4>? constants = null, Vector4? modelColor = null)
    {
        var technique = pass.DrawsMainPass ? DebugSurfaceTechniqueOf(_debugView) : null;
        if (technique is null || _debugView == GrDebugView.Wireframe) DrawMaterial(pass, mesh, material, world, view, viewProjection, eye, constants, modelColor);
        if (technique is not null && pass.PassRange != GrPassRange.Later) DrawDebugSurface(pass, mesh, material, world, view, viewProjection, mesh is IGrInstancedMesh ? "Instanced" + technique : technique, modelColor);
    }

    private static GrArchiveSampler? AlbedoSamplerOf(GrArchiveShaderMaterial material) =>
        material.Samplers.FirstOrDefault(sampler => sampler.Slot == AlbedoSlot && !sampler.IsEngineTexture);

    private void DrawDebugSurface(GrPassRecorder pass, IGrMaterialMesh mesh, GrArchiveShaderMaterial material, Matrix4x4 world, Matrix4x4 view, Matrix4x4 viewProjection, string technique, Vector4? modelColor)
    {
        var effect = _debugSurface!.GetProgram(technique);
        var graphicsDevice = _device.GraphicsDevice;
        pass.Constants.SetWorld(world);
        pass.Constants.Apply(effect, viewProjection);
        effect.Parameters["View"]?.SetValue(view);
        effect.Parameters["ModelColor"]?.SetValue(modelColor ?? Vector4.One);
        effect.Parameters["SunDirection"]?.SetValue(_environmentLighting?.ShSunData ?? System.Numerics.Vector4.UnitZ);
        effect.Parameters["WireColor"]?.SetValue(WireColor);
        SetRegister(effect, "LightBuffer", GrEngineConstants.LightBuffer);
        SetRegister(effect, "TexelOffset", GrEngineConstants.TexelOffset);
        SetRegister(effect, "ScreenDims", GrEngineConstants.ScreenDims((int)graphicsDevice.Viewport.Width, (int)graphicsDevice.Viewport.Height, _cameraNear, _cameraFar));
        effect.Parameters["LightBufferTexture"]?.SetValue(_lightBuffer.Texture);
        effect.Parameters["LightBufferSampler"]?.SetValue(_device.Sampler(SamplerDescription.PointClamp));
        var albedo = AlbedoSamplerOf(material);
        var albedoTexture = albedo is not null ? mesh.MaterialTexture(albedo.ResourceIndex) ?? _blankAtlas : _blankAtlas;
        effect.Parameters["Albedo"]?.SetValue(albedoTexture);
        effect.Parameters["TextureSize"]?.SetValue(albedoTexture is Texture2D texture ? new Vector2(texture.Width, texture.Height) : Vector2.One);
        effect.Parameters["AlbedoSampler"]?.SetValue(_device.Sampler(albedo is not null ? GrSamplerStates.From(albedo.State) : SamplerDescription.LinearWrap));

        var (blendState, depthStencilState, rasterizerState) = _debugView switch
        {
            GrDebugView.Overdraw => (BlendState.Additive, DepthStencilState.None, pass.RasterizerState),
            GrDebugView.Wireframe => (BlendState.Opaque, DepthStencilState.DepthRead, WireRasterizerState),
            _ => (BlendState.Opaque, pass.DepthStencilState, pass.RasterizerState),
        };
        _device.ApplyPipeline(effect, blendState, depthStencilState, rasterizerState, mesh.VertexLayout);
        mesh.Draw(graphicsDevice);
    }

    private void DrawReflectionProps(GrPassRecorder pass, Matrix4x4 view, Matrix4x4 viewProjection, Vector3 eye)
    {
        var rasterizerState = pass.RasterizerState;
        foreach (var (mesh, propWorld, constants, modelColor, material, _, _, _) in PropsInView(pass.CameraFrustum).Where(prop => prop.Material is not null && !Gw2SortBuckets.IsNonOpaque(prop.Material.SortBucket)))
        {
            pass.RasterizerState = PropRasterizerState(pass, material!, mesh.TwoSided, rasterizerState);
            DrawWithArchiveShaders(pass, mesh, ReflectionMaterial(mesh.MaterialFileId, material!), propWorld, view, viewProjection, eye, constants, modelColor);
        }

        foreach (var (instances, material, _) in VisiblePropInstances(pass.CameraFrustum).Where(entry => entry.Material is not null && !Gw2SortBuckets.IsNonOpaque(entry.Material.SortBucket)))
        {
            pass.RasterizerState = PropRasterizerState(pass, material!, instances.TwoSided, rasterizerState);
            DrawWithArchiveShaders(pass, instances, InstancedReflectionMaterial(instances.MaterialFileId, material!), Matrix4x4.Identity, view, viewProjection, eye, WithFadeColor(instances));
        }
        pass.RasterizerState = rasterizerState;
    }

    private enum GrPassRange
    {
        All,
        First,
        Later,
    }

    private readonly record struct GrSortedDraw(int Bucket, float Depth, long StateOrder, int GeometryOrder, GrSortedDrawKind Kind, GrPassRange Range, IGrMaterialMesh Mesh, GrArchiveShaderMaterial? Material, GrSceneProp? Prop)
    {
        public bool BatchesWith(in GrSortedDraw next) =>
            Kind == GrSortedDrawKind.Prop && next.Kind == GrSortedDrawKind.Prop && Range == next.Range && ReferenceEquals(Mesh, next.Mesh) && ReferenceEquals(Material, next.Material)
            && ReferenceEquals(Prop!.Constants, next.Prop!.Constants) && Prop.ModelColor == next.Prop.ModelColor;
    }

    private readonly Dictionary<(GrStaticMesh Mesh, GrArchiveShaderMaterial Material, IReadOnlyDictionary<uint, Vector4> Constants, Vector4 ModelColor), List<Matrix4x4>> _normalSpecWorldsByGroup = [];

    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<IGrMaterialMesh, object> _geometryOrderByMesh = new();
    private int _geometryCount;

    private int GeometryOrderOf(IGrMaterialMesh mesh) => (int)_geometryOrderByMesh.GetValue(mesh, _ => ++_geometryCount);

    private readonly Dictionary<string, int> _textureListIds = new();
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<IGrMaterialMesh, object> _textureListIdByMesh = new();

    private int TextureListIdOf(IGrMaterialMesh mesh)
    {
        if (_textureListIdByMesh.TryGetValue(mesh, out var cached)) return (int)cached;
        var textures = mesh.MaterialTextureCount == int.MaxValue
            ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(mesh).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : string.Join(',', Enumerable.Range(0, mesh.MaterialTextureCount).Select(index => mesh.MaterialTexture(index) is { } texture ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(texture) : 0));
        if (!_textureListIds.TryGetValue(textures, out var id)) _textureListIds[textures] = id = _textureListIds.Count;
        _textureListIdByMesh.AddOrUpdate(mesh, id);
        return id;
    }

    private readonly List<GrSortedDraw> _submittedDraws = [];
    private readonly List<GrSortedDraw> _sortedDraws = [];
    private readonly List<GrSortedDraw> _opaqueDraws = [];
    private readonly List<GrSortedDraw> _nonOpaqueDraws = [];
    private readonly List<GrSortedDraw> _distortionDraws = [];

    private void AddSortedDraw(GrArchiveShaderMaterial material, IGrMaterialMesh mesh, BoundingBox bounds, GrSortedDrawKind kind, GrSceneProp? prop = null)
    {
        var stateOrder = ((long)material.CreationOrder << 32) | (uint)TextureListIdOf(mesh);
        var depth = GrWorldBounds.SortDepth(bounds, _sceneView.View);
        var geometryOrder = GeometryOrderOf(mesh);
        if (material.SortBucket != Gw2SortBuckets.SplitPasses)
        {
            _submittedDraws.Add(new(material.SortBucket, depth, stateOrder, geometryOrder, kind, GrPassRange.All, mesh, material, prop));
            return;
        }

        _submittedDraws.Add(new(Gw2SortBuckets.SplitFirstPass, depth, stateOrder, geometryOrder, kind, GrPassRange.First, mesh, material, prop));
        if (material.LaterPasses.Count != 0)
            _submittedDraws.Add(new(Gw2SortBuckets.SplitLaterPasses, depth, stateOrder, geometryOrder, kind, GrPassRange.Later, mesh, material, prop));
    }

    private List<GrSortedDraw> SortedDraws(GrPassRecorder pass)
    {
        var (world, view, _, _, _) = _sceneView;
        var frustum = pass.CameraFrustum;
        _submittedDraws.Clear();
        foreach (var (chunk, material, _) in _terrainChunks)
            if (material is not null && frustum.Intersects(chunk.Bounds))
                AddSortedDraw(material, chunk, chunk.Bounds, GrSortedDrawKind.Terrain);

        foreach (var prop in _props)
            if (prop.GpuMainMaterial is null && prop.Material is { } material && prop.LodState.Level == prop.LodLevel && frustum.Intersects(prop.WorldBounds))
                AddSortedDraw(material, prop.Mesh, prop.WorldBounds, GrSortedDrawKind.Prop, prop);

        foreach (var (instances, material, _) in VisiblePropInstances(frustum))
            if (material is not null)
                AddSortedDraw(material, instances, instances.Bounds, GrSortedDrawKind.PropInstances);

        foreach (var mesh in _meshes)
        {
            if (_archiveMaterialByMesh.TryGetValue(mesh, out var archiveMaterial))
                AddSortedDraw(archiveMaterial, mesh, CharacterBounds(mesh, world), GrSortedDrawKind.CharacterMaterial);
            else
                _submittedDraws.Add(new(Gw2SortBuckets.Default, GrWorldBounds.SortDepth(CharacterBounds(mesh, world), view), -1, 0, GrSortedDrawKind.Character, GrPassRange.All, mesh, null, null));
        }

        foreach (var mesh in _agentSkinnedMeshes)
            if (!mesh.HasBounds || frustum.Intersects(CharacterBounds(mesh, Matrix4x4.Identity)))
                _submittedDraws.Add(new(Gw2SortBuckets.Default, GrWorldBounds.SortDepth(CharacterBounds(mesh, Matrix4x4.Identity), view), -1, 0, GrSortedDrawKind.AgentCharacter, GrPassRange.All, mesh, null, null));

        foreach (var cloud in _effectParticleClouds)
            if (cloud.QuadCount != 0 && frustum.Intersects(cloud.Bounds) && ArchiveMaterial(_archiveMaterials, cloud.MaterialFileId, Gw2AmatEffectKeys.Default) is { } particleMaterial)
                AddSortedDraw(particleMaterial, cloud, cloud.Bounds, GrSortedDrawKind.EffectParticles);

        _sortKeys.Clear();
        for (var submitOrder = 0; submitOrder < _submittedDraws.Count; submitOrder++)
            _sortKeys.Add(SortKeyOf(_submittedDraws[submitOrder], submitOrder));
        _sortKeys.Sort();
        _sortedDraws.Clear();
        foreach (var key in _sortKeys) _sortedDraws.Add(_submittedDraws[key.SubmitOrder]);
        return _sortedDraws;
    }

    private readonly List<GrSortedDrawKey> _sortKeys = [];

    private readonly record struct GrSortedDrawKey(int Bucket, long StateOrder, int GeometryOrder, float SignedDepth, int SubmitOrder) : IComparable<GrSortedDrawKey>
    {
        public int CompareTo(GrSortedDrawKey other)
        {
            var order = Bucket.CompareTo(other.Bucket);
            if (order == 0) order = StateOrder.CompareTo(other.StateOrder);
            if (order == 0) order = GeometryOrder.CompareTo(other.GeometryOrder);
            if (order == 0) order = SignedDepth.CompareTo(other.SignedDepth);
            return order != 0 ? order : SubmitOrder.CompareTo(other.SubmitOrder);
        }
    }

    private static GrSortedDrawKey SortKeyOf(GrSortedDraw draw, int submitOrder)
    {
        var sortType = Gw2SortBuckets.TypeOf(draw.Bucket);
        var byState = sortType == Gw2SortType.ByStateThenFrontToBack;
        var signedDepth = sortType switch
        {
            Gw2SortType.FrontToBack or Gw2SortType.ByStateThenFrontToBack => draw.Depth,
            Gw2SortType.BackToFront => -draw.Depth,
            _ => 0f,
        };
        return new(draw.Bucket, byState ? draw.StateOrder : 0, byState ? draw.GeometryOrder : 0, signedDepth, submitOrder);
    }

    private void DrawSorted(GrPassRecorder pass, List<GrSortedDraw> draws) => DrawSorted(pass, draws, 0, draws.Count);

    private void DrawSorted(GrPassRecorder pass, List<GrSortedDraw> draws, int start, int end)
    {
        var (_, view, _, viewProjection, eye) = _sceneView;
        var unculled = pass.RasterizerState;
        for (var index = start; index < end;)
        {
            var draw = draws[index];
            var runLength = 1;
            while (runLength < GrInstanceStream.MaximumInstancesPerDraw && index + runLength < end && draw.BatchesWith(draws[index + runLength]))
                runLength++;
            var instancedMaterial = runLength > 1 ? InstancedMaterial(_instancedMaterials, draw.Prop!.Mesh, Gw2AmatEffectKeys.Default) : null;
            if (instancedMaterial is null)
            {
                for (var single = 0; single < runLength; single++)
                {
                    DrawSortedItem(pass, draws[index + single], unculled);
                    pass.RasterizerState = unculled;
                }
            }
            else
            {
                var prop = draw.Prop!;
                for (var instance = 0; instance < runLength; instance++)
                    pass.InstanceScratch[instance] = GrVertexInstance.From(draws[index + instance].Prop!.World, prop.ModelColor);
                pass.PassRange = draw.Range;
                pass.RasterizerState = PropRasterizerState(pass, draw.Material!, prop.Mesh.TwoSided, unculled);
                DrawWithArchiveShaders(pass, pass.InstanceStream.Append(prop.Mesh, pass.InstanceScratch, runLength), instancedMaterial, Matrix4x4.Identity, view, viewProjection, eye, prop.Constants, prop.ModelColor);
                pass.PassRange = GrPassRange.All;
                pass.RasterizerState = unculled;
            }
            index += runLength;
        }
    }

    private void DrawGpuMainGroups(GrPassRecorder pass)
    {
        if (_gpuScene is null) return;
        var (_, view, _, viewProjection, eye) = _sceneView;
        GrGpuScene.GrOcclusion? occlusion = _hiZ is not null && _hiZViewProjection == viewProjection ? new GrGpuScene.GrOcclusion(_hiZ, viewProjection) : null;
        _gpuScene.Cull(OpaqueCullSlot, pass.CameraFrustum, casters: false, pass.GpuVisibleDrawGroups, occlusion);
        var unculled = pass.RasterizerState;
        foreach (var group in pass.GpuVisibleDrawGroups)
            if (_gpuScene.DrawGroupProp(group) is { GpuMainMaterial: { } instancedMaterial, Material: { } material } prop)
            {
                pass.RasterizerState = PropRasterizerState(pass, material, prop.Mesh.TwoSided, unculled);
                DrawWithArchiveShaders(pass, _gpuScene.Batch(OpaqueCullSlot, casters: false, group), instancedMaterial, Matrix4x4.Identity, view, viewProjection, eye, prop.Constants, prop.ModelColor);
                pass.RasterizerState = unculled;
            }
    }

    private void DrawGpuNormalSpecGroups(GrPassRecorder pass, Matrix4x4 view, Matrix4x4 viewProjection, Vector3 eye)
    {
        if (_gpuScene is null) return;
        _gpuScene.Cull(CameraCullSlot, pass.CameraFrustum, casters: false, pass.GpuVisibleDrawGroups);
        foreach (var group in pass.GpuVisibleDrawGroups)
            if (_gpuScene.DrawGroupProp(group) is { GpuNormalSpecMaterial: { } instancedMaterial } prop)
                DrawWithArchiveShaders(pass, _gpuScene.Batch(CameraCullSlot, casters: false, group), instancedMaterial, Matrix4x4.Identity, view, viewProjection, eye, prop.Constants, prop.ModelColor);
    }

    private IReadOnlyList<GrDepthCaster> GpuDepthCasters(int cascade, GrCullFrustum frustum)
    {
        _gpuDepthCasters.Clear();
        var slot = FirstCascadeCullSlot + cascade;
        _gpuScene!.Cull(slot, frustum, casters: true, _gpuVisibleCasterGroups);
        foreach (var group in _gpuVisibleCasterGroups)
        {
            var mesh = _gpuScene.CasterGroupProp(group).Mesh;
            if (!_gpuDepthCasterByMesh.TryGetValue(mesh, out var caster))
                _gpuDepthCasterByMesh[mesh] = caster = AlphaMaskedCaster(mesh, Matrix4x4.Identity, mesh.MaterialFileId, default) with { WorldBounds = null };
            _gpuDepthCasters.Add(caster.WithCurrentAlphaMask() with { Mesh = _gpuScene.Batch(slot, casters: true, group) });
        }
        return _gpuDepthCasters;
    }

    private void DrawSortedItem(GrPassRecorder pass, in GrSortedDraw draw, RasterizerState unculled)
    {
        var (world, view, _, viewProjection, eye) = _sceneView;
        pass.PassRange = draw.Range;
        switch (draw.Kind)
        {
            case GrSortedDrawKind.Terrain:
                DrawWithArchiveShaders(pass, draw.Mesh, draw.Material!, Matrix4x4.Identity, view, viewProjection, eye);
                break;
            case GrSortedDrawKind.Prop:
                var prop = draw.Prop!;
                pass.RasterizerState = PropRasterizerState(pass, draw.Material!, prop.Mesh.TwoSided, unculled);
                DrawWithArchiveShaders(pass, prop.Mesh, draw.Material!, prop.World, view, viewProjection, eye, prop.Constants, prop.ModelColor);
                break;
            case GrSortedDrawKind.PropInstances:
                var instances = (GrStaticMeshInstances)draw.Mesh;
                pass.RasterizerState = PropRasterizerState(pass, draw.Material!, instances.TwoSided, unculled);
                DrawWithArchiveShaders(pass, instances, draw.Material!, Matrix4x4.Identity, view, viewProjection, eye, WithFadeColor(instances));
                break;
            case GrSortedDrawKind.CharacterMaterial:
                DrawMaterial(pass, draw.Mesh, draw.Material!, world, view, viewProjection, eye);
                break;
            case GrSortedDrawKind.Character:
                DrawCharacterMesh(pass, (GrSkinnedMesh)draw.Mesh, world, viewProjection);
                break;
            case GrSortedDrawKind.AgentCharacter:
                DrawCharacterMesh(pass, (GrSkinnedMesh)draw.Mesh, Matrix4x4.Identity, viewProjection);
                break;
            case GrSortedDrawKind.EffectParticles:
                pass.RasterizerState = unculled;
                DrawWithArchiveShaders(pass, draw.Mesh, draw.Material!, Matrix4x4.Identity, view, viewProjection, eye);
                break;
        }
        pass.PassRange = GrPassRange.All;
    }

    private RasterizerState PropRasterizerState(GrPassRecorder pass, GrArchiveShaderMaterial material, bool twoSided, RasterizerState unculled) =>
        twoSided || _debugView == GrDebugView.Wireframe ? unculled : (pass.CullsMirrored ? Gw2.Dat.Content.Gw2AmatEffectCull.Mirrored(material.Cull) : material.Cull) switch
        {
            Gw2.Dat.Content.Gw2AmatCull.CounterClockwise => RasterizerState.CullBack,
            Gw2.Dat.Content.Gw2AmatCull.Clockwise => RasterizerState.CullFront,
            _ => unculled,
        };

    private void DrawCharacterMesh(GrPassRecorder pass, GrSkinnedMesh mesh, Matrix4x4 world, Matrix4x4 viewProjection)
    {
        if (mesh.IsSkinnedOnGpu && _agentPaletteBuffer is null) return;
        var graphicsDevice = _device.GraphicsDevice;
        var program = mesh.IsSkinnedOnGpu ? _skinnedCharacter : _character;
        if (mesh.IsSkinnedOnGpu)
        {
            program.Parameters["PaletteColumns"]?.SetValue(_agentPaletteBuffer);
            program.Parameters["PaletteOffset"]?.SetValue((uint)mesh.PaletteOffset);
            program.Parameters["PaletteBoneCount"]?.SetValue((uint)mesh.PaletteBoneCount);
        }
        pass.Constants.SetWorld(world);
        pass.Constants.Apply(program, viewProjection);
        program.Parameters["Atlas"]?.SetValue(mesh.Atlas);
        program.Parameters["NormalAtlas"]?.SetValue(mesh.NormalAtlas);
        program.Parameters["TintMultiply"]?.SetValue(mesh.TintMultiply);
        program.Parameters["TintOffset"]?.SetValue(mesh.TintOffset);
        program.Parameters["TintContrast"]?.SetValue(mesh.TintContrast);
        program.Parameters["TintOpacity"]?.SetValue(mesh.TintOpacity);
        program.Parameters["AtlasSampler"]?.SetValue(_device.Sampler(SamplerDescription.LinearClamp));
        program.Parameters["NormalAtlasSampler"]?.SetValue(_device.Sampler(SamplerDescription.LinearClamp));
        _device.ApplyPipeline(program, BlendState.Opaque, pass.DepthStencilState, pass.RasterizerState, mesh.VertexLayout);
        mesh.Draw(graphicsDevice);
    }

    private void DrawNormalSpecGroup(GrPassRecorder pass, GrStaticMesh mesh, GrArchiveShaderMaterial material, IReadOnlyDictionary<uint, Vector4> constants, Vector4 modelColor, List<Matrix4x4> worlds, Matrix4x4 view, Matrix4x4 viewProjection, Vector3 eye)
    {
        var instancedMaterial = worlds.Count > 1 ? InstancedMaterial(_instancedNormalSpecMaterials, mesh, Gw2AmatEffectKeys.NormalSpecDepth) : null;
        if (instancedMaterial is null)
        {
            foreach (var world in worlds) DrawWithArchiveShaders(pass, mesh, material, world, view, viewProjection, eye, constants, modelColor);
            return;
        }
        for (var first = 0; first < worlds.Count; first += GrInstanceStream.MaximumInstancesPerDraw)
        {
            var count = Math.Min(GrInstanceStream.MaximumInstancesPerDraw, worlds.Count - first);
            for (var index = 0; index < count; index++) pass.InstanceScratch[index] = GrVertexInstance.From(worlds[first + index], modelColor);
            DrawWithArchiveShaders(pass, pass.InstanceStream.Append(mesh, pass.InstanceScratch, count), instancedMaterial, Matrix4x4.Identity, view, viewProjection, eye, constants, modelColor);
        }
    }

    private void DrawMaterial(GrPassRecorder pass, IGrMaterialMesh mesh, GrArchiveShaderMaterial material, Matrix4x4 world, Matrix4x4 view, Matrix4x4 viewProjection, Vector3 eye, IReadOnlyDictionary<uint, Vector4>? constants = null, Vector4? modelColor = null)
    {
        if (pass.PassRange != GrPassRange.Later) DrawMaterialPass(pass, mesh, material, world, view, viewProjection, eye, constants, modelColor);
        if (_debugView != GrDebugView.None || pass.PassRange == GrPassRange.First) return;
        foreach (var laterPass in material.LaterPasses)
            DrawMaterialPass(pass, mesh, laterPass, world, view, viewProjection, eye, constants, modelColor);
    }

    private void DrawMaterialPass(GrPassRecorder pass, IGrMaterialMesh mesh, GrArchiveShaderMaterial material, Matrix4x4 world, Matrix4x4 view, Matrix4x4 viewProjection, Vector3 eye, IReadOnlyDictionary<uint, Vector4>? constants, Vector4? modelColor)
    {
        GrDrawConstants.Apply(material, DrawFrame(pass, view, viewProjection, eye), new GrDrawItem(world, modelColor ?? Vector4.One, 0, constants ?? mesh.MaterialConstants, mesh.TextureTransforms));
        pass.BindingMesh = mesh;
        GrDrawTextures.Bind(_device, material, pass.TextureOfSampler!, AnisotropicFiltering);
        pass.BindingMesh = null;

        var (blendState, depthStencilState, rasterizerState) = _debugView == GrDebugView.None
            ? (GrBlendStates.From(material.RenderState, material.DrawState.ColorWrite), GrDrawStates.DepthStencil(material.DrawState), GrDrawStates.WithDepthBias(pass.RasterizerState, material.DrawState))
            : (GrBlendStates.From(material.RenderState), pass.DepthStencilState, pass.RasterizerState);
        _device.ApplyPipeline(material.Program, blendState, depthStencilState, rasterizerState, mesh.VertexLayout);
        mesh.Draw(_device.GraphicsDevice);
    }

    private Texture TextureOfSampler(GrPassRecorder pass, GrArchiveSampler sampler) => sampler switch
    {
        { IsVertexStage: true, IsEngineTexture: true } => _engineTextures.BlackFallback,
        { ParameterName: WaterReflectionSamplerName } when pass.ActiveWaterReflection is { } waterReflection => waterReflection,
        { IsEngineTexture: true, ResourceIndex: LightBufferEngineTexture } when !pass.DrawsReflection && _lightBuffer.Texture is { } lightBuffer => lightBuffer,
        { IsEngineTexture: true } => _engineTextures.Resolve(sampler.ResourceIndex),
        { IsEngineTexture: false } when sampler.ResourceIndex >= pass.BindingMesh!.MaterialTextureCount => _engineTextures.BlackFallback,
        _ => pass.BindingMesh!.SamplerTexture(sampler) ?? (sampler.IsCube ? _device.BlackFallbackCube : _blankAtlas),
    };

    private GrDrawFrame DrawFrame(GrPassRecorder pass, Matrix4x4 view, Matrix4x4 viewProjection, Vector3 eye)
    {
        var viewport = _device.GraphicsDevice.Viewport;
        var drawFrame = pass.DrawFrame;
        drawFrame.View = view;
        drawFrame.ViewProjection = viewProjection;
        drawFrame.CameraPosition = eye;
        drawFrame.ViewportWidth = (int)viewport.Width;
        drawFrame.ViewportHeight = (int)viewport.Height;
        drawFrame.Near = _cameraNear;
        drawFrame.Far = _cameraFar;
        drawFrame.ClockSeconds = _passClockSeconds;
        drawFrame.ShLighting = _environmentLighting;
        drawFrame.Fog = _environmentFog;
        drawFrame.SetGlobal(GrGlobalConstantTokens.LightBuffer, GrEngineConstants.LightBuffer);
        drawFrame.SetGlobal(GrGlobalConstantTokens.CharLightCurve, _environmentLighting is { } lighting ? lighting.CharLightCurve : GrEngineConstants.DefaultCharLightCurve);
        drawFrame.SetGlobal(GrGlobalConstantTokens.BacklightColor, GrEngineConstants.BacklightColor);
        drawFrame.SetGlobal(GrGlobalConstantTokens.BacklightDirection, GrEngineConstants.BacklightDirection);
        drawFrame.SetGlobal(GrGlobalConstantTokens.BacklightShadowInfluence, GrEngineConstants.BacklightShadowInfluence);
        drawFrame.SetGlobal(GrGlobalConstantTokens.EyeColorRed, _eyeColor.Red);
        drawFrame.SetGlobal(GrGlobalConstantTokens.EyeColorGreen, _eyeColor.Green);
        drawFrame.SetGlobal(GrGlobalConstantTokens.EyeColorBlue, _eyeColor.Blue);
        drawFrame.SetGlobal(GrGlobalConstantTokens.WorldToShadowA, _farShadowMap?.Constants.WorldToShadowA ?? default);
        drawFrame.SetGlobal(GrGlobalConstantTokens.WorldToShadowB, _farShadowMap?.Constants.WorldToShadowB ?? default);
        drawFrame.SetGlobal(GrGlobalConstantTokens.WorldToShadowC, _farShadowMap?.Constants.WorldToShadowC ?? default);
        drawFrame.SetGlobal(GrGlobalConstantTokens.WorldToShadowD, _farShadowMap?.Constants.WorldToShadowD ?? default);
        return drawFrame;
    }

    private static void SetRegister(ShaderProgram effect, string name, Vector4 value) => effect.Parameters[name]?.SetValue(value);

    public void Dispose()
    {
        foreach (var (chunk, _, _) in _terrainChunks) chunk.Dispose();
        _terrainChunks.Clear();
        _props.Clear();
        _propInstances.Clear();
        foreach (var mesh in _meshes) mesh.Dispose();
        _meshes.Clear();
        foreach (var mesh in _agentSkinnedMeshes) mesh.Dispose();
        _agentPaletteBuffer?.Dispose();
        _agentSkinnedMeshes.Clear();
        foreach (var atlas in _atlases) atlas.Dispose();
        _atlases.Clear();
        _blankAtlas.Dispose();
        _flatNormalAtlas.Dispose();
        _farShadowMap?.Dispose();
        _engineTextures.Dispose();
        _lightBuffer.Dispose();
        _volumetricFog?.Dispose();
        _ambientOcclusion?.Dispose();
        _linearDepthCopy.Dispose();
        _shadowMap.Dispose();
        _hiZ?.Dispose();
        _gpuScene?.Dispose();
        _waterReflection?.Dispose();
        foreach (var pass in _passesInExecutionOrder) pass.Dispose();
    }

    private readonly record struct GrSceneView(Matrix4x4 World, Matrix4x4 View, Matrix4x4 Projection, Matrix4x4 ViewProjection, Vector3 Eye);

    private readonly record struct GrWaterReflectionView(Texture2D Target, Matrix4x4 View, Matrix4x4 ViewProjection, Vector3 Eye);

    private sealed class GrPassRecorder : IDisposable
    {
        private static readonly Vector3 LightDirection = Vector3.Normalize(new Vector3(-0.4f, 0.6f, 0.7f));
        private static readonly Vector3 LightColor = new(0.9f, 0.85f, 0.8f);

        public GrPassRecorder(GrFrameSection section, GraphicsCommandList? commandList)
        {
            Section = section;
            CommandList = commandList;
            Constants.SetLight(LightDirection, LightColor);
        }

        public GrFrameSection Section { get; }
        public GraphicsCommandList? CommandList { get; }
        public GrConstants Constants { get; } = new();
        public GrDrawFrame DrawFrame { get; } = new();
        public GrInstanceStream InstanceStream { get; } = new();
        public GrVertexInstance[] InstanceScratch { get; } = new GrVertexInstance[GrInstanceStream.MaximumInstancesPerDraw];
        public List<int> GpuVisibleDrawGroups { get; } = [];
        public Func<GrArchiveSampler, Texture>? TextureOfSampler { get; set; }
        public RasterizerState RasterizerState { get; set; } = RasterizerState.CullNone;
        public DepthStencilState DepthStencilState { get; set; } = DepthStencilState.Default;
        public GrPassRange PassRange { get; set; }
        public bool DrawsMainPass { get; private set; }
        public bool DrawsReflection { get; set; }
        public bool CullsMirrored { get; set; }
        public GrCullFrustum CameraFrustum { get; private set; } = GrCullFrustum.Camera(Matrix4x4.Identity);
        public Texture2D? ActiveWaterReflection { get; set; }
        public IGrMaterialMesh? BindingMesh { get; set; }

        public void Begin(GrCullFrustum cameraFrustum, bool drawsMainPass)
        {
            InstanceStream.BeginFrame();
            RasterizerState = RasterizerState.CullNone;
            DepthStencilState = DepthStencilState.Default;
            PassRange = GrPassRange.All;
            DrawsMainPass = drawsMainPass;
            DrawsReflection = false;
            CullsMirrored = false;
            CameraFrustum = cameraFrustum;
            ActiveWaterReflection = null;
        }

        public void Dispose() => CommandList?.Dispose();
    }
}
