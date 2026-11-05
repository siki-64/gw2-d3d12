using System.Threading.Tasks;
﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading;
using Gw2.Client.Content;
using Gw2.Client.Model;
using Gw2.Client.Scene;
using Gw2.Dat.Content;
using Gw2.Formats;
using Gw2.Client.Graphics;
using Crystal.Graphics;
using ScatterGroup = System.Linq.IGrouping<(Gw2.Dat.Content.Gw2ZoneScatterBlock Block, int LayerIndex, uint ModelFileId), Gw2.Dat.Content.Gw2ZoneScatterInstance>;

namespace Gw2.Client;

public sealed class Gw2PropLoader : IDisposable
{
    private const uint WholeModelSubmodel = 0;
    private const float ColorByteScale = 1f / 255f;
    private const int LevelCount = 3;
    private const int InstancesPerDraw = 1000;
    private static readonly TimeSpan UploadBudget = TimeSpan.FromMilliseconds(5);
    private static readonly TimeSpan FirstFillUploadBudget = TimeSpan.FromMilliseconds(25);
    private bool _hasSettledOnce;

    private readonly GraphicsDevice _device;
    private readonly Gw2ContentService _content;
    private readonly GrCharacterPass _pass;
    private readonly Gw2ArchiveMaterials _materials;
    private readonly Gw2MapBundle _map;
    private readonly Gw2PropActivation _propActivation = new();
    private readonly Gw2ScatterActivation? _scatterActivation;
    private readonly IReadOnlyDictionary<(int X, int Y), List<ScatterGroup>> _scatterGroupsByBlock = new Dictionary<(int X, int Y), List<ScatterGroup>>();
    private readonly Vector2 _origin;
    private readonly Dictionary<Gw2MapPropPlacement, Gw2PropInstance> _loadedProps = new();
    private readonly Dictionary<Gw2MapPropPlacement, List<Gw2PropAnimatedSurface>> _animatedSurfaces = new();
    private readonly Stopwatch _animationClock = Stopwatch.StartNew();
    private readonly Dictionary<(int X, int Y), List<GrStaticMeshInstances>> _loadedScatter = new();
    private readonly List<Gw2MapPropPlacement> _pendingProps = new();
    private readonly HashSet<Gw2MapPropPlacement> _pendingPropSet = new();
    private readonly List<Gw2MapPropPlacement> _candidates = new();
    private readonly HashSet<GrPropLodState> _removedLodStates = new();
    private readonly Gw2PlacementGrid<Gw2MapPropPlacement>? _placementGrid;
    private Gw2PlacementRect? _activeRect;
    private readonly List<(int X, int Y)> _pendingBlocks = new();
    private readonly Dictionary<(int X, int Y), IEnumerator<GrStaticMeshInstances>> _scatterBuilds = new();
    private readonly Dictionary<(int X, int Y), Task> _scatterMaterialPreparations = new();
    private readonly List<(int X, int Y)> _released = new();
    private readonly List<(int X, int Y)> _activated = new();
    private readonly Dictionary<Gw2PropModelKey, Gw2PropModelEntry> _models = new();
    private readonly Dictionary<uint, Gw2PropTextureEntry> _textures = new();
    private readonly GrTexResidency _residency;
    private readonly Gw2HighResolutionFiles? _highResolutionFiles;
    private readonly List<uint> _promotions = new();
    private readonly List<uint> _demotions = new();
    private readonly List<uint> _pendingPromotions = new();
    private readonly List<Gw2AttachedModel> _attachedModels = new();
    private readonly List<Gw2AttachedModel> _attachedModelsToPlace = new();
    private readonly HashSet<GrPropLodState> _replacedAttachedLodStates = new();

    public Gw2PropLoader(GraphicsDevice device, Gw2ContentService content, Gw2ArchiveMaterials materials, GrCharacterPass pass, Gw2MapBundle map, Gw2LodDistance lodDistance, GrTextureQuality textureQuality, Gw2HighResolutionFiles? highResolutionFiles)
    {
        _residency = new GrTexResidency(textureQuality);
        _highResolutionFiles = highResolutionFiles;
        _materials = materials;
        _device = device;
        _content = content;
        _pass = pass;
        _map = map;
        if (map.Props is { } props) _placementGrid = new Gw2PlacementGrid<Gw2MapPropPlacement>(props.Placements, placement => new Vector2(placement.Position.X, placement.Position.Y));
        if (map.Terrain is not { } terrain || map.Zones is not { } zones || map.Info.Bounds.Count < 4) return;
        _origin = new Vector2(map.Info.Bounds[0], map.Info.Bounds[1]);
        if (Gw2TerrainGround.From(terrain, _origin) is not { } ground) return;
        Ground = ground;
        _scatterActivation = new Gw2ScatterActivation(_origin);
        var water = map.Collision is { } collision ? Gw2CollisionWater.From(collision) : null;
        _scatterGroupsByBlock = Gw2ZoneScatter.DrawGroups(Gw2ZoneScatter.Place(zones, Gw2ZoneScatterMapBounds.From(map.Info.Bounds), ground, Gw2LodDistanceSetting.ScatterQualityMode(lodDistance), water))
            .Where(group => group.Key.ModelFileId != 0)
            .GroupBy(group => (group.Key.Block.X, group.Key.Block.Y))
            .ToDictionary(blocks => blocks.Key, blocks => blocks.ToList());
    }

