using System;
using System.Collections.Generic;
using System.Linq;
using Gw2.Client.Content;
using Gw2.Client.Model;
using Gw2.Dat.Content;
using Gw2.Formats;
using DomainModel = Gw2.Client.Model.Gw2Model;

namespace Gw2.Client.Scene;

public sealed class Gw2CharacterAppearanceScene : IDisposable
{
    private const uint NoPartHideFlags = 0;
    private const int HairNormalRegion = 10;
    private const int EarRegion = 11;
    private const uint HairSkinFlagAddsEars = 0x200;
    private static readonly byte[] WalkOrder = [0, 1, 2, 3, 5, 6, 7, 4, 8, 9, 10, 11, 14, 13, 12];

    private readonly Gw2ContentService _content;
    private readonly Gw2ContentHandle<Gw2Composite>? _composite;
    private readonly List<PartRequests> _parts = [];
    private readonly Gw2ContentHandle<DomainModel>? _speciesModel;
    private List<Gw2ContentHandle<Gw2RgbaImage>?>? _skinPatternMasks;
    private Gw2ContentHandle<Gw2ModelSkeleton>? _skeleton;
    private bool _skeletonRequested;
    private bool _imagesReleased;

    public Gw2CharacterAppearanceScene(Gw2ContentService content, Gw2CharacterAppearance appearance, Gw2ContentHandle<Gw2Composite> composite, Gw2HighResolutionFiles? highResolutionFiles)
    {
        _content = content ?? throw new ArgumentNullException(nameof(content));
        Appearance = appearance ?? throw new ArgumentNullException(nameof(appearance));
        HighResolutionFiles = highResolutionFiles;
        if (appearance.IsSpeciesModel)
        {
            if (appearance.SpeciesModelFile == 0) return;
            _speciesModel = content.Request(appearance.SpeciesModelFile, DecodeModel);
            _skeletonRequested = true;
            SkeletonFileId = appearance.SpeciesModelFile;
            _skeleton = content.Request(appearance.SpeciesModelFile, (reader, fileId) => reader.ReadModelSkeleton(fileId));
            return;
        }

        _composite = composite;
        foreach (var part in appearance.Parts.OrderBy(part => Array.IndexOf(WalkOrder, part.Type)))
            _parts.Add(new PartRequests(
                part,
                RequestModel(part.Mesh),
                RequestAtlasTexture(part.Texture),
                RequestAtlasTexture(part.NormalTexture),
                RequestMask(part.CutMask),
                [RequestMask(part.DyeMask1), RequestMask(part.DyeMask2), RequestMask(part.DyeMask3), RequestMask(part.DyeMask4)]));
    }

    public Gw2CharacterAppearance Appearance { get; }

    public Gw2HighResolutionFiles? HighResolutionFiles { get; }

    private bool HalfResolutionAtlas => HighResolutionFiles is null;

    public IReadOnlyList<DomainModel> Models { get; private set; } = Array.Empty<DomainModel>();

    public Gw2RgbaImage? Atlas { get; private set; }

    public Gw2RgbaImage? NormalAtlas { get; private set; }

    public bool IsSettled { get; private set; }

    public Gw2ModelSkeleton? Skeleton => _skeleton?.Result;

    public uint? SkeletonFileId { get; private set; }

    public Exception? Error { get; private set; }

    public void Update()
    {
        if (IsSettled) return;
        if (_speciesModel is { } species)
        {
            if (!species.IsCompleted || _skeleton is { IsCompleted: false }) return;
            Error = species.Error;
            Models = species.Result is { } model ? [model] : Array.Empty<DomainModel>();
            IsSettled = true;
            return;
        }
        if (_parts.Count == 0)
        {
            IsSettled = true;
            return;
        }
        if (_composite is not { IsCompleted: true } compositeHandle) return;
        if (compositeHandle.Result is not { } composite)
        {
            Error = compositeHandle.Error ?? new InvalidOperationException("Character composite file failed to decode.");
            IsSettled = true;
            return;
        }

        _skinPatternMasks ??= RequestSkinPatternMasks(composite);
        RequestSkeleton(composite);
        if (_parts.Any(part => !part.IsCompleted) || _skinPatternMasks.Any(handle => handle is { IsCompleted: false }) || _skeleton is { IsCompleted: false }) return;

        Error = _parts.Select(part => part.Error).Concat(_skinPatternMasks.Select(handle => handle?.Error)).FirstOrDefault(error => error is not null);
        Models = _parts.Select(part => part.Model?.Result).OfType<DomainModel>().ToList();
        ComposeAtlas(composite);
        IsSettled = true;
    }

