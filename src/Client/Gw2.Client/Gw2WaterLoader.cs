using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Gw2.Client.Scene;
using Gw2.Dat.Content;
using Gw2.Client.Graphics;
using Crystal;
using Crystal.Graphics;

namespace Gw2.Client;

public sealed class Gw2WaterLoader : IDisposable
{
    private Gw2MapEnvWaterInfo _aboveWater;
    private readonly Gw2MapEnvWaterInfo _belowWater;
    private readonly Gw2WaterConstants _constants;
    private readonly Gw2WaterWaves _waves;
    private readonly GrDevice _device;
    private readonly Gw2ContentReader _reader;
    private readonly Dictionary<uint, Texture2D?> _textures;
    private readonly GrTextureSlot[] _aboveTextureSlots;
    private Gw2GlobalWaterGrid? _globalGrid;
    private Vector2 _viewDirection;

    private Gw2WaterLoader(GrWater water, Gw2MapEnvWaterInfo aboveWater, Gw2MapEnvWaterInfo belowWater, float tileSize, int gridSize, bool lowQuality, GrDevice device, Gw2ContentReader reader, Dictionary<uint, Texture2D?> textures, GrTextureSlot[] aboveTextureSlots)
    {
        _device = device;
        _reader = reader;
        _textures = textures;
        _aboveTextureSlots = aboveTextureSlots;
        _waves = new Gw2WaterWaves(gridSize, lowQuality);
        Water = water;
        _aboveWater = aboveWater;
        _belowWater = belowWater;
        _constants = new Gw2WaterConstants(tileSize);
        Advance(0f, Vector3.Zero, Vector2.Zero);
    }

    public GrWater Water { get; }

    public float SurfaceZ { get; private set; } = float.PositiveInfinity;

    public float UnderwaterDistortAmount => _belowWater.DistortAmount;

    public static Gw2WaterLoader? Load(GrDevice device, Gw2ContentReader reader, Gw2MapBundle map, float farClip, float dayWeight, Vector3 position, bool lowQuality)
    {
        if (map.Water is not { } water || map.Terrain is not { } terrain || map.Info.Bounds.Count < 4) return null;
        if (map.Environment is not { } environment || Gw2EnvironmentWaterBlend.FromZones(environment, Gw2EnvironmentZoneWeights.At(environment, position), dayWeight) is not { } aboveWater) return null;
        var belowWater = environment.UnderwaterWater ?? aboveWater;
        if (Gw2TerrainGround.From(terrain, new Vector2(map.Info.Bounds[0], map.Info.Bounds[1])) is not { } ground) return null;
        var bodies = Gw2WaterBodies.From(water);
        if (bodies.Count == 0) return null;
        var tileSize = terrain.WorldUnitsPerHeightSample;
        var gridSize = Gw2WaterWaves.GridSizeForTilesPerChunk(terrain.TilesPerChunk);
        var textures = new Dictionary<uint, Texture2D?>();
        if (Material(device, reader, aboveWater, Gw2BuiltInWaterMaterial.Above, textures) is not { } loadedAboveMaterial || Material(device, reader, belowWater, Gw2BuiltInWaterMaterial.Below, textures) is not { } belowMaterial) return null;
        var aboveTextureSlots = loadedAboveMaterial.Textures.Select(texture => new GrTextureSlot { Texture = (Texture2D?)texture }).ToArray();
        var aboveMaterial = new GrWaterMaterial(loadedAboveMaterial.MaterialFileId, new GrTextureSlotList(aboveTextureSlots)) { BuiltInMaterial = loadedAboveMaterial.BuiltInMaterial };
        var innerHalfExtent = Gw2WaterBodies.GlobalInnerHalfExtent(gridSize, tileSize);
        var grWater = new GrWater(
            device,
            2f * innerHalfExtent / (gridSize - 1),
            aboveMaterial,
            belowMaterial);
        foreach (var texture in textures.Values) if (texture is not null) grWater.Own(texture);
        var inverseShoreDepth = aboveWater.DepthAttenuation == 0f ? 1f : 1f / aboveWater.DepthAttenuation;
        var terrainCut = new Gw2WaterTerrainCut((water.Flags & Gw2WaterTerrainCut.MapWaterTerrainCutFlag) != 0, Gw2WaterTerrainCut.DeepCutoffOf(aboveWater.AnimAmplitude, aboveWater.AnimWind), ground.ChunkFlagsAt);
        var mapMinimum = new Vector2(map.Info.Bounds[0], map.Info.Bounds[1]);
        var mapMaximum = new Vector2(map.Info.Bounds[2], map.Info.Bounds[3]);
        var loader = new Gw2WaterLoader(grWater, aboveWater, belowWater, tileSize, gridSize, lowQuality, device, reader, textures, aboveTextureSlots);
        foreach (var body in bodies)
        {
            var globalCells = body.IsGlobal ? Gw2GlobalWaterCells.From(mapMinimum, mapMaximum, body.SurfaceZ, tileSize, ground.HeightAt, terrainCut) : null;
            var globalGrid = body.IsGlobal ? new Gw2GlobalWaterGrid(loader._waves, body.SurfaceZ, tileSize, farClip, position => DepthWeight(position, body.SurfaceZ, ground.HeightAt, inverseShoreDepth, terrainCut)) : null;
            loader._globalGrid ??= globalGrid;
            if (body.IsGlobal) loader.SurfaceZ = body.SurfaceZ;
            var mesh = body.IsGlobal
                ? Gw2WaterBodies.GlobalPlane(gridSize, body.SurfaceZ, innerHalfExtent, farClip)
                : Gw2WaterBodies.Surface(body, ground.HeightAt, tileSize, aboveWater.DepthAttenuation);
            grWater.Add(new GrWaterSurface(
                body.IsGlobal,
                body.IsBelowWater,
                body.VisibilityBox,
                mesh.Vertices.Select(vertex => vertex.Position).ToArray(),
                mesh.Vertices.Select(vertex => vertex.Alpha).ToArray(),
                mesh.Indices,
                body.IsGlobal ? (local, world) => Gw2WaterBodies.GlobalAlpha(local, world, innerHalfExtent, ground.HeightAt, inverseShoreDepth, terrainCut) : null,
                globalCells is { } cells ? (eye, reach) => cells.ShownBoxes(eye, reach) : null,
                globalGrid is { } grid ? (eye, positions, normals, alphas) => grid.Fill(eye, loader._viewDirection, positions, normals, alphas) : null));
        }
        return loader;
    }

