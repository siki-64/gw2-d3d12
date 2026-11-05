using Gw2.Formats;
using Gw2.Dat.Identity;

namespace Gw2.Dat.Content;

public sealed record Gw2TextureAsset(Gw2Texture? Texture, Gw2RawDds? RawDds, byte[] Bytes)
{
    public bool IsRawDds => RawDds is not null;
    public string Container => Texture?.Container ?? "DDS";
    public string Format => Texture?.Format ?? "RAW";
    public int Width => Texture?.Width ?? RawDds?.Width ?? 0;
    public int Height => Texture?.Height ?? RawDds?.Height ?? 0;
}

public sealed record Gw2MapBundle(
    Gw2MapInfo Info,
    Gw2MapProps Props,
    Gw2MapTerrain? Terrain,
    Gw2MapCollision? Collision,
    Gw2MapZones? Zones,
    Gw2MapEnvironment? Environment,
    Gw2MapLayers? Layers,
    Gw2MapLights? Lights,
    Gw2PagedImage? Atlas,
    Gw2MapWater? Water)
{
    public Gw2MapDecals? Decals { get; init; }
}

public sealed record Gw2EffectModelAsset(
    Gw2Model Model,
    Gw2ModelParticles Particles,
    Gw2ModelSkeleton? Skeleton,
    Gw2ModelAnimation? Animations,
    Gw2ModelStreaks Streaks);

public sealed record Gw2ModelBundle(
    Gw2Model Model,
    Gw2ModelCollision? Collision,
    Gw2ModelSkeleton? Skeleton = null,
    Gw2ModelAnimation? Animation = null);

public sealed record Gw2DecodedFile(uint FileId, string Kind, byte[] Bytes, IReadOnlyList<Gw2DecodedObject> Objects)
{
    public T? Get<T>() where T : class => Objects.Select(decoded => decoded.Value).OfType<T>().FirstOrDefault();
}

public sealed class Gw2ContentReader : IDisposable
{
    private readonly FileArchive _archive;
    private readonly Gw2PackSchemaCatalog _schemaCatalog;
    private FileArchiveIds? _ids;
    private Gw2TextManifest? _textManifest;
    private HashSet<uint>? _glyphStreamFileIds;
    private Gw2KeyStore? _keys;
    private readonly string? _packCachePath;

    private Gw2ContentReader(FileArchive archive, Gw2PackSchemaCatalog schemaCatalog, string? packCachePath)
    {
        _archive = archive;
        _schemaCatalog = schemaCatalog;
        _packCachePath = packCachePath;
    }

    public FileArchive Archive => _archive;

    public IReadOnlyDictionary<uint, uint> FileIndex => _archive.FileIndex;

    public FileArchiveIds Ids => _ids ??= new FileArchiveIds(_archive.FileIndex);

    public Gw2KeyStore Keys => _keys ??= Gw2KeyStore.Empty();

    public static Gw2ContentReader Open(string datPath, bool verifyChecksums = true, bool usePackCache = true) =>
        new(FileArchive.Open(datPath, verifyChecksums), Gw2PackSchemaCatalog.Default,
            usePackCache ? Gw2CntcPackCache.PathFor(datPath) : null);

    public static Gw2ContentReader Open(Stream archive, string? packCachePath, bool verifyChecksums = true) =>
        new(FileArchive.Open(archive, verifyChecksums), Gw2PackSchemaCatalog.Default, packCachePath);

    public string? PackCachePath => _packCachePath;

    public bool LoadedContentStoreFromPackCache { get; private set; }

    public bool Contains(uint contentFileId) => _archive.FileIndex.ContainsKey(contentFileId);

    public byte[] ReadBytes(uint contentFileId)
    {
        using var stream = new MemoryStream();
        _archive.DecodeFileId(contentFileId, stream);
        return stream.ToArray();
    }

    public Gw2TextManifest TextManifest => _textManifest ??= Gw2PackDecoders.Decode<Gw2TextManifest>(ReadDocument(Gw2TextManifest.ManifestFileId))
        ?? throw new InvalidDataException("Text pack manifest missing");

    public (Gw2TextStatus Status, string Text) ReadText(uint textId, Gw2Language language, ulong? key = null)
    {
        if (!TextManifest.TryLocate(textId, language, out var fileId, out var indexInFile) || !Contains(fileId))
            return (Gw2TextStatus.Missing, string.Empty);
        var entries = Gw2TextEntry.ReadFile(ReadBytes(fileId));
        return indexInFile < entries.Count
            ? Gw2TextDecoder.Decode(entries[indexInFile], key)
            : (Gw2TextStatus.Missing, string.Empty);
    }

    public (Gw2TextStatus Status, string Text) ReadKnownText(uint textId, Gw2Language language) => ReadText(textId, language, Keys);