    public Gw2TerrainGround? Ground { get; }

    public IReadOnlyList<Gw2MapDecalInfo> MapDecals => _map.Decals?.Decals ?? [];

    public IEnumerable<Gw2PropInstance> LoadedProps => _loadedProps.Values;

    public int ResidentModelCount => _models.Count;

    public int ResidentTextureCount => _textures.Count;

    public int HighResolutionTextureCount => _residency.HighResolutionCount;

    public bool IsSettled => _pendingProps.Count == 0 && _pendingBlocks.Count == 0;

    public double LastActivateMilliseconds { get; private set; }

    public int LastUploadedTextures { get; private set; }

    public int LastUploadedMeshes { get; private set; }

    public int LastLoadedProps { get; private set; }

    public int LastLoadedBlocks { get; private set; }

    public double LastLongestStepMilliseconds { get; private set; }

    public string LastLongestStepKind { get; private set; } = "";

    public void Update(Vector3 view, float viewDistance)
    {
        LastUploadedTextures = 0;
        LastUploadedMeshes = 0;
        LastLoadedProps = 0;
        LastLoadedBlocks = 0;
        LastLongestStepMilliseconds = 0;
        LastLongestStepKind = "";
        var activateStart = Stopwatch.GetTimestamp();
        Activate(view, viewDistance);
        LastActivateMilliseconds = Stopwatch.GetElapsedTime(activateStart).TotalMilliseconds;
        UpdateResidency(view);
        Upload(_hasSettledOnce ? UploadBudget : FirstFillUploadBudget);
        _hasSettledOnce |= IsSettled;
    }

    public void LoadAll(Vector3 view, float viewDistance)
    {
        Activate(view, viewDistance);
        while (!IsSettled)
            if (!Upload(TimeSpan.MaxValue)) Thread.Sleep(1);
    }

    private void Activate(Vector3 view, float viewDistance)
    {
        if (_placementGrid is not null && _propActivation.Update(view, viewDistance))
        {
            var active = new Gw2PlacementRect(_propActivation.Min, _propActivation.Max);
            _candidates.Clear();
            _placementGrid.CollectChanged(_activeRect, active, _candidates);
            _activeRect = active;
            _removedLodStates.Clear();
            var added = false;
            foreach (var placement in _candidates)
            {
                if (_propActivation.Contains(placement.Position))
                {
                    if (_loadedProps.ContainsKey(placement) || !_pendingPropSet.Add(placement)) continue;
                    _pendingProps.Add(placement);
                    AcquireModel(KeyOf(placement));
                    added = true;
                }
                else if (_pendingPropSet.Remove(placement))
                {
                    ReleaseModel(KeyOf(placement));
                }
                else if (_loadedProps.Remove(placement, out var loaded))
                {
                    _animatedSurfaces.Remove(placement);
                    _removedLodStates.Add(loaded.LodState);
                    ReleaseModel(KeyOf(placement));
                }
            }
            _pendingProps.RemoveAll(placement => !_pendingPropSet.Contains(placement));
            _pass.RemoveProps(_removedLodStates);
            if (added)
            {
                var viewXy = new Vector2(view.X, view.Y);
                _pendingProps.Sort((left, right) => DistanceSquared(left.Position, viewXy).CompareTo(DistanceSquared(right.Position, viewXy)));
            }
        }

        if (_scatterActivation is null) return;
        _released.Clear();
        _activated.Clear();
        if (!_scatterActivation.Update(view, viewDistance, _released, _activated)) return;
        foreach (var block in _released)
        {
            var wasPending = _pendingBlocks.Remove(block);
            if (_scatterBuilds.Remove(block, out var build)) build.Dispose();
            _scatterMaterialPreparations.Remove(block);
            var wasLoaded = _loadedScatter.Remove(block, out var instances);
            foreach (var meshInstances in instances ?? [])
            {
                _pass.RemovePropInstances(meshInstances);
                meshInstances.Dispose();
            }
            if (wasPending || wasLoaded)
                foreach (var group in _scatterGroupsByBlock[block]) ReleaseModel(ScatterKeyOf(group));
        }
        foreach (var block in _activated)
        {
            if (!_scatterGroupsByBlock.TryGetValue(block, out var groups)) continue;
            _pendingBlocks.Add(block);
            foreach (var group in groups)
            {
                AcquireModel(ScatterKeyOf(group));
                _materials.Request(GroundCoverMaterialFileIdOf(group));
            }
        }
        var viewInBlocks = (new Vector2(view.X, view.Y) - _origin) / Gw2ZoneScatterFade.BlockSize;
        _pendingBlocks.Sort((left, right) => BlockDistanceSquared(left, viewInBlocks).CompareTo(BlockDistanceSquared(right, viewInBlocks)));
    }

