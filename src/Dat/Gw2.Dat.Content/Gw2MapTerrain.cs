using Gw2.Formats;
namespace Gw2.Dat.Content;

public sealed record Gw2MapTerrainChunk(uint Flags, IReadOnlyList<ushort> SurfaceIndices, IReadOnlyList<ulong> SurfaceTokens);

public sealed record Gw2MapTerrainTexture(uint TokenName, uint Flags, uint Layer, PackFileReference Reference)
{
    public bool TryGetFileId(out uint fileId) => Reference.TryGetFileId(out fileId);
}

public sealed record Gw2MapTerrainMaterial(
    uint Fvf,
    PackFileReference MaterialFile,
    IReadOnlyList<uint> ConstantIndices,
    IReadOnlyList<uint> TextureIndices)
{
    public bool TryGetFileId(out uint fileId) => MaterialFile.TryGetFileId(out fileId);
}

public sealed record Gw2MapTerrainConstant(uint TokenName, IReadOnlyList<float> Value);

public sealed record Gw2MapTerrainUvData(
    IReadOnlyList<float> Translation,
    IReadOnlyList<float> XScaleRange,
    IReadOnlyList<float> YScaleRange,
    IReadOnlyList<float> ScaleSpeed,
    float Rotation)
{
    public float[] CreateRotationQuaternion()
    {
        var half = Rotation * 0.5f;
        return [0f, 0f, -MathF.Sin(half), MathF.Cos(half)];
    }
}

public sealed record Gw2MapTerrainChunkMaterial(
    IReadOnlyList<byte> Tiling,
    Gw2MapTerrainMaterial High,
    Gw2MapTerrainMaterial? Low,
    Gw2MapTerrainMaterial? Fader,
    Gw2MapTerrainUvData? UvData);

public sealed class Gw2MapTerrain
{
    public Gw2MapTerrain(
        IReadOnlyList<uint> dims,
        float swapDistance,
        IReadOnlyList<float> heights,
        IReadOnlyList<uint> tileFlags,
        IReadOnlyList<Gw2MapTerrainChunk> chunks,
        PackFileReference pagedImage,
        IReadOnlyList<Gw2MapTerrainTexture> textures,
        IReadOnlyList<Gw2MapTerrainConstant> constants,
        IReadOnlyList<Gw2MapTerrainChunkMaterial> chunkMaterials)
    {
        Dims = dims;
        SwapDistance = swapDistance;
        Heights = heights;
        TileFlags = tileFlags;
        Chunks = chunks;
        PagedImage = pagedImage;
        Textures = textures;
        Constants = constants;
        ChunkMaterials = chunkMaterials;
        Materials = chunkMaterials.Select(material => material.High).ToArray();
    }

    public IReadOnlyList<uint> Dims { get; }
    public float SwapDistance { get; }
    public IReadOnlyList<float> Heights { get; }
    public IReadOnlyList<uint> TileFlags { get; }
    public IReadOnlyList<Gw2MapTerrainChunk> Chunks { get; }
    public PackFileReference PagedImage { get; }
    public IReadOnlyList<Gw2MapTerrainTexture> Textures { get; }
    public IReadOnlyList<Gw2MapTerrainConstant> Constants { get; }
    public IReadOnlyList<Gw2MapTerrainChunkMaterial> ChunkMaterials { get; }
    public IReadOnlyList<Gw2MapTerrainMaterial> Materials { get; }
    public uint? VerticesPerChunkSide { get; init; }
    public int TilesPerChunk => (int)(VerticesPerChunkSide ?? DefaultTilesPerChunk);
    public int HeightBlockSize => TilesPerChunk + HeightBlockOverlap;
    public float WorldUnitsPerHeightSample => ChunkWorldSize / TilesPerChunk;
    public int TileFlagWordsPerChunk => TilesPerChunk * TilesPerChunk / TilesPerTileFlagWord;
    public IReadOnlyList<float> MidFade { get; init; } = [];
    public IReadOnlyList<float> FarFade { get; init; } = [];

    public static Gw2MapTerrain? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.FindRoot("PackMapTerrain");
        if (root is null) return null;

        var materialRoot = root.Fields.GetValueOrDefault("materials") as PackObject;

