using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Gw2.Client.Content;
using Gw2.Client.Scene;
using Gw2.Dat.Content;
using Gw2.Dat.Archive;
using Gw2.Client.Graphics;
using Crystal;
using Crystal.Graphics;

namespace Gw2.Client;

public sealed class Gw2TerrainLoader : IDisposable
{
    private const uint AtlasTextureFlag = 4;
    private static readonly TimeSpan UploadBudget = TimeSpan.FromMilliseconds(3);
    private static readonly TimeSpan FirstFillUploadBudget = TimeSpan.FromMilliseconds(25);
    private bool _hasSettledOnce;

    private readonly GraphicsDevice _device;
    private readonly Gw2ContentService _content;
    private readonly GrCharacterPass _pass;
    private readonly Gw2ArchiveMaterials _materials;
    private readonly Gw2MapBundle _map;
    private readonly Gw2MapTerrain _terrain;
    private readonly Gw2TerrainActivation _activation;
    private readonly Dictionary<(int Column, int Row), GrTerrainChunk> _resident = new();
    private readonly Dictionary<(int Column, int Row), Gw2TerrainChunkBuild> _building = new();
    private readonly List<(int Column, int Row)> _uploadOrder = new();
    private readonly List<(int Column, int Row)> _released = new();
    private readonly List<(int Column, int Row)> _activated = new();
    private readonly Dictionary<uint, Gw2TextureEntry> _texturesByFileId = new();
    private readonly Dictionary<(uint Layer, int Column, int Row), Gw2TerrainTextureSlot> _atlasPages = new();

    private Gw2TerrainLoader(GraphicsDevice device, Gw2ContentService content, Gw2ArchiveMaterials materials, GrCharacterPass pass, Gw2MapBundle map, Gw2MapTerrain terrain)
    {
        _materials = materials;
        _device = device;
        _content = content;
        _pass = pass;
        _map = map;
        _terrain = terrain;
        _activation = new Gw2TerrainActivation(terrain, map.Info.Bounds);
    }

    public static Gw2TerrainLoader? For(GraphicsDevice device, Gw2ContentService content, Gw2ArchiveMaterials materials, GrCharacterPass pass, Gw2MapBundle map) =>
        map.Terrain is { } terrain ? new Gw2TerrainLoader(device, content, materials, pass, map, terrain) : null;

    public static int ResidencySpan(Gw2MapTerrain terrain) => Gw2TerrainActivation.SpanOf(terrain);

    public int ResidentChunkCount => _resident.Count;

    public bool IsSettled => _building.Count == 0;

    public int LastUploadedChunks { get; private set; }

    public int LastUploadedTextures { get; private set; }

    public void Update(Vector3 view)
    {
        Activate(view);
        Upload(_hasSettledOnce ? UploadBudget : FirstFillUploadBudget);
        _hasSettledOnce |= IsSettled;
    }

    public void LoadAll(Vector3 view)
    {
        Activate(view);
        while (!IsSettled)
            if (!Upload(TimeSpan.MaxValue)) Thread.Sleep(1);
    }

    private void Activate(Vector3 view)
    {
        _released.Clear();
        _activated.Clear();
        if (!_activation.Update(view, _released, _activated)) return;
        foreach (var chunk in _released) Release(chunk);
        foreach (var chunk in _activated) Activate(chunk, view);
    }

    private void Activate((int Column, int Row) chunk, Vector3 view)
    {
        var material = MaterialOf(chunk);
        if (!material.TryGetFileId(out var materialFileId)) return;
        var bounds = _map.Info.Bounds;
        var slots = material.TextureIndices.Select(index => SlotOf((int)index, chunk.Column, chunk.Row)).ToArray();
        foreach (var slot in slots)
            if (slot.FileId is { } fileId) RequestTexture(fileId);
        _materials.Request(materialFileId);
        _building[chunk] = new Gw2TerrainChunkBuild(
            Task.Run(() => Gw2MapTerrainGeometry.BuildChunk(_terrain, bounds, chunk.Column, chunk.Row, view)), materialFileId, slots);
    }

    private void Release((int Column, int Row) chunk)
    {
        _building.Remove(chunk);
        if (!_resident.Remove(chunk, out var resident)) return;
        _pass.RemoveTerrainChunk(resident);
    }