    public Gw2AttachedModel AttachModel(uint modelFileId)
    {
        var attached = new Gw2AttachedModel(new Gw2PropModelKey(modelFileId, []));
        AcquireModel(attached.Key);
        _attachedModels.Add(attached);
        return attached;
    }

    public Gw2AttachedModel AttachOffscreenModel(uint modelFileId)
    {
        var attached = AttachModel(modelFileId);
        attached.Offscreen = true;
        return attached;
    }

    public IReadOnlyList<(GrStaticMesh Mesh, int Level)>? BuiltMeshes(Gw2AttachedModel attached) =>
        _models.TryGetValue(attached.Key, out var entry) ? entry.Model?.Meshes : null;

    public void DetachModel(Gw2AttachedModel attached)
    {
        if (!_attachedModels.Remove(attached)) return;
        if (attached.LodState is { } placed) _pass.RemoveProps(new HashSet<GrPropLodState> { placed });
        ReleaseModel(attached.Key);
    }

    public void PlaceAttachedModels()
    {
        _replacedAttachedLodStates.Clear();
        _attachedModelsToPlace.Clear();
        foreach (var attached in _attachedModels)
        {
            if (attached.Offscreen || _models[attached.Key].Model is null) continue;
            if (attached.MeshPlacement is null && attached.LodState is not null && attached.PlacedWorld == attached.World) continue;
            if (attached.LodState is { } placed) _replacedAttachedLodStates.Add(placed);
            _attachedModelsToPlace.Add(attached);
        }
        _pass.RemoveProps(_replacedAttachedLodStates);
        foreach (var attached in _attachedModelsToPlace)
        {
            var model = _models[attached.Key].Model!;
            attached.PlacedWorld = attached.World;
            var lodState = new GrPropLodState();
            for (var index = 0; index < model.Meshes.Count; index++)
            {
                var (mesh, level) = model.Meshes[index];
                if (level != lodState.Level) continue;
                if (attached.MeshPlacement is not { } placement)
                {
                    _pass.AddProp(mesh, attached.World, mesh.MaterialConstants, Vector4.One, lodState, level, placedEachFrame: true);
                    continue;
                }
                if (placement(model.SourceMeshes[index], mesh.MaterialConstants) is { } placedMesh)
                    _pass.AddProp(mesh, placedMesh.World, placedMesh.Constants, Vector4.One, lodState, level, constantsChangeEachFrame: true, placedEachFrame: true);
            }
            attached.LodState = lodState;
        }
    }

    private bool Upload(TimeSpan budget)
    {
        var clock = Stopwatch.StartNew();
        var progressed = false;
        foreach (var attached in _attachedModels)
            if (_models[attached.Key].Model is null && clock.Elapsed < budget)
                Advance(attached.Key, clock, budget, ref progressed);
        for (var index = 0; index < _pendingProps.Count && clock.Elapsed < budget;)
        {
            var placement = _pendingProps[index];
            var state = Advance(KeyOf(placement), clock, budget, ref progressed);
            if (state != Gw2PropModelState.Ready)
            {
                if (state == Gw2PropModelState.Waiting) index++;
                continue;
            }
            _pendingProps.RemoveAt(index);
            _pendingPropSet.Remove(placement);
            var propStart = Stopwatch.GetTimestamp();
            LoadProp(placement, _models[KeyOf(placement)].Model!);
            RecordStep("prop", propStart);
            LastLoadedProps++;
            progressed = true;
        }
        for (var index = 0; index < _pendingBlocks.Count && clock.Elapsed < budget;)
        {
            var block = _pendingBlocks[index];
            var groups = _scatterGroupsByBlock[block];
            var state = Gw2PropModelState.Ready;
            foreach (var group in groups)
            {
                state = Advance(ScatterKeyOf(group), clock, budget, ref progressed);
                if (state != Gw2PropModelState.Ready) break;
            }
            if (state == Gw2PropModelState.Ready && !groups.TrueForAll(group => _materials.IsDecoded(GroundCoverMaterialFileIdOf(group)))) state = Gw2PropModelState.Waiting;
            if (state != Gw2PropModelState.Ready)
            {
                if (state == Gw2PropModelState.Waiting) index++;
                continue;
            }
            var blockStart = Stopwatch.GetTimestamp();
            var finished = ContinueScatterBuild(block, groups, clock, budget);
            RecordStep("block", blockStart);
            progressed = true;
            if (!finished) break;
            _pendingBlocks.RemoveAt(index);
            LastLoadedBlocks++;
        }
        for (var index = 0; index < _pendingPromotions.Count && clock.Elapsed < budget;)
        {
            if (!CompletePromotion(_pendingPromotions[index]))
            {
                index++;
                continue;
            }
            _pendingPromotions.RemoveAt(index);
            progressed = true;
        }
        return progressed;
    }