    public (Gw2TextStatus Status, string Text) ReadText(uint textId, Gw2Language language, Gw2KeyStore keys)
    {
        if (!TextManifest.TryLocate(textId, language, out var fileId, out var indexInFile) || !Contains(fileId))
            return (Gw2TextStatus.Missing, string.Empty);
        var entries = Gw2TextEntry.ReadFile(ReadBytes(fileId));
        if (indexInFile >= entries.Count)
            return (Gw2TextStatus.Missing, string.Empty);
        var entry = entries[indexInFile];
        if (!entry.IsEncrypted)
            return Gw2TextDecoder.Decode(entry, null);
        return keys.FindTextKey(textId) is { } key
            ? Gw2TextDecoder.Decode(entry, key)
            : (Gw2TextStatus.MissingKey, string.Empty);
    }

    public Gw2DecodedFile Decode(uint contentFileId)
    {
        var bytes = ReadBytes(contentFileId);
        var kind = Gw2DatFileKind.Classify(bytes);
        if (kind == Gw2DatFileKind.Unknown && IsGlyphStream(contentFileId)) kind = Gw2DatFileKind.GlyphStream;
        return new(contentFileId, kind, bytes, DecodeObjects(kind, bytes));
    }

    public static IReadOnlyList<Gw2DecodedObject> DecodeObjects(string kind, byte[] bytes, Gw2PackSchemaCatalog? catalog = null)
    {
        switch (kind)
        {
            case Gw2DatFileKind.Pack:
                var document = Gw2AssetDocument.Parse(bytes, catalog ?? Gw2PackSchemaCatalog.Default);
                var objects = Gw2PackDecoders.Decode(document).ToList();
                if (document.Pack.Type == Gw2FontManifest.PackType)
                    objects.Add(new(nameof(Gw2FontManifest), Gw2FontManifest.Parse(bytes)));
                return objects;
            case Gw2DatFileKind.Texture or Gw2DatFileKind.Dds:
                return ReadTexture(bytes) is { } texture ? [new(nameof(Gw2TextureAsset), texture)] : [];
            case Gw2DatFileKind.Strings:
                return [new(nameof(Gw2TextEntry), Gw2TextEntry.ReadFile(bytes))];
            default:
                return [];
        }
    }

    public bool IsGlyphStream(uint contentFileId) => (_glyphStreamFileIds ??= ReadGlyphStreamFileIds()).Contains(contentFileId);

    private HashSet<uint> ReadGlyphStreamFileIds()
    {
        if (!Contains(Gw2FontManifest.ManifestFileId)) return [];
        return Gw2FontManifest.Parse(ReadBytes(Gw2FontManifest.ManifestFileId))
            .SelectMany(entry => entry.RangeFileIds)
            .OfType<uint>()
            .ToHashSet();
    }

    public Gw2AssetDocument ReadDocument(uint contentFileId, Gw2PackReadLimits? limits = null) =>
        Gw2AssetDocument.Parse(ReadBytes(contentFileId), _schemaCatalog, limits);

    public Gw2Composite? ReadComposite(uint contentFileId) =>
        Gw2PackDecoders.Decode<Gw2Composite>(ReadDocument(contentFileId, Gw2PackReadLimits.Composite));

    public const uint AnimSequencesBaseId = 0x2D1D4;
    public const uint ContentStoreFirstFileId = 0x13930E;
    public const int ContentStorePackCount = 32;
    private IReadOnlyList<Gw2CntcPack>? _contentStore;
    private Gw2ContentIndex? _contentIndex;
    private IReadOnlyList<ReadOnlyMemory<byte>>? _contentPacks;

    public IReadOnlyList<Gw2CntcPack> ContentStore => _contentStore ??= ReadContentStore();

    public Gw2ContentIndex ContentIndex => _contentIndex ??= new Gw2ContentIndex(ContentStore);

    public IReadOnlyList<ReadOnlyMemory<byte>> ContentPacks => _contentPacks ??= ContentStore.Select(static pack => pack.Data).ToArray();

    private IReadOnlyList<Gw2CntcPack> ReadContentStore()
    {
        var stamps = ContentStoreStamps();
        if (_packCachePath is not null && Gw2CntcPackCache.TryRead(_packCachePath, stamps, out var cached))
        {
            LoadedContentStoreFromPackCache = true;
            return cached.Select(pack => Gw2CntcPack.Parse(pack)).ToArray();
        }

        var decodedPacks = stamps.Select(stamp => ReadBytes(stamp.FileId)).ToArray();
        if (_packCachePath is not null) TryWritePackCache(_packCachePath, stamps, decodedPacks);
        return decodedPacks.Select(pack => Gw2CntcPack.Parse(pack)).ToArray();
    }