    private bool Upload(TimeSpan budget)
    {
        LastUploadedChunks = 0;
        LastUploadedTextures = 0;
        if (_building.Count == 0) return false;
        var clock = Stopwatch.StartNew();
        var progressed = false;
        _uploadOrder.Clear();
        _uploadOrder.AddRange(_building.Keys);
        if (_activation.Center is { } center)
            _uploadOrder.Sort((left, right) => ChunkDistanceSquared(left, center).CompareTo(ChunkDistanceSquared(right, center)));
        foreach (var chunk in _uploadOrder)
        {
            if (clock.Elapsed >= budget) break;
            var build = _building[chunk];
            if (!build.Geometry.IsCompleted) continue;
            if (!build.Geometry.IsCompletedSuccessfully)
            {
                _building.Remove(chunk);
                progressed = true;
                continue;
            }
            if (!_materials.IsDecoded(build.MaterialFileId) || !UploadTextures(build, clock, budget, ref progressed)) continue;
            if (clock.Elapsed >= budget) break;
            _building.Remove(chunk);
            var uploaded = new GrTerrainChunk(_device, build.Geometry.Result, build.MaterialFileId, build.Slots.Select(TextureOf).ToArray());
            _resident[chunk] = uploaded;
            _pass.AddTerrainChunk(uploaded);
            LastUploadedChunks++;
            progressed = true;
        }
        return progressed;
    }

    private bool UploadTextures(Gw2TerrainChunkBuild build, Stopwatch clock, TimeSpan budget, ref bool progressed)
    {
        foreach (var slot in build.Slots)
        {
            if (slot.FileId is not { } fileId) continue;
            var entry = _texturesByFileId[fileId];
            if (entry.IsUploaded) continue;
            if (clock.Elapsed >= budget || !entry.Data!.IsCompleted) return false;
            entry.Texture = entry.Data.Result is { } data ? GrTextureUpload.Upload(_device, data) : null;
            _content.Release(entry.Data);
            entry.Data = null;
            LastUploadedTextures++;
            progressed = true;
        }
        return true;
    }

    private Gw2MapTerrainMaterial MaterialOf((int Column, int Row) chunk) =>
        _terrain.ChunkMaterials[chunk.Row * Gw2MapTerrainGeometry.ChunkColumns(_terrain) + chunk.Column].High;

    private Texture? TextureOf(Gw2TerrainTextureSlot slot) =>
        slot.FileId is { } fileId ? _texturesByFileId[fileId].Texture : slot.Solid;

    private Gw2TerrainTextureSlot SlotOf(int textureIndex, int column, int row)
    {
        if (textureIndex >= _terrain.Textures.Count) return Gw2TerrainTextureSlot.Empty;
        var texture = _terrain.Textures[textureIndex];
        if (texture.Flags == AtlasTextureFlag)
            return _map.Atlas is not { } atlas ? Gw2TerrainTextureSlot.Empty : AtlasPage(atlas, texture.Layer, column / Gw2MapTerrainGeometry.AtlasQuadrantsPerPage, row / Gw2MapTerrainGeometry.AtlasQuadrantsPerPage);
        return texture.TryGetFileId(out var fileId) ? new Gw2TerrainTextureSlot(fileId, null) : Gw2TerrainTextureSlot.Empty;
    }

    private Gw2TerrainTextureSlot AtlasPage(Gw2PagedImage atlas, uint layer, int column, int row)
    {
        if (_atlasPages.TryGetValue((layer, column, row), out var cached)) return cached;
        var page = atlas.StrippedPages.FirstOrDefault(candidate => candidate.Layer == layer && candidate.Coord[0] == column && candidate.Coord[1] == row);
        var slot = page is null ? Gw2TerrainTextureSlot.Empty
            : page.TryGetFileId(out var fileId) ? new Gw2TerrainTextureSlot(fileId, null)
            : new Gw2TerrainTextureSlot(null, SolidPage(page.SolidColor));
        _atlasPages[(layer, column, row)] = slot;
        return slot;
    }

    private Texture2D SolidPage(uint argb)
    {
        var texture = new Texture2D(_device, new TextureDescription { Width = 1, Height = 1, Name = "TerrainSolidPage" });
        texture.SetData<Color>([new Color((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, (byte)(argb >> 24))]);
        return texture;
    }

    private void RequestTexture(uint fileId)
    {
        if (_texturesByFileId.ContainsKey(fileId)) return;
        _texturesByFileId[fileId] = new Gw2TextureEntry(_content.Request(fileId, static (reader, id) => reader.ReadTexture(id) is { } asset ? GrTextureUpload.Prepare(asset) : null));
    }

    private static int ChunkDistanceSquared((int Column, int Row) chunk, (int Column, int Row) center)
    {
        var column = chunk.Column - center.Column;
        var row = chunk.Row - center.Row;
        return column * column + row * row;
    }

    public void Dispose()
    {
        foreach (var entry in _texturesByFileId.Values)
        {
            entry.Texture?.Dispose();
            if (entry.Data is { } data) _content.Release(data);
        }
        foreach (var slot in _atlasPages.Values) slot.Solid?.Dispose();
    }
}

public readonly record struct Gw2TerrainTextureSlot(uint? FileId, Texture2D? Solid)
{
    public static Gw2TerrainTextureSlot Empty => default;
}

public sealed record Gw2TerrainChunkBuild(Task<Gw2MapTerrainChunkGeometry> Geometry, uint MaterialFileId, IReadOnlyList<Gw2TerrainTextureSlot> Slots);