    private void UpdateResidency(Vector3 view)
    {
        _residency.BeginRound();
        foreach (var prop in _loadedProps.Values)
        {
            var distance = Vector3.Distance(view, prop.BoundsCenter) - prop.BoundsRadius;
            foreach (var fileId in prop.TextureFileIds) _residency.Report(fileId, distance);
        }
        _promotions.Clear();
        _demotions.Clear();
        var (backBufferWidth, backBufferHeight) = (_device.BackBufferWidth, _device.BackBufferHeight);
        _residency.Evaluate(GrTexResidency.PromoteDistance(_residency.Quality, backBufferWidth, backBufferHeight), _promotions, _demotions);
        foreach (var fileId in _demotions) _textures[fileId].Demote();
        foreach (var fileId in _promotions)
        {
            _textures[fileId].Promotion = RequestPromotion(fileId);
            _pendingPromotions.Add(fileId);
        }
    }

    private Gw2ContentHandle<Gw2PromotedTextureData> RequestPromotion(uint fileId)
    {
        var highResolutionFileId = _highResolutionFiles?.HighResolutionOf(fileId);
        var dropped = GrTexResidency.HighResolutionTopMipsDropped(_residency.Quality);
        return _content.Request(fileId, (reader, id) => reader.ReadTexture(id) is { } asset
            && (highResolutionFileId is { } highResolution
                ? GrTextureUpload.PrepareUpResolved(asset, dropped == 0 ? reader.ReadTexture(highResolution) : null, dropped)
                : GrTextureUpload.Prepare(asset, dropped)) is { } data
            ? new Gw2PromotedTextureData(data)
            : null);
    }

    private bool CompletePromotion(uint fileId)
    {
        var entry = _textures[fileId];
        if (entry.Promotion is not { IsCompleted: true } promotion) return false;
        entry.Promotion = null;
        _content.Release(promotion);
        var promoted = promotion.Result is { } data && entry.BaseTexture is { } baseTexture && data.Data.Width > baseTexture.Width
            && GrTextureUpload.Upload(_device, data.Data) is { } uploaded && entry.Promote(uploaded);
        if (promoted) LastUploadedTextures++;
        _residency.CompletePromotion(fileId, promoted);
        return true;
    }

    private Gw2PropModelState Advance(Gw2PropModelKey key, Stopwatch clock, TimeSpan budget, ref bool progressed)
    {
        var entry = _models[key];
        while (entry.Model is null)
        {
            if (clock.Elapsed >= budget) return Gw2PropModelState.OutOfBudget;
            var stepKind = StepKindOf(entry);
            var stepStart = Stopwatch.GetTimestamp();
            if (!Step(key, entry)) return Gw2PropModelState.Waiting;
            RecordStep(stepKind, stepStart);
            progressed = true;
        }
        return Gw2PropModelState.Ready;
    }

    private static string StepKindOf(Gw2PropModelEntry entry) =>
        entry.Build is not { } build ? "plan"
        : build.TexturesAcquired < build.TextureFileIds.Count ? "texture"
        : build.Built.Count < build.Planned.Count ? "mesh"
        : "finish";

    private void RecordStep(string kind, long startTimestamp)
    {
        var elapsed = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
        if (elapsed <= LastLongestStepMilliseconds) return;
        LastLongestStepMilliseconds = elapsed;
        LastLongestStepKind = kind;
    }

    private bool Step(Gw2PropModelKey key, Gw2PropModelEntry entry)
    {
        if (entry.Build is null)
        {
            if (!entry.Source.IsCompleted) return false;
            if (entry.Source.Result is not { } decoded)
            {
                Finish(entry, null);
                return true;
            }
            entry.Build = new Gw2PropModelBuild(decoded, PlanMeshes(decoded.Model), RenderableMeshes(decoded.Model)
                .SelectMany(mesh => TextureFileIds(decoded.Model.Materials[(int)mesh.MaterialIndex], key.Blits)).Select(texture => texture.FileId).Distinct().ToList(),
                RenderableMeshes(decoded.Model).Select(mesh => decoded.Model.Materials[(int)mesh.MaterialIndex].MaterialFileId ?? 0u).Distinct().ToList());
            foreach (var fileId in entry.Build.TextureFileIds) RequestTexture(fileId);
            foreach (var fileId in entry.Build.MaterialFileIds) _materials.Request(fileId);
            return true;
        }

        var build = entry.Build;
        if (build.TexturesAcquired < build.TextureFileIds.Count)
        {
            if (!AcquireTexture(build.TextureFileIds[build.TexturesAcquired])) return false;
            build.TexturesAcquired++;
            return true;
        }
        if (build.Built.Count < build.Planned.Count)
        {
            var (mesh, indices, level) = build.Planned[build.Built.Count];
            build.Built.Add((UploadMesh(build, mesh, indices, key.Blits), level));
            LastUploadedMeshes++;
            return true;
        }
        if (!build.MaterialFileIds.TrueForAll(_materials.IsDecoded)) return false;
        Finish(entry, build.Decoded.Source);
        return true;
    }

    private void Finish(Gw2PropModelEntry entry, Gw2.Dat.Content.Gw2Model? source)
    {
        entry.Model = new Gw2PropModel(source, entry.Build?.Built ?? [], entry.Build?.TextureFileIds ?? [])
        {
            SourceMeshes = entry.Build?.Planned.Take(entry.Build.Built.Count).Select(planned => planned.Mesh).ToList() ?? [],
            Materials = entry.Build?.Decoded.Model.Materials ?? [],
            Animation = entry.Build?.Decoded.Animation,
        };
        _content.Release(entry.Source);
    }

