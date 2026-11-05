using System.Numerics;
using Gw2.Dat.Archive;
using Gw2.Formats;

namespace Gw2.Dat.Content;

public readonly record struct Gw2EffectModel(uint ModelFileId, float Scale, Vector3 OffsetInGameUnits, ulong AnimationToken, float AnimationTimeScale,
    uint DelayMilliseconds, uint Flags, ulong PlacementBoneToken);

public sealed record Gw2EffectDecal(uint MaterialFileId, IReadOnlyList<uint> TextureFileIds, Vector2 ProjectionSize, float ProjectionDepth,
    uint DelayMilliseconds, long DurationMilliseconds);

public sealed class Gw2EffectDefinitions
{
    private const int PointerSize = 8;

    private readonly Gw2ContentIndex _index;
    private readonly EffectContentLayout _layout;

    public Gw2EffectDefinitions(Gw2ContentIndex index, EffectContentLayout layout)
    {
        _index = index;
        _layout = layout;
    }

    public Dictionary<Guid, Gw2EffectModel> ModelsByContentKey()
    {
        var models = new Dictionary<Guid, Gw2EffectModel>();
        foreach (var (contentKey, kind, payload) in Definitions())
            if (ModelOf(kind, payload) is { } model) models[contentKey] = model;
        return models;
    }

    public Dictionary<Guid, Gw2EffectDecal> DecalsByContentKey()
    {
        var decals = new Dictionary<Guid, Gw2EffectDecal>();
        foreach (var (contentKey, kind, payload) in Definitions())
            if (kind == EffectContentLayout.DecalKind && DecalOf(payload) is { } decal) decals[contentKey] = decal;
        return decals;
    }

    private IEnumerable<(Guid ContentKey, uint Kind, Gw2ContentLocation Payload)> Definitions()
    {
        int guidOffset = _index.FieldOffsetsOf(_layout.EffectContentType).GuidOffset;
        foreach (var definition in _index.All(_layout.EffectContentType))
            if (_index.ReadPointer(definition, _layout.PayloadOffset) is { } payload)
                yield return (_index.ReadGuid(definition, guidOffset), _index.ReadUInt32(definition, _layout.KindOffset), payload);
    }

    private Gw2EffectModel? ModelOf(uint kind, Gw2ContentLocation payload)
    {
        if (kind == EffectContentLayout.BoneModelKind) return AttachedModelOf(payload, _layout.BoneModel);
        if (kind == EffectContentLayout.WeaponTrailKind) return AttachedModelOf(payload, _layout.WeaponTrail);
        if (kind != EffectContentLayout.ModelKind) return null;
        var fields = _layout.Model;
        if (FileIdAt(payload, fields.ModelFileOffset) is not { } modelFileId) return null;
        var offset = new Vector3(
            _index.ReadSingle(payload, fields.OffsetInGameUnitsOffset),
            _index.ReadSingle(payload, fields.OffsetInGameUnitsOffset + sizeof(float)),
            _index.ReadSingle(payload, fields.OffsetInGameUnitsOffset + 2 * sizeof(float)));
        return new Gw2EffectModel(modelFileId, _index.ReadSingle(payload, fields.ScaleOffset), new Vector3(-offset.X, -offset.Y, offset.Z),
            ReadUInt64(payload, fields.AnimationTokenOffset), _index.ReadSingle(payload, fields.AnimationTimeScaleOffset),
            _index.ReadUInt32(payload, fields.DelayMillisecondsOffset), _index.ReadUInt32(payload, fields.FlagsOffset),
            ReadUInt64(payload, fields.PlacementBoneTokenOffset));
    }

    private Gw2EffectModel? AttachedModelOf(Gw2ContentLocation payload, EffectAttachedModelLayout fields) =>
        FileIdAt(payload, fields.ModelFileOffset) is { } modelFileId
            ? new Gw2EffectModel(modelFileId, 1f, Vector3.Zero, ReadUInt64(payload, fields.AnimationTokenOffset), 1f, 0, 0, 0)
            : null;

    private Gw2EffectDecal? DecalOf(Gw2ContentLocation payload)
    {
        var fields = _layout.Decal;
        var size = new Vector2(_index.ReadSingle(payload, fields.ProjectionSizeOffset), _index.ReadSingle(payload, fields.ProjectionSizeOffset + sizeof(float)));
        if (FileIdAt(payload, fields.MaterialFileOffset) is not { } materialFileId || size.X <= 0f || size.Y <= 0f) return null;
        uint count = _index.ReadUInt32(payload, fields.TextureFileCountOffset);
        var textures = new uint[count];
        if (count > 0 && _index.ReadPointer(payload, fields.TextureFilesOffset) is { } array)
            for (int index = 0; index < count; index++) textures[index] = _index.ReadFileId(array, index * PointerSize) ?? 0;
        return new Gw2EffectDecal(materialFileId, textures, size, _index.ReadSingle(payload, fields.ProjectionDepthOffset),
            _index.ReadUInt32(payload, fields.DelayMillisecondsOffset), BitConverter.ToInt64(_index.ReadBytes(payload, fields.DurationMillisecondsOffset, sizeof(long))));
    }

    private uint? FileIdAt(Gw2ContentLocation owner, int offset) => _index.ReadFileId(owner, offset) is { } fileId and not 0 ? fileId : null;

    private ulong ReadUInt64(Gw2ContentLocation location, int offset) => BitConverter.ToUInt64(_index.ReadBytes(location, offset, sizeof(ulong)));
}