    private static void TryWritePackCache(string path, Gw2CntcPackStamp[] stamps, byte[][] decodedPacks)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            Gw2CntcPackCache.Write(path, stamps, decodedPacks);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    public string WritePackCache()
    {
        var path = _packCachePath ?? throw new InvalidOperationException("The pack cache is disabled for this reader");
        var stamps = ContentStoreStamps();
        Gw2CntcPackCache.Write(path, stamps, stamps.Select(stamp => ReadBytes(stamp.FileId)).ToArray());
        return path;
    }

    private Gw2CntcPackStamp[] ContentStoreStamps()
    {
        var stamps = new List<Gw2CntcPackStamp>(ContentStorePackCount);
        for (var index = 0u; index < ContentStorePackCount; index++)
        {
            var fileId = ContentStoreFirstFileId + index;
            if (_archive.FileIndex.TryGetValue(fileId, out var record))
                stamps.Add(Gw2CntcPackCache.StampOf(fileId, _archive.ReadMftEntry(record)));
        }

        return [.. stamps];
    }

    public Gw2AnimSequences? ReadAnimSequences() =>
        Ids.TryGetFileId(AnimSequencesBaseId, out var fileId) && Contains(fileId)
            ? Gw2PackDecoders.Decode<Gw2AnimSequences>(ReadDocument(fileId))
            : null;

    public Gw2AnimMachines? ReadAnimMachines() =>
        Ids.TryGetFileId(AnimSequencesBaseId, out var fileId) && Contains(fileId)
            ? Gw2PackDecoders.Decode<Gw2AnimMachines>(ReadDocument(fileId))
            : null;

    public Gw2MapExpansionProperties? ReadMapExpansionProperties(uint mapFileId) =>
        Gw2PackDecoders.Decode<Gw2MapExpansionProperties>(ReadDocument(mapFileId));

    public Gw2MapShadow? ReadMapShadow(uint mapFileId)
    {
        var document = ReadDocument(mapFileId);
        if (Gw2PackDecoders.Decode<Gw2MapShadow>(document) is { } baked) return baked;
        return Gw2PackDecoders.Decode<Gw2MapShadowExt>(document) is { } external
            ? Gw2PackDecoders.Decode<Gw2MapShadow>(ReadDocument(external.File.FileId))
            : null;
    }

    public Gw2Model? ReadModel(uint contentFileId) => Gw2PackDecoders.Decode<Gw2Model>(ReadDocument(contentFileId));

    public Gw2ModelBundle? ReadModelBundle(uint contentFileId)
    {
        var document = ReadDocument(contentFileId);
        if (Gw2PackDecoders.Decode<Gw2Model>(document) is not { HasGeometry: true } model) return null;
        return new(
            model,
            Gw2PackDecoders.Decode<Gw2ModelCollision>(document),
            ReadSkeleton(document),
            LoadStreamedAnimations(Gw2PackDecoders.Decode<Gw2ModelAnimation>(document)));
    }

    public Gw2EffectModelAsset? ReadEffectModel(uint contentFileId)
    {
        var document = ReadDocument(contentFileId);
        if (Gw2PackDecoders.Decode<Gw2Model>(document) is not { } model) return null;
        return new(model, Gw2ModelParticles.Parse(document), ReadSkeleton(document), LoadStreamedAnimations(ReadModelAnimations(contentFileId)), Gw2ModelStreaks.Parse(document));
    }

    public Gw2ModelAnimation? ReadModelAnimations(uint contentFileId)
    {
        var root = ReadAnimationBank(contentFileId);
        if (root is null) return null;
        return Gw2ModelAnimationResolver.Resolve(contentFileId,
            id => id == contentFileId ? root : ReadAnimationBank(id));
    }

    public Gw2ModelAnimation? ReadStreamedModelAnimations(uint contentFileId) => LoadStreamedAnimations(ReadModelAnimations(contentFileId));

    private const int MaximumSkeletonReferenceDepth = 4;

    public Gw2ModelSkeleton? ReadModelSkeleton(uint contentFileId) => ReadSkeleton(ReadDocument(contentFileId));