    private static List<(Gw2Mesh Mesh, int[]? Indices, int Level)> PlanMeshes(Gw2.Client.Model.Gw2Model model)
    {
        var planned = new List<(Gw2Mesh Mesh, int[]? Indices, int Level)>();
        var renderable = RenderableMeshes(model);
        var shellLevels = renderable.Select(mesh => mesh.LodLevel).Where(level => level > 0).Distinct().Order().ToList();
        for (var level = 0; level < LevelCount; level++)
        {
            if (level > 0 && shellLevels.Count > 0)
            {
                var shellLevel = shellLevels.LastOrDefault(candidate => candidate <= level, shellLevels[0]);
                planned.AddRange(renderable.Where(mesh => mesh.LodLevel == shellLevel).Select(mesh => (mesh, (int[]?)null, level)));
                continue;
            }
            planned.AddRange(renderable.Where(mesh => mesh.LodLevel == 0)
                .Select(mesh => (mesh, level == 0 || mesh.Lods.Length == 0 ? null : mesh.Lods[Math.Min(level, mesh.Lods.Length) - 1], level)));
        }
        return planned;
    }

    private void AcquireModel(Gw2PropModelKey key)
    {
        if (!_models.TryGetValue(key, out var entry))
        {
            var animationToken = key.AnimationToken;
            entry = new Gw2PropModelEntry(_content.Request(key.ModelFileId, (reader, fileId) =>
                reader.ReadModel(fileId) is { } source
                    ? new Gw2PropModelSource(source, Gw2ModelBuilder.Build(fileId, source, _ => null))
                    {
                        Animation = animationToken == 0 ? null : Gw2PropAnimationPlayback.For(reader.ReadStreamedModelAnimations(fileId), animationToken),
                    }
                    : null));
            _models[key] = entry;
        }
        entry.References++;
    }

    private void ReleaseModel(Gw2PropModelKey key)
    {
        var entry = _models[key];
        if (--entry.References > 0) return;
        _models.Remove(key);
        if (entry.Model is null) _content.Release(entry.Source);
        foreach (var (mesh, _) in entry.Model?.Meshes ?? entry.Build?.Built ?? []) mesh.Dispose();
        if (entry.Build is { } build)
            foreach (var fileId in build.TextureFileIds.Take(build.TexturesAcquired)) ReleaseTexture(fileId);
    }

    private Gw2PropTextureEntry RequestTexture(uint fileId)
    {
        if (_textures.TryGetValue(fileId, out var entry)) return entry;
        var dropped = GrTexResidency.BaseTopMipsDropped(_residency.Quality, _highResolutionFiles?.HighResolutionOf(fileId) is not null);
        entry = new Gw2PropTextureEntry(_content.Request(fileId, (reader, id) =>
            reader.ReadTexture(id) is { } asset && GrTextureUpload.Prepare(asset, dropped) is { } data ? new Gw2ResidentTextureData(data) : null));
        _textures[fileId] = entry;
        return entry;
    }

    private bool AcquireTexture(uint fileId)
    {
        var entry = RequestTexture(fileId);
        if (!entry.IsUploaded)
        {
            if (!entry.Data!.IsCompleted) return false;
            entry.Upload(entry.Data.Result is { } data ? GrTextureUpload.Upload(_device, data.Data) : null);
            if (entry.BaseTexture is not null) _residency.Track(fileId);
            LastUploadedTextures++;
            _content.Release(entry.Data);
            entry.Data = null;
        }
        entry.References++;
        return true;
    }

    private void ReleaseTexture(uint fileId)
    {
        var entry = _textures[fileId];
        if (--entry.References > 0) return;
        _textures.Remove(fileId);
        _residency.Untrack(fileId);
        _pendingPromotions.Remove(fileId);
        if (entry.Promotion is { } promotion) _content.Release(promotion);
        entry.Dispose();
    }