    public void ReleaseImages()
    {
        if (_imagesReleased) return;
        _imagesReleased = true;
        foreach (var part in _parts)
            foreach (var image in part.Images())
                if (image is not null) _content.Release(image);
        foreach (var mask in _skinPatternMasks ?? [])
            if (mask is not null) _content.Release(mask);
        for (var index = 0; index < _parts.Count; index++)
            _parts[index] = _parts[index] with { Texture = null, NormalTexture = null, CutMask = null, DyeMasks = [] };
        _skinPatternMasks?.Clear();
        Atlas = null;
        NormalAtlas = null;
    }

    public void Dispose()
    {
        ReleaseImages();
        foreach (var part in _parts)
            if (part.Model is { } model) _content.Release(model);
        if (_speciesModel is { } species) _content.Release(species);
        if (_skeleton is { } skeleton) _content.Release(skeleton);
        Models = Array.Empty<DomainModel>();
    }

    private void ComposeAtlas(Gw2Composite composite)
    {
        var first = _parts.FirstOrDefault(part => part.Part.BlitRectIndex < composite.BlitRects.Count);
        if (first is null) return;
        var shift = HalfResolutionAtlas ? 1 : 0;
        var atlasSet = composite.BlitRects[first.Part.BlitRectIndex];
        var atlas = new Gw2RgbaImage((int)atlasSet.Width >> shift, (int)atlasSet.Height >> shift);
        var normalAtlas = Gw2RgbaImage.FlatNormals(atlas.Width, atlas.Height);
        for (var index = 0; index < _parts.Count; index++)
        {
            var requests = _parts[index];
            var part = requests.Part;
            if (part.BlitRectIndex >= composite.BlitRects.Count) continue;
            var set = composite.BlitRects[part.BlitRectIndex];
            var rects = RectsOf(set, Gw2CompositeAtlas.Regions(part.Type, NoPartHideFlags, part.HideFlags, part.MeshOverlap != 0));
            if (requests.Texture?.Result is { } texture)
            {
                if (IsBarePart(part.Type)) Gw2CompositeAtlas.Compose(atlas, rects, texture, BarePasses(requests, _skinPatternMasks![index]?.Result));
                else if (IsHairPart(part.Type)) Gw2CompositeAtlas.ComposeOver(atlas, rects, texture, HairPasses(requests));
                else Gw2CompositeAtlas.ComposeOver(atlas, rects, texture, ArmourPasses(requests));
            }
            if (requests.NormalTexture?.Result is { } normals)
                Gw2CompositeAtlas.Blit(normalAtlas, NormalRects(set, part, rects), normals);
        }

        NormalAtlas = normalAtlas;
        Atlas = atlas;
    }

    private IReadOnlyList<Gw2AtlasRect> NormalRects(Gw2CompositeBlitRectSet set, Gw2CharacterAppearancePart part, IReadOnlyList<Gw2AtlasRect> partRects)
    {
        if (IsBarePart(part.Type)) return partRects;
        if (IsHairPart(part.Type))
        {
            var regions = new List<int> { HairNormalRegion };
            if (part.MeshOverlap == 0 || (part.SkinFlags & HairSkinFlagAddsEars) != 0) regions.Add(EarRegion);
            return RectsOf(set, regions);
        }
        return RectsOf(set, Gw2CompositeAtlas.Regions(part.Type, NoPartHideFlags, part.HideFlags & ~part.SkinFlags, part.MeshOverlap != 0));
    }

    private IReadOnlyList<Gw2AtlasRect> RectsOf(Gw2CompositeBlitRectSet set, IReadOnlyList<int> regions) =>
        regions.SelectMany(region => Gw2CompositeAtlas.Rects(set, region, HalfResolutionAtlas)).ToList();

    private Gw2CompositePass[] BarePasses(PartRequests requests, Gw2RgbaImage? skinPatternMask) =>
    [
        new(null, Appearance.SkinColor),
        new(skinPatternMask, Appearance.SkinPatternColor),
        new(requests.DyeMasks[0]?.Result, Appearance.HairColor),
        new(requests.CutMask?.Result, null),
    ];