    private Gw2ModelSkeleton? ReadSkeleton(Gw2AssetDocument document)
    {
        var skeleton = Gw2PackDecoders.Decode<Gw2ModelSkeleton>(document);
        for (var depth = 0; skeleton is { Bones.Count: 0 } && depth < MaximumSkeletonReferenceDepth; depth++)
        {
            if (!skeleton.FileReference.TryGetFileId(out var fileId) || fileId == 0 || !Contains(fileId)) break;
            var referenced = Gw2PackDecoders.Decode<Gw2ModelSkeleton>(ReadDocument(fileId));
            if (referenced is null) break;
            skeleton = new Gw2ModelSkeleton(
                referenced.Bones.Count == 0 ? referenced.FileReference : skeleton.FileReference,
                referenced.Bones,
                referenced.BoneFlags,
                referenced.BoneConstraints,
                referenced.EmitterBones,
                referenced.TrackMasks)
            {
                ModelName = referenced.ModelName,
                SkeletonName = referenced.SkeletonName,
                LodType = referenced.LodType,
                InitialPlacement = referenced.InitialPlacement,
                MeshBindingCount = referenced.MeshBindingCount,
                MirrorSpec = referenced.MirrorSpec,
                OverrideBoneConstraints = skeleton.OverrideBoneConstraints,
            };
        }

        return skeleton;
    }

    public Gw2ModelAnimation? ReadAnimationBank(uint contentFileId) =>
        Gw2PackDecoders.Decode<Gw2ModelAnimation>(ReadDocument(contentFileId));

    private Gw2ModelAnimation? LoadStreamedAnimations(Gw2ModelAnimation? bank)
    {
        if (bank is null || !bank.Animations.Any(animation => animation.IsStreamed)) return bank;

        var animations = new List<Gw2ModelAnimationData>(bank.Animations.Count);
        foreach (var animation in bank.Animations)
            animations.Add(ReadStreamedAnimation(animation));
        return new Gw2ModelAnimation(animations, bank.CompoundAnimations, bank.Fallbacks, bank.Imports,
            bank.GrannyAnimation, bank.GrannyPointers);
    }

    public Gw2ModelAnimationData ReadStreamedAnimation(Gw2ModelAnimationData animation)
    {
        if (!animation.IsStreamed || !animation.StreamedAnimationFile.TryGetFileId(out var fileId) || !Contains(fileId)) return animation;
        var document = ReadDocument(fileId);
        var streamed = Gw2PackDecoders.Decode<Gw2ModelAnimation>(document);
        if (streamed is null || streamed.GrannyAnimation.Length == 0) return animation;
        return animation with
        {
            GrannyAnimation = streamed.GrannyAnimation,
            GrannyPointers = streamed.GrannyPointers,
            PointerSize = document.Pack.PointerSize,
        };
    }

    public Gw2AmatMaterial? ReadMaterial(uint contentFileId) => ReadMaterial(ReadBytes(contentFileId));

    public Gw2AmatMaterial? ReadMaterial(byte[] bytes) => Gw2PackDecoders.Decode<Gw2AmatMaterial>(Gw2AssetDocument.Parse(bytes, _schemaCatalog));

    public Gw2PagedImage? ReadPagedImage(uint contentFileId) => Gw2PackDecoders.Decode<Gw2PagedImage>(ReadDocument(contentFileId));

    public Gw2TextureAsset? ReadTexture(uint contentFileId) => ReadTexture(ReadBytes(contentFileId));

    public static bool IsTexture(byte[] bytes) => Gw2RawDds.TryParse(bytes, out _) || Gw2Texture.LooksLike(bytes);

    public static Gw2TextureAsset? ReadTexture(byte[] bytes)
    {
        if (Gw2RawDds.TryParse(bytes, out var dds))
            return new Gw2TextureAsset(null, dds, bytes);
        if (!Gw2Texture.LooksLike(bytes)) return null;

        return new Gw2TextureAsset(Gw2Texture.Parse(bytes), null, bytes);
    }

    public Gw2MapBundle? ReadMap(uint contentFileId)
    {
        var document = ReadDocument(contentFileId);
        var info = Gw2PackDecoders.Decode<Gw2MapInfo>(document);
        if (info is null) return null;

        var terrain = Gw2PackDecoders.Decode<Gw2MapTerrain>(document);
        return new(
            info,
            Gw2PackDecoders.Decode<Gw2MapProps>(document) ?? new Gw2MapProps([]),
            terrain,
            Gw2PackDecoders.Decode<Gw2MapCollision>(document),
            Gw2PackDecoders.Decode<Gw2MapZones>(document),
            Gw2PackDecoders.Decode<Gw2MapEnvironment>(document),
            Gw2PackDecoders.Decode<Gw2MapLayers>(document),
            Gw2PackDecoders.Decode<Gw2MapLights>(document),
            ReadAtlas(terrain),
            Gw2PackDecoders.Decode<Gw2MapWater>(document))
        {
            Decals = Gw2PackDecoders.Decode<Gw2MapDecals>(document),
        };
    }

    public void Dispose() => _archive.Dispose();

    private Gw2PagedImage? ReadAtlas(Gw2MapTerrain? terrain)
    {
        if (terrain is null || !terrain.PagedImage.TryGetFileId(out var imageId) || !Contains(imageId)) return null;
        return ReadPagedImage(imageId);
    }
}