    private void LoadProp(Gw2MapPropPlacement placement, Gw2PropModel model)
    {
        var overrides = (placement.Constants ?? []).Where(constant => constant.Submodel == WholeModelSubmodel).ToList();
        var meshes = model.Meshes
            .Select(entry => new Gw2PropMesh(entry.Mesh, entry.Level, overrides.Count == 0 ? entry.Mesh.MaterialConstants : WithOverrides(entry.Mesh.MaterialConstants, overrides)))
            .ToList();
        var world = Gw2MapPropTransform.World(placement);
        var bounds = GrWorldBounds.Union(model.Meshes.Where(entry => entry.Level == 0).Select(entry => GrWorldBounds.Of(entry.Mesh.LocalMinimum, entry.Mesh.LocalMaximum, world)));
        var center = (bounds.Min + bounds.Max) * 0.5f;
        var prop = new Gw2PropInstance(placement, meshes, world, ModelColor(placement.Color), Gw2PropLodDistances.For(placement, model.Source), new GrPropLodState(),
            model.TextureFileIds, new Vector3(center.X, center.Y, center.Z), ((bounds.Max - bounds.Min) * 0.5f).Length());
        _loadedProps[placement] = prop;
        if (model.Animation is { } animation)
        {
            var surfaces = new List<Gw2PropAnimatedSurface>();
            var startSeconds = (float)_animationClock.Elapsed.TotalSeconds;
            var loops = (placement.Flags & Gw2MapPropFlags.NoLoopAnim) == 0;
            for (var index = 0; index < prop.Meshes.Count; index++)
            {
                var (mesh, level, constants) = prop.Meshes[index];
                var material = model.Materials[(int)model.SourceMeshes[index].MaterialIndex];
                var surface = new Gw2PropAnimatedSurface(animation, material, new Dictionary<uint, Vector4>(constants), startSeconds, loops);
                surfaces.Add(surface);
                _pass.AddProp(mesh, prop.World, surface.Constants, prop.ModelColor, prop.LodState, level, constantsChangeEachFrame: true, placedEachFrame: true);
            }
            _animatedSurfaces[placement] = surfaces;
            return;
        }
        foreach (var (mesh, level, constants) in prop.Meshes) _pass.AddProp(mesh, prop.World, constants, prop.ModelColor, prop.LodState, level);
    }

    public void AnimateProps()
    {
        if (_animatedSurfaces.Count == 0) return;
        var seconds = (float)_animationClock.Elapsed.TotalSeconds;
        foreach (var surfaces in _animatedSurfaces.Values)
            foreach (var surface in surfaces) surface.Playback.WriteSurface(surface.Material, surface.Constants, seconds - surface.StartSeconds, surface.Loops);
    }

    private bool ContinueScatterBuild((int X, int Y) block, List<ScatterGroup> groups, Stopwatch clock, TimeSpan budget)
    {
        if (!_scatterBuilds.TryGetValue(block, out var build))
        {
            if (!_scatterMaterialPreparations.TryGetValue(block, out var preparation))
                _scatterMaterialPreparations[block] = preparation = PrepareScatterMaterials(groups);
            if (!preparation.IsCompleted) return false;
            _scatterMaterialPreparations.Remove(block);
            build = LoadScatter(groups).GetEnumerator();
            _scatterBuilds[block] = build;
            _loadedScatter[block] = new List<GrStaticMeshInstances>();
        }
        var built = _loadedScatter[block];
        do
        {
            if (!build.MoveNext())
            {
                _scatterBuilds.Remove(block);
                build.Dispose();
                return true;
            }
            built.Add(build.Current);
        }
        while (clock.Elapsed < budget);
        return false;
    }

    private Task PrepareScatterMaterials(List<ScatterGroup> groups) =>
        Task.WhenAll(groups.SelectMany(group =>
        {
            var groundCoverMaterialFileId = GroundCoverMaterialFileIdOf(group);
            return _models[ScatterKeyOf(group)].Model!.Meshes
                .Where(entry => entry.Level == 0)
                .Select(entry => (MaterialFileId: groundCoverMaterialFileId != 0 ? groundCoverMaterialFileId : entry.Mesh.MaterialFileId, entry.Mesh.DrawFlags, entry.Mesh.SortBucketOverride));
        }).Distinct().Select(material => _pass.PrepareInstancedMaterialsAsync(material.MaterialFileId, material.DrawFlags, material.SortBucketOverride)));

    private IEnumerable<GrStaticMeshInstances> LoadScatter(IEnumerable<ScatterGroup> groups)
    {
        foreach (var group in groups)
        {
            var instances = group.ToList();
            var materialFileId = GroundCoverMaterialFileIdOf(group);
            var fade = new Gw2ZoneScatterFade(Gw2ZoneScatterFade.CenterOf(group.Key.Block, _origin), instances[0].FadeDistance, Gw2ZoneScatterFade.CutsHard(instances[0].LayerFlags));
            var model = _models[ScatterKeyOf(group)].Model!;
            foreach (var batch in instances.Chunk(InstancesPerDraw))
            {
                var worlds = batch.Select(instance => Gw2MapPropTransform.World(instance.Position, instance.Rotation, instance.Scale)).ToList();
                var colors = batch.Select(instance => instance.Color).ToList();
                foreach (var (mesh, level) in model.Meshes)
                {
                    if (level != 0) continue;
                    var meshInstances = new GrStaticMeshInstances(_device, mesh, worlds, colors, fade, materialFileId);
                    _pass.AddPropInstances(meshInstances);
                    yield return meshInstances;
                }
            }
        }
    }

    private uint GroundCoverMaterialFileIdOf(ScatterGroup group) => Gw2ZoneScatter.GroundCoverMaterialFileId(_map.Zones!, group.First());

    private static Gw2PropModelKey KeyOf(Gw2MapPropPlacement placement) => new(placement.ModelFileId, placement.BlitTextures.ToList(), placement.AnimSequence ?? 0);

    private static Gw2PropModelKey ScatterKeyOf(ScatterGroup group) => new(group.Key.ModelFileId, []);