        return new Gw2MapTerrain(
            (root.Fields.GetValueOrDefault("dims") as uint[])?.ToArray() ?? [],
            root.Fields.GetValueOrDefault("swapDistance") is float swap ? swap : 0,
            Floats(root.Fields.GetValueOrDefault("heightMapArray")),
            UIntList(root.Fields.GetValueOrDefault("tileFlagArray")),
            ReadChunks(root.Fields.GetValueOrDefault("chunkArray")),
            Reference(materialRoot, "pagedImage"),
            ReadTextures(materialRoot),
            ReadConstants(materialRoot),
            ReadChunkMaterials(materialRoot))
        {
            VerticesPerChunkSide = PackValue.OptionalU32(root, "verticesPerChunkSide"),
            MidFade = materialRoot?.Optional("midFade") is { } midFade ? Floats(midFade) : [],
            FarFade = materialRoot?.Optional("farFade") is { } farFade ? Floats(farFade) : [],
        };
    }

    public IEnumerable<uint> EnumerateDependencies()
    {
        var seen = new HashSet<uint>();
        if (PagedImage.TryGetFileId(out var paged) && seen.Add(paged)) yield return paged;

        foreach (var texture in Textures)
            if (texture.TryGetFileId(out var textureId) && seen.Add(textureId))
                yield return textureId;

        foreach (var material in Materials)
            if (material.TryGetFileId(out var materialId) && seen.Add(materialId))
                yield return materialId;
    }

    public bool TryBuildHeightGrid(out int width, out int height, out float[] grid)
    {
        width = 0;
        height = 0;
        grid = [];
        if (Dims.Count < 2) return false;

        var chunksX = (int)(Dims[0] / TilesPerChunk);
        var chunksY = (int)(Dims[1] / TilesPerChunk);
        if (chunksX <= 0 || chunksY <= 0) return false;
        if ((long)chunksX * chunksY * HeightBlockSize * HeightBlockSize != Heights.Count) return false;

        var stride = HeightBlockSize - HeightBlockOverlap;
        width = (chunksX - 1) * stride + HeightBlockSize;
        height = (chunksY - 1) * stride + HeightBlockSize;
        grid = new float[width * height];

        for (var by = 0; by < chunksY; by++)
            for (var bx = 0; bx < chunksX; bx++)
            {
                var blockOffset = (by * chunksX + bx) * HeightBlockSize * HeightBlockSize;
                for (var y = 0; y < HeightBlockSize; y++)
                {
                    var row = height - 1 - (by * stride + y);
                    var destination = row * width + bx * stride;
                    for (var x = 0; x < HeightBlockSize; x++)
                        grid[destination + x] = Heights[blockOffset + y * HeightBlockSize + x];
                }
            }

        return true;
    }

    public const int HeightBlockOverlap = 3;
    public const int HeightBlockBorder = 1;
    public const int DefaultTilesPerChunk = 32;
    public const int TilesPerTileFlagWord = 16;

    public const float ChunkWorldSize = 3072f;
    public const float DefaultViewSpan = 24576f;
    public const int MinimumViewChunks = 3;

    public float ViewDistance => ViewDistanceOf(TilesPerChunk, SwapDistance);

    public static float DefaultViewDistance(int tilesPerChunk)
    {
        var chunkSize = tilesPerChunk * ((int)ChunkWorldSize / tilesPerChunk);
        return chunkSize * MathF.Round(DefaultViewSpan / chunkSize);
    }

    public static float ViewDistanceOf(int tilesPerChunk, float swapDistance)
    {
        var chunkSize = tilesPerChunk * ((int)ChunkWorldSize / tilesPerChunk);
        return chunkSize * Math.Max(MinimumViewChunks, MathF.Round(swapDistance / chunkSize));
    }

    public bool TryBuildTileSurfaceIndex(out int tileWidth, out int tileHeight, out byte[] indices, out IReadOnlyList<uint> palette)
    {
        tileWidth = 0;
        tileHeight = 0;
        indices = [];
        palette = [];
        if (Dims.Count < 2) return false;

        tileWidth = (int)Dims[0];
        tileHeight = (int)Dims[1];
        var chunksX = tileWidth / TilesPerChunk;
        var chunksY = tileHeight / TilesPerChunk;
        if (chunksX <= 0 || chunksY <= 0) return false;
        if (Materials.Count < chunksX * chunksY || TileFlags.Count < chunksX * chunksY * TileFlagWordsPerChunk) return false;

        var paletteList = new List<uint>();
        var paletteLookup = new Dictionary<uint, int>();
        indices = new byte[tileWidth * tileHeight];

        for (var cy = 0; cy < chunksY; cy++)
            for (var cx = 0; cx < chunksX; cx++)
            {
                var chunk = cy * chunksX + cx;
                var material = Materials[chunk];
                var surfaceIds = new uint[4];
                for (var surface = 0; surface < 4; surface++)
                {
                    var slot = material.TextureIndices.Count > surface ? material.TextureIndices[surface] : 0;
                    surfaceIds[surface] = slot < Textures.Count && Textures[(int)slot].TryGetFileId(out var fileId) ? fileId : 0;
                }

                var flagBase = chunk * TileFlagWordsPerChunk;
                for (var ly = 0; ly < TilesPerChunk; ly++)
                    for (var lx = 0; lx < TilesPerChunk; lx++)
                    {
                        var bitIndex = ly * TilesPerChunk + lx;
                        var packed = TileFlags[flagBase + bitIndex / TilesPerTileFlagWord];
                        var surface = (int)((packed >> ((bitIndex % TilesPerTileFlagWord) * 2)) & 3);
                        var textureId = surfaceIds[surface];
                        if (!paletteLookup.TryGetValue(textureId, out var paletteIndex))
                        {
                            paletteIndex = paletteList.Count;
                            paletteList.Add(textureId);
                            paletteLookup[textureId] = paletteIndex;
                        }

                        indices[(tileHeight - 1 - (cy * TilesPerChunk + ly)) * tileWidth + cx * TilesPerChunk + lx] = (byte)paletteIndex;
                    }
            }

        palette = paletteList;
        return true;
    }

    private static IReadOnlyList<Gw2MapTerrainChunk> ReadChunks(object? value)
    {
        if (value is not object?[] chunks) return [];
        var result = new List<Gw2MapTerrainChunk>(chunks.Length);
        foreach (var chunkObject in chunks)
        {
            if (chunkObject is not PackObject chunk) continue;
            result.Add(new(
                chunk.Fields.GetValueOrDefault("chunkFlags") is uint flags ? flags : 0,
                PackValue.AsU16List(chunk.Fields.GetValueOrDefault("surfaceIndexArray")),
                Qwords(chunk.Fields.GetValueOrDefault("surfaceTokenArray"))));
        }
        return result;
    }

    private static IReadOnlyList<Gw2MapTerrainTexture> ReadTextures(PackObject? materials)
    {
        if (materials?.Fields.GetValueOrDefault("texFileArray") is not object?[] textures) return [];
        var result = new List<Gw2MapTerrainTexture>(textures.Length);
        foreach (var textureObject in textures)
        {
            if (textureObject is not PackObject texture) continue;
            result.Add(new(
                PackValue.AsU32(texture.Fields.GetValueOrDefault("tokenName")),
                PackValue.AsU32(texture.Fields.GetValueOrDefault("flags")),
                PackValue.AsU32(texture.Fields.GetValueOrDefault("layer")),
                Reference(texture, "filename")));
        }
        return result;
    }

    private static IReadOnlyList<Gw2MapTerrainConstant> ReadConstants(PackObject? materials)
    {
        if (materials?.Fields.GetValueOrDefault("constArray") is not object?[] constants) return [];
        var result = new List<Gw2MapTerrainConstant>(constants.Length);
        foreach (var constantObject in constants)
        {
            if (constantObject is not PackObject constant) continue;
            result.Add(new(
                PackValue.AsU32(constant.Fields.GetValueOrDefault("tokenName")),
                Floats(constant.Fields.GetValueOrDefault("value"))));
        }
        return result;
    }

    private static IReadOnlyList<Gw2MapTerrainChunkMaterial> ReadChunkMaterials(PackObject? materials)
    {
        if (materials?.Fields.GetValueOrDefault("materials") is not object?[] chunkMaterials) return [];
        var result = new List<Gw2MapTerrainChunkMaterial>(chunkMaterials.Length);
        foreach (var materialObject in chunkMaterials)
        {
            if (materialObject is not PackObject chunkMaterial) continue;
            var high = ReadMaterial(chunkMaterial.Fields.GetValueOrDefault("hiResMaterial"));
            if (high is null) continue;
            result.Add(new(
                PackValue.AsBytes(chunkMaterial.Fields.GetValueOrDefault("tiling")),
                high,
                ReadMaterial(chunkMaterial.Fields.GetValueOrDefault("loResMaterial")),
                ReadMaterial(chunkMaterial.Fields.GetValueOrDefault("faderMaterial")),
                ReadUvData(chunkMaterial.Fields.GetValueOrDefault("uvData"))));
        }
        return result;
    }

    private static Gw2MapTerrainMaterial? ReadMaterial(object? value)
    {
        if (value is not PackObject material) return null;
        return new(
            PackValue.AsU32(material.Fields.GetValueOrDefault("fvf")),
            Reference(material, "materialFile"),
            UIntList(material.Fields.GetValueOrDefault("constIndexArray")),
            UIntList(material.Fields.GetValueOrDefault("texIndexArray")));
    }

    private static Gw2MapTerrainUvData? ReadUvData(object? value)
    {
        if (value is not PackObject uvData) return null;
        return new(
            Floats(uvData.Fields.GetValueOrDefault("translation")),
            Floats(uvData.Fields.GetValueOrDefault("xScaleRange")),
            Floats(uvData.Fields.GetValueOrDefault("yScaleRange")),
            Floats(uvData.Fields.GetValueOrDefault("scaleSpeed")),
            uvData.Fields.GetValueOrDefault("rotation") is float rotation ? rotation : 0f);
    }

    private static IReadOnlyList<float> Floats(object? value)
    {
        if (value is float[] typed) return typed;
        if (value is not object?[] values) return [];
        var result = new float[values.Length];
        for (var i = 0; i < result.Length; i++)
            result[i] = values[i] is float single ? single : 0;
        return result;
    }

    private static IReadOnlyList<uint> UIntList(object? value)
    {
        if (value is uint[] typed) return typed;
        if (value is not object?[] values) return [];
        var result = new uint[values.Length];
        for (var i = 0; i < result.Length; i++) result[i] = PackValue.AsU32(values[i]);
        return result;
    }

    private static IReadOnlyList<ulong> Qwords(object? value)
    {
        if (value is not object?[] values) return [];
        var result = new ulong[values.Length];
        for (var i = 0; i < result.Length; i++)
            result[i] = PackValue.AsU64(values[i]);
        return result;
    }

    private static PackFileReference Reference(PackObject? obj, string name) =>
        obj is not null && obj.Fields.GetValueOrDefault(name) is PackFileReference reference ? reference : default;
}