    private Gw2CompositePass[] HairPasses(PartRequests requests) =>
    [
        new(requests.CutMask?.Result, null),
        new(requests.DyeMasks[0]?.Result, Appearance.HairColor),
        new(requests.DyeMasks[1]?.Result, Appearance.SecondHairColor),
    ];

    private static Gw2CompositePass[] ArmourPasses(PartRequests requests) =>
    [
        new(requests.CutMask?.Result, null),
        .. requests.DyeMasks.Select((mask, channel) => new Gw2CompositePass(mask?.Result, requests.Part.DyeChannels.Count > channel ? requests.Part.DyeChannels[channel] : null)),
    ];

    private List<Gw2ContentHandle<Gw2RgbaImage>?> RequestSkinPatternMasks(Gw2Composite composite)
    {
        var race = FindRace(composite, Appearance.Race);
        var pattern = race is null ? null : Gw2CharacterSkinPattern.Select(race, (ushort)Appearance.SkinPatternIndex);
        return _parts
            .Select(part => pattern is null || !IsBarePart(part.Part.Type) || !Gw2CharacterSkinPattern.TextureFor(pattern, part.Part.Type).TryGetFileId(out var maskFileId) ? null : RequestMask(maskFileId))
            .ToList();
    }

    private void RequestSkeleton(Gw2Composite composite)
    {
        if (_skeletonRequested) return;
        _skeletonRequested = true;
        if (FindRace(composite, Appearance.Race) is { } race && race.SkeletonFile.TryGetFileId(out var skeletonFileId))
        {
            SkeletonFileId = skeletonFileId;
            _skeleton = _content.Request(skeletonFileId, (reader, fileId) => reader.ReadModelSkeleton(fileId));
        }
    }

    private static Gw2CompositeRace? FindRace(Gw2Composite composite, string raceToken) =>
        Gw2CharacterBodyParts.FindRace(composite, raceToken)
        ?? composite.Races.FirstOrDefault(race => race.Name.StartsWith(raceToken, StringComparison.OrdinalIgnoreCase));

    private Gw2ContentHandle<DomainModel>? RequestModel(uint fileId) => fileId == 0 ? null : _content.Request(fileId, DecodeModel);

    private Gw2ContentHandle<Gw2RgbaImage>? RequestMask(uint fileId) => fileId == 0 ? null : _content.Request(fileId, DecodeMaskImage);

    private Gw2ContentHandle<Gw2RgbaImage>? RequestAtlasTexture(uint fileId) =>
        fileId == 0 ? null : _content.Request(HighResolutionFiles?.HighResolutionOf(fileId) ?? fileId, DecodeRgbaImage);

    private static bool IsBarePart(byte type) => type is <= 3 or 5 or 7;

    private static bool IsHairPart(byte type) => type is 4 or 6;

    private static DomainModel? DecodeModel(Gw2ContentReader reader, uint fileId)
    {
        var model = reader.ReadModel(fileId);
        return model is null ? null : Gw2ModelBuilder.Build(fileId, model, _ => null);
    }

    private static Gw2RgbaImage? DecodeMaskImage(Gw2ContentReader reader, uint fileId) =>
        reader.ReadTexture(fileId)?.Texture is { } texture ? Gw2RgbaImage.FromMaskTexture(texture) : null;

    private static Gw2RgbaImage? DecodeRgbaImage(Gw2ContentReader reader, uint fileId) =>
        reader.ReadTexture(fileId)?.Texture is { } texture ? Gw2RgbaImage.FromTexture(texture) : null;

    private sealed record PartRequests(
        Gw2CharacterAppearancePart Part,
        Gw2ContentHandle<DomainModel>? Model,
        Gw2ContentHandle<Gw2RgbaImage>? Texture,
        Gw2ContentHandle<Gw2RgbaImage>? NormalTexture,
        Gw2ContentHandle<Gw2RgbaImage>? CutMask,
        Gw2ContentHandle<Gw2RgbaImage>?[] DyeMasks)
    {
        public bool IsCompleted => (Model?.IsCompleted ?? true) && Images().All(image => image?.IsCompleted ?? true);

        public Exception? Error => Model?.Error ?? Images().Select(image => image?.Error).FirstOrDefault(error => error is not null);

        public IEnumerable<Gw2ContentHandle<Gw2RgbaImage>?> Images() => new[] { Texture, NormalTexture, CutMask }.Concat(DyeMasks);
    }
}