    private static float DistanceSquared(Vector3 position, Vector2 view) =>
        Vector2.DistanceSquared(new Vector2(position.X, position.Y), view);

    private static float BlockDistanceSquared((int X, int Y) block, Vector2 viewInBlocks) =>
        Vector2.DistanceSquared(new Vector2(block.X + 0.5f, block.Y + 0.5f), viewInBlocks);

    private static Vector4 ModelColor(uint argb) =>
        new((byte)(argb >> 16) * ColorByteScale, (byte)(argb >> 8) * ColorByteScale, (byte)argb * ColorByteScale, (byte)(argb >> 24) * ColorByteScale);

    private static Dictionary<uint, Vector4> WithOverrides(IReadOnlyDictionary<uint, Vector4> materialConstants, IEnumerable<Gw2MapPropConstant> overrides)
    {
        var constants = new Dictionary<uint, Vector4>(materialConstants);
        foreach (var constant in overrides) constants[constant.Token] = ToVector(constant.Value);
        return constants;
    }

    private static Vector4 ToVector(IReadOnlyList<float> value) =>
        new(value.Count > 0 ? value[0] : 0f, value.Count > 1 ? value[1] : 0f, value.Count > 2 ? value[2] : 0f, value.Count > 3 ? value[3] : 0f);

    private static List<Gw2Mesh> RenderableMeshes(Gw2.Client.Model.Gw2Model model) =>
        model.Meshes.Where(mesh => mesh.IsRenderable && model.Materials.ElementAtOrDefault((int)mesh.MaterialIndex) is { MaterialFileId: not null }).ToList();

    private GrStaticMesh UploadMesh(Gw2PropModelBuild build, Gw2Mesh mesh, int[]? indices, IReadOnlyList<(ulong BlitId, uint TextureFileId)> blits)
    {
        var material = build.Decoded.Model.Materials[(int)mesh.MaterialIndex];
        var sharedVertices = build.SharedVertices.TryGetValue(mesh, out var existing) ? existing.Share() : build.SharedVertices[mesh] = new GrStaticMeshVertices(_device, mesh);
        return new GrStaticMesh(_device, sharedVertices, mesh, material.MaterialFileId!.Value, MaterialTextures(material, blits), MaterialConstants(material), indices) { TwoSided = material.TwoSided, DrawFlags = Gw2.Dat.Content.Gw2ModelMaterialDraw.DrawFlagsOf(material.MaterialFlags), SortBucketOverride = Gw2.Dat.Content.Gw2ModelMaterialDraw.SortBucketOverrideOf(material.SortOrder), TextureTransforms = GrTextureTransformAnimation.For(material.TextureTransforms) };
    }

    private static IEnumerable<(int Index, uint FileId)> TextureFileIds(Gw2Material material, IReadOnlyList<(ulong BlitId, uint TextureFileId)> blits)
    {
        foreach (var texture in material.Textures)
        {
            var blitFileId = blits.FirstOrDefault(blit => texture.BlitId != 0 && blit.BlitId == texture.BlitId).TextureFileId;
            if ((blitFileId != 0 ? blitFileId : texture.ContentFileId) is { } fileId && texture.MaterialTextureIndex >= 0)
                yield return (texture.MaterialTextureIndex, fileId);
        }
    }

    private GrTextureSlotList MaterialTextures(Gw2Material material, IReadOnlyList<(ulong BlitId, uint TextureFileId)> blits)
    {
        var slots = new GrTextureSlot?[material.Textures.Count == 0 ? 0 : material.Textures.Max(texture => texture.MaterialTextureIndex) + 1];
        foreach (var (index, fileId) in TextureFileIds(material, blits)) slots[index] = _textures[fileId].Slot;
        return new GrTextureSlotList(slots);
    }

    private static Dictionary<uint, Vector4> MaterialConstants(Gw2Material material) =>
        material.Constants.GroupBy(constant => constant.Token).ToDictionary(group => group.Key, group => ToVector(group.Last().Value));

    public void Dispose()
    {
        foreach (var build in _scatterBuilds.Values) build.Dispose();
        foreach (var instances in _loadedScatter.Values.SelectMany(block => block)) instances.Dispose();
        foreach (var entry in _models.Values)
        foreach (var (mesh, _) in entry.Model?.Meshes ?? entry.Build?.Built ?? []) mesh.Dispose();
        foreach (var entry in _textures.Values) entry.Dispose();
    }
}

public enum Gw2PropModelState
{
    Ready,
    Waiting,
    OutOfBudget,
}

public sealed class Gw2AttachedModel(Gw2PropModelKey key)
{
    public Gw2PropModelKey Key { get; } = key;

    public Matrix4x4 World { get; set; } = Matrix4x4.Identity;

    public bool Offscreen { get; set; }

    public GrPropLodState? LodState { get; set; }

    public Matrix4x4? PlacedWorld { get; set; }

    public Func<Gw2Mesh, IReadOnlyDictionary<uint, Vector4>, Gw2AttachedMeshPlacement?>? MeshPlacement { get; set; }
}