    public void Reblend(Gw2MapEnvironment environment, IReadOnlyList<Gw2EnvironmentZoneWeight> zoneWeights, float dayWeight)
    {
        if (Gw2EnvironmentWaterBlend.FromZones(environment, zoneWeights, dayWeight) is not { } aboveWater) return;
        if (aboveWater.MaterialFile.TryGetFileId(out var materialFileId) && materialFileId == Water.Above.MaterialFileId && !aboveWater.TextureFiles.SequenceEqual(_aboveWater.TextureFiles))
            for (var slot = 0; slot < _aboveTextureSlots.Length && slot < aboveWater.TextureFiles.Count; slot++)
                _aboveTextureSlots[slot].Texture = aboveWater.TextureFiles[slot] is { } reference && reference.TryGetFileId(out var textureFileId)
                    ? OwnedTexture(textureFileId)
                    : EngineSlotStandIn(_device, _textures);
        _aboveWater = aboveWater;
    }

    public void Advance(float elapsedSeconds, Vector3 eye, Vector2 viewDirection)
    {
        _viewDirection = viewDirection;
        var eyeHeightAboveSurface = _globalGrid?.EyeHeightAboveSurface(eye) ?? 0f;
        _waves.Advance(elapsedSeconds, _aboveWater.AnimAmplitude, _aboveWater.AnimWind, _aboveWater.AnimChoppiness, eyeHeightAboveSurface);
        _constants.Advance(elapsedSeconds, _aboveWater);
        Water.Above.Constants = _constants.Pack(_aboveWater);
        Water.Below.Constants = _constants.Pack(_belowWater);
    }

    private static float DepthWeight(Vector2 position, float surfaceZ, Func<Vector2, float> groundHeight, float inverseShoreDepth, Gw2WaterTerrainCut terrainCut) =>
        terrainCut.Depth(groundHeight(position) - surfaceZ, surfaceZ, terrainCut.ChunkFlags(position)) * inverseShoreDepth;

    private static GrWaterMaterial? Material(GrDevice device, Gw2ContentReader reader, Gw2MapEnvWaterInfo water, Gw2BuiltInWaterMaterial builtInMaterial, Dictionary<uint, Texture2D?> textures)
    {
        if (water.MaterialFile.TryGetFileId(out var materialFileId))
            return new(materialFileId, water.TextureFiles.Select(file => file is { } reference && reference.TryGetFileId(out var textureFileId) ? Texture(device, reader, textureFileId, textures) : EngineSlotStandIn(device, textures)).ToArray());
        return new(0, [EngineSlotStandIn(device, textures), Solid(device, textures, BlackKey, Color.Transparent), Solid(device, textures, WhiteKey, Color.White)]) { BuiltInMaterial = builtInMaterial };
    }

    private const uint EngineSlotStandInKey = 0;
    private const uint BlackKey = 1;
    private const uint WhiteKey = 2;

    private static Texture2D EngineSlotStandIn(GrDevice device, Dictionary<uint, Texture2D?> textures) => Solid(device, textures, EngineSlotStandInKey, Color.Transparent);

    private static Texture2D Solid(GrDevice device, Dictionary<uint, Texture2D?> textures, uint key, Color color)
    {
        if (textures.GetValueOrDefault(key) is { } existing) return existing;
        var solid = new Texture2D(device.GraphicsDevice, new TextureDescription { Width = 1, Height = 1, Name = "WaterSolid" });
        solid.SetData<Color>([color]);
        textures[key] = solid;
        return solid;
    }

    private static Texture2D? Texture(GrDevice device, Gw2ContentReader reader, uint fileId, Dictionary<uint, Texture2D?> textures)
    {
        if (textures.TryGetValue(fileId, out var cached)) return cached;
        var uploaded = reader.ReadTexture(fileId) is { } asset ? GrTextureUpload.FromAsset(device.GraphicsDevice, asset) : null;
        textures[fileId] = uploaded;
        return uploaded;
    }

    private Texture2D? OwnedTexture(uint fileId)
    {
        if (_textures.TryGetValue(fileId, out var cached)) return cached;
        var uploaded = Texture(_device, _reader, fileId, _textures);
        if (uploaded is not null) Water.Own(uploaded);
        return uploaded;
    }

    public void Dispose() => Water.Dispose();
}