public sealed record Gw2PropModelKey(uint ModelFileId, IReadOnlyList<(ulong BlitId, uint TextureFileId)> Blits, ulong AnimationToken = 0)
{
    public bool Equals(Gw2PropModelKey? other) => other is not null && ModelFileId == other.ModelFileId && AnimationToken == other.AnimationToken && Blits.SequenceEqual(other.Blits);

    public override int GetHashCode() => Blits.Aggregate(HashCode.Combine(ModelFileId, AnimationToken), HashCode.Combine);
}

public sealed class Gw2PropModelEntry(Gw2ContentHandle<Gw2PropModelSource> source)
{
    public Gw2ContentHandle<Gw2PropModelSource> Source { get; } = source;

    public Gw2PropModelBuild? Build { get; set; }

    public Gw2PropModel? Model { get; set; }

    public int References { get; set; }
}

public sealed class Gw2PropModelBuild(Gw2PropModelSource decoded, List<(Gw2Mesh Mesh, int[]? Indices, int Level)> planned, List<uint> textureFileIds, List<uint> materialFileIds)
{
    public List<uint> MaterialFileIds { get; } = materialFileIds;

    public Gw2PropModelSource Decoded { get; } = decoded;

    public List<(Gw2Mesh Mesh, int[]? Indices, int Level)> Planned { get; } = planned;

    public List<uint> TextureFileIds { get; } = textureFileIds;

    public int TexturesAcquired { get; set; }

    public List<(GrStaticMesh Mesh, int Level)> Built { get; } = new();

    public Dictionary<Gw2Mesh, GrStaticMeshVertices> SharedVertices { get; } = new(ReferenceEqualityComparer.Instance);
}

public sealed class Gw2PropTextureEntry(Gw2ContentHandle<Gw2ResidentTextureData> data) : IDisposable
{
    public Gw2ContentHandle<Gw2ResidentTextureData>? Data { get; set; } = data;

    public Gw2ContentHandle<Gw2PromotedTextureData>? Promotion { get; set; }

    public GrTextureSlot Slot { get; } = new();

    public Texture2D? BaseTexture { get; private set; }

    public Texture2D? HighResolutionTexture { get; private set; }

    public bool IsUploaded => Data is null;

    public int References { get; set; }

    public void Upload(Texture2D? baseTexture)
    {
        BaseTexture = baseTexture;
        Slot.Texture = baseTexture;
    }

    public bool Promote(Texture2D highResolutionTexture)
    {
        HighResolutionTexture = highResolutionTexture;
        Slot.Texture = highResolutionTexture;
        return true;
    }

    public void Demote()
    {
        Slot.Texture = BaseTexture;
        HighResolutionTexture?.Dispose();
        HighResolutionTexture = null;
    }

    public void Dispose()
    {
        Slot.Texture = null;
        BaseTexture?.Dispose();
        HighResolutionTexture?.Dispose();
    }
}

public sealed class Gw2TextureEntry(Gw2ContentHandle<GrTextureData> data)
{
    public Gw2ContentHandle<GrTextureData>? Data { get; set; } = data;

    public Texture2D? Texture { get; set; }

    public bool IsUploaded => Data is null;

    public int References { get; set; }
}

public sealed record Gw2ResidentTextureData(GrTextureData Data);

public sealed record Gw2PromotedTextureData(GrTextureData Data);

public sealed record Gw2PropModelSource(Gw2.Dat.Content.Gw2Model Source, Gw2.Client.Model.Gw2Model Model)
{
    public Gw2PropAnimationPlayback? Animation { get; init; }
}

public sealed record Gw2PropAnimatedSurface(Gw2PropAnimationPlayback Playback, Gw2Material Material, Dictionary<uint, Vector4> Constants, float StartSeconds, bool Loops);

public sealed record Gw2PropModel(Gw2.Dat.Content.Gw2Model? Source, IReadOnlyList<(GrStaticMesh Mesh, int Level)> Meshes, IReadOnlyList<uint> TextureFileIds)
{
    public IReadOnlyList<Gw2Mesh> SourceMeshes { get; init; } = [];

    public IReadOnlyList<Gw2Material> Materials { get; init; } = [];

    public Gw2PropAnimationPlayback? Animation { get; init; }
}

public readonly record struct Gw2AttachedMeshPlacement(Matrix4x4 World, IReadOnlyDictionary<uint, Vector4> Constants);

public sealed record Gw2PropMesh(GrStaticMesh Mesh, int Level, IReadOnlyDictionary<uint, Vector4> Constants);

public sealed record Gw2PropInstance(Gw2MapPropPlacement Placement, IReadOnlyList<Gw2PropMesh> Meshes, Matrix4x4 World, Vector4 ModelColor, Gw2PropLodDistances LodDistances, GrPropLodState LodState,
    IReadOnlyList<uint> TextureFileIds, Vector3 BoundsCenter, float BoundsRadius)
{
    public void UpdateLevel(Vector3 camera, float distanceFactor) =>
        LodState.Level = Gw2PropLod.SelectForPlacement(Placement.Flags, LodDistances, Placement.Scale, distanceFactor, camera, Placement.Position, LodState.Level);
}
