using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Gw2.Client.Content;
using Gw2.Client.Model;
using Gw2.Dat.Content;
using Gw2.Dat.Archive;
using Gw2.Formats;
using DomainModel = Gw2.Client.Model.Gw2Model;

namespace Gw2.Client.Scene;



public sealed class CharacterScene
{
    private const uint NoPartHideFlags = 0;
    private const uint LastBodySkinPartType = 3;
    private const uint SkinMaterialKey = 0;
    private const uint PlayerDyeMaterialKey = 1;
    private const uint FirstArmorPartType = 8;
    private const uint FirstDyeIsSkinFlag = 0x2;
    private const int DyeMaterialBitsPerChannel = 2;
    private const uint DyeMaterialMask = 0x3;
    private const uint FirstDyeMaterialKey = 1;
    public const string SecondHairDyeSet = "Gw2.Common.Color.Dye Set Accessories";
    public const string SylvariSecondHairDyeSet = "Gw2.Common.Color.Dye Set Sylvari HairMask2";
    private static readonly Gw2ColorShift ClearedColorShift = new(0f, 0f, 0f, 0f, 0f);
    public const byte DefaultSkinColorIndex = 0x80;
    public const byte DefaultDyeColorIndex = 0x80;
    public const byte DefaultSkinPatternColorIndex = 0x80;
    public const byte DefaultEyeColorIndex = 0x80;
    public const ushort NoSkinPattern = 0;

    private readonly Gw2ContentService _content;
    private readonly string _raceSexName;
    private readonly Gw2ContentHandle<Gw2Composite> _composite;
    private Gw2ContentHandle<Gw2ModelSkeleton>? _skeleton;
    private Gw2ContentHandle<Gw2CharacterIdlePlayback>? _idle;
    private Gw2ContentHandle<Gw2CharacterLocomotionStates>? _locomotion;
    private readonly Gw2CharacterAnimator _animator = new();
    private Gw2ContentHandle<CharacterColors>? _colors;
    private Gw2ContentHandle<IReadOnlyList<WornArmorPart>>? _armor;
    private readonly Dictionary<Gw2CompositeFileData, IReadOnlyList<Gw2ColorShift>> _armorDyes = new(ReferenceEqualityComparer.Instance);
    private IReadOnlyList<Gw2CompositeFileData>? _partData;
    private List<Gw2ContentHandle<DomainModel>>? _parts;
    private List<Gw2ContentHandle<Gw2RgbaImage>?>? _baseTextures;
    private List<Gw2ContentHandle<Gw2RgbaImage>?>? _normalTextures;
    private List<Gw2ContentHandle<Gw2RgbaImage>?>? _dyeMasks;
    private List<Gw2ContentHandle<Gw2RgbaImage>?>? _secondDyeMasks;
    private List<Gw2ContentHandle<Gw2RgbaImage>?>? _thirdDyeMasks;
    private List<Gw2ContentHandle<Gw2RgbaImage>?>? _fourthDyeMasks;
    private List<Gw2ContentHandle<Gw2RgbaImage>?>? _cutMasks;
    private List<Gw2ContentHandle<Gw2RgbaImage>?>? _skinPatternMasks;
    private Gw2BoneConstraintSimulator? _boneConstraints;
    private Gw2ModelSkeleton? _boneConstraintSkeleton;
    private readonly List<CharacterAttachment> _attachments = [];

    public CharacterScene(Gw2ContentService content, string raceSexName)
    {
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _raceSexName = raceSexName;
        _composite = content.Request(Gw2RaceAnimations.CompositeFileId, (reader, fileId) => reader.ReadComposite(fileId));
    }

    public string RaceSexName => _raceSexName;

    public Gw2AppearanceIndices? Appearance { get; private init; }

    public Gw2CharacterArmor Armor { get; private init; } = Gw2CharacterArmor.None;

    public static CharacterScene? Of(Gw2ContentService content, Gw2AppearanceIndices appearance, Gw2HighResolutionFiles? highResolutionFiles) =>
        Of(content, appearance, Gw2CharacterArmor.None, highResolutionFiles);

    public static CharacterScene? Of(Gw2ContentService content, Gw2AppearanceIndices appearance, Gw2CharacterArmor armor, Gw2HighResolutionFiles? highResolutionFiles)
    {
        ArgumentNullException.ThrowIfNull(armor);
        ArgumentNullException.ThrowIfNull(appearance);
        if (appearance.CompositeRaceName is not { } compositeRaceName) return null;
        return new CharacterScene(content, compositeRaceName)
        {
            Appearance = appearance,
            Armor = armor,
            HighResolutionFiles = highResolutionFiles,
            FaceIndex = appearance.FaceIndex,
            HairStyleIndex = appearance.HairStyleIndex == Gw2AppearanceIndices.NoPart ? null : appearance.HairStyleIndex,
            SkinColorIndex = (byte)appearance.SkinColorIndex,
            DyeColorIndex = (byte)appearance.HairColorIndex,
            SecondHairColorIndex = (byte)appearance.SecondHairColorIndex,
            SkinPatternIndex = (ushort)appearance.SkinPatternIndex,
            SkinPatternColorIndex = (byte)appearance.SkinPatternColorIndex,
            EyeColorIndex = (byte)appearance.EyeColorIndex,
        };
    }

    public static CharacterScene OfCompositeIdentity(Gw2ContentService content, ulong raceToken, ulong? variantToken, Gw2CharacterArmor armor, Gw2HighResolutionFiles? highResolutionFiles) =>
        new(content, Token64.Decode(raceToken))
        {
            VariantToken = variantToken,
            Armor = armor,
            HighResolutionFiles = highResolutionFiles,
        };

    public ulong? VariantToken { get; private init; }

    public Gw2ModelSkeleton? Skeleton => _skeleton?.Result;

    public Gw2AnimationClip? IdleClip => _idle?.Result?.Clip;

    public IReadOnlyList<DomainModel> Parts =>
        _parts?.Where((part, index) => part.Result is not null && !IsReplacedByAnotherPart(index)).Select(part => part.Result!).ToList() ?? (IReadOnlyList<DomainModel>)Array.Empty<DomainModel>();

    public Gw2RgbaImage? Atlas { get; private set; }

    public Gw2RgbaImage? NormalAtlas { get; private set; }

    public Gw2HighResolutionFiles? HighResolutionFiles { get; init; }

    private bool HalfResolutionAtlas => HighResolutionFiles is null;

    public byte SkinColorIndex { get; init; } = DefaultSkinColorIndex;

    public Gw2ColorShift SkinColor { get; private set; } = Gw2ColorShift.Identity;

    public byte DyeColorIndex { get; init; } = DefaultDyeColorIndex;

    public Gw2ColorShift DyeColor { get; private set; } = Gw2ColorShift.Identity;

    public byte? SecondHairColorIndex { get; init; }

    public Gw2ColorShift? SecondHairColor { get; private set; }

    public IReadOnlyList<Gw2ColorShift> UnderwearColors { get; private set; } = [];

    public ushort SkinPatternIndex { get; init; } = NoSkinPattern;

    public int? FaceIndex { get; init; }

    public int? HairStyleIndex { get; init; }

    public byte EyeColorIndex { get; init; } = DefaultEyeColorIndex;

    public Gw2ColorShift EyeColor { get; private set; } = Gw2ColorShift.Identity;

    public byte SkinPatternColorIndex { get; init; } = DefaultSkinPatternColorIndex;

    public Gw2ColorShift SkinPatternColor { get; private set; } = Gw2ColorShift.Identity;

    public bool IsSettled => Atlas is not null || Error is not null;

    public Exception? Error =>
        _composite.Error ?? _armor?.Error ?? _skeleton?.Error ?? _idle?.Error ?? _locomotion?.Error ?? _colors?.Error ??
        _parts?.Select(part => part.Error).FirstOrDefault(error => error is not null) ??
        AllTextures().Select(texture => texture?.Error).FirstOrDefault(error => error is not null);

    public Matrix4x4[] SkinPalette { get; private set; } = Array.Empty<Matrix4x4>();

    public IReadOnlyList<CharacterAttachment> Attachments => _attachments;

    public CharacterAttachment Attach(uint modelFileId, string socketBoneName, string? gripBoneName)
    {
        var attachment = new CharacterAttachment(
            Token64.EncodeAfterColon(socketBoneName),
            gripBoneName is null ? 0 : Token64.EncodeAfterColon(gripBoneName),
            _content.Request(modelFileId, (reader, fileId) => reader.ReadModelSkeleton(fileId)));
        _attachments.Add(attachment);
        return attachment;
    }

    public void Detach(CharacterAttachment attachment) => _attachments.Remove(attachment);

    public System.Numerics.Vector3 Wind { get; set; }

    public Gw2CharacterMotion Motion { get; set; }

    public int LocomotionStateId => _animator.StateId;

    public Quaternion ModelRotation => _animator.ModelRotation;

    public void Update(float elapsedSeconds)
    {
        RequestParts();
        ComposeAtlas();
        Pose(elapsedSeconds);
    }

    private void RequestParts()
    {
        if (_parts is not null || _composite.Result is not { } composite) return;

        var foundRace = Gw2CharacterBodyParts.FindRace(composite, _raceSexName);
        if (VariantToken is { } variantToken)
        {
            if (foundRace is null) RequestPartResources([], null);
            else RequestVariantParts(composite, foundRace, variantToken);
            return;
        }
        if (foundRace is null && Appearance is null)
        {
            RequestPartResources([], null);
            return;
        }
        var race = foundRace ?? throw new InvalidOperationException($"Composite has no race '{_raceSexName}'.");
        _armor ??= RequestArmor(composite, race);
        if (!_armor.IsCompleted || _armor.Result is not { } wornArmor) return;
        foreach (var worn in wornArmor) _armorDyes[worn.Part] = worn.Dyes;
        RequestSkeleton(race);
        var skinPattern = Gw2CharacterSkinPattern.Select(race, SkinPatternIndex);
        _colors = RequestColors(race, skinPattern is not null);
        RequestPartResources(Gw2CharacterBodyParts.PartsOf(race, FaceIndex, HairStyleIndex, Gw2CharacterRaces.RaceAndSexOf(_raceSexName)?.Sex ?? -1, wornArmor.Select(worn => worn.Part).ToList()), skinPattern);
    }

    private void RequestVariantParts(Gw2Composite composite, Gw2CompositeRace race, ulong variantToken)
    {
        if (race.Variants.FirstOrDefault(candidate => candidate.Token == variantToken) is not { } variant)
        {
            RequestPartResources([], null);
            return;
        }
        var componentParts = Gw2CharacterBodyParts.VariantComponentPartsOf(composite, race, variant);
        foreach (var (part, component) in componentParts)
            if (part.Type >= FirstArmorPartType)
                _armorDyes[part] = [ShiftOf(component.Color0), ShiftOf(component.Color1), ShiftOf(component.Color2), ShiftOf(component.Color3)];
        SkinColor = ShiftOf(variant.SkinColor);
        DyeColor = ShiftOf(variant.HairColor);
        SecondHairColor = ShiftOf(variant.HairColor2);
        EyeColor = ShiftOf(variant.EyeColor);
        SkinPatternColor = ShiftOf(variant.PatternColor);
        RequestSkeleton(race);
        RequestPartResources(Gw2CharacterBodyParts.VariantPartsOf(race, componentParts.Select(entry => entry.Part).ToList()), null);
    }

    private static Gw2ColorShift ShiftOf(Gw2CompositeColor color) =>
        new(color.Brightness, color.Contrast, color.Hue, color.Saturation, color.Lightness);

    private void RequestSkeleton(Gw2CompositeRace race)
    {
        if (!race.SkeletonFile.TryGetFileId(out var skeletonFileId)) return;
        _skeleton = _content.Request(skeletonFileId, (reader, fileId) => reader.ReadModelSkeleton(fileId));
        _idle = _content.Request(skeletonFileId, Gw2CharacterIdle.Decode);
        _locomotion = _content.Request(skeletonFileId, Gw2CharacterLocomotionStates.Decode);
    }

    private void RequestPartResources(IReadOnlyList<Gw2CompositeFileData> partData, Gw2CompositeSkinPattern? skinPattern)
    {
        _partData = partData;
        _parts = _partData
            .Select(part => part.MeshBase.TryGetFileId(out var meshFileId) ? meshFileId : 0u)
            .Select(meshFileId => _content.Request(meshFileId, DecodeModel))
            .ToList();
        _baseTextures = _partData.Select(part => RequestAtlasTexture(part.TextureBase)).ToList();
        _normalTextures = _partData.Select(part => RequestAtlasTexture(part.TextureNormal)).ToList();
        _dyeMasks = _partData.Select(part => RequestMask(part.MaskDye1)).ToList();
        _secondDyeMasks = _partData.Select(part => RequestMask(part.MaskDye2)).ToList();
        _thirdDyeMasks = _partData.Select(part => RequestMask(part.MaskDye3)).ToList();
        _fourthDyeMasks = _partData.Select(part => RequestMask(part.MaskDye4)).ToList();
        _cutMasks = _partData.Select(part => RequestMask(part.MaskCut)).ToList();
        _skinPatternMasks = _partData
            .Select(part => skinPattern is null ? null : RequestMask(Gw2CharacterSkinPattern.TextureFor(skinPattern, part.Type)))
            .ToList();
    }

    private void ComposeAtlas()
    {
        if (Atlas is not null || _partData is null || _partData.Count == 0 || _baseTextures is null || _composite.Result is not { } composite) return;
        if (_skeleton is { IsCompleted: false } || _idle is { IsCompleted: false } || _locomotion is { IsCompleted: false } || _colors is { IsCompleted: false }) return;
        if (_parts!.Any(part => !part.IsCompleted) || AllTextures().Any(texture => texture is { IsCompleted: false })) return;
        if (Error is not null) return;

        if (_colors?.Result is { } colors)
        {
            SkinColor = colors.Skin ?? SkinColor;
            DyeColor = colors.Dye ?? DyeColor;
            SecondHairColor = colors.SecondHair;
            UnderwearColors = colors.Underwear;
            SkinPatternColor = colors.SkinPattern ?? SkinPatternColor;
            EyeColor = colors.Eye ?? EyeColor;
        }
        var atlasSet = composite.BlitRects[(int)_partData[0].BlitRectIndex];
        var shift = HalfResolutionAtlas ? 1 : 0;
        var atlas = Gw2RgbaImage.TransparentWhite((int)atlasSet.Width >> shift, (int)atlasSet.Height >> shift);
        var normalAtlas = Gw2RgbaImage.FlatNormals(atlas.Width, atlas.Height);
        for (var index = 0; index < _partData.Count; index++)
        {
            var part = _partData[index];
            if (part.BlitRectIndex >= composite.BlitRects.Count) continue;
            var rects = Gw2CompositeAtlas.PartRects(composite.BlitRects[(int)part.BlitRectIndex], part, NoPartHideFlags, HalfResolutionAtlas);
            if (_baseTextures[index]?.Result is { } texture)
            {
                if (IsSkinPart(part))
                    Gw2CompositeAtlas.Compose(atlas, rects, texture, SkinPasses(index));
                else if (part.Type == Gw2CharacterBodyParts.HairPartType)
                    Gw2CompositeAtlas.ComposeOver(atlas, rects, texture, HairPasses(index));
                else if (part.Type >= FirstArmorPartType)
                    Gw2CompositeAtlas.ComposeOver(atlas, rects, texture, ArmorPasses(index));
                else
                    Gw2CompositeAtlas.Blit(atlas, rects, texture);
            }
            if (_normalTextures![index]?.Result is { } normals)
                Gw2CompositeAtlas.Blit(normalAtlas, rects, normals);
        }

        NormalAtlas = normalAtlas;
        Atlas = atlas;
    }

    private bool IsReplacedByAnotherPart(int partIndex)
    {
        var part = _partData![partIndex];
        if (part.Type > LastBodySkinPartType) return false;
        var regions = Gw2CompositeAtlas.Regions(part.Type, NoPartHideFlags, 0, false);
        return _partData.Where((other, otherIndex) => otherIndex != partIndex)
            .Any(other => regions.Any(region => Gw2CompositeAtlas.HidesRegion(other.HideFlags, region)));
    }

    private void Pose(float elapsedSeconds)
    {
        if (Skeleton is not { } skeleton) return;

        var idle = _idle?.Result;
        idle?.Advance(elapsedSeconds);

        SelectLocomotionState(_animator, Motion);
        _animator.Advance(elapsedSeconds, Motion.GroundSpeed);
        var pose = Gw2SkeletonPose.LocalPoseBlended(skeleton, _animator.Samples(idle?.Clip, idle?.Seconds ?? 0f));
        if (_boneConstraints is null || !ReferenceEquals(_boneConstraintSkeleton, skeleton))
        {
            _boneConstraints = new Gw2BoneConstraintSimulator(skeleton);
            _boneConstraintSkeleton = skeleton;
        }
        _boneConstraints.Wind = Wind;
        _boneConstraints.Step(pose, elapsedSeconds);
        var worlds = Gw2SkeletonPose.WorldMatricesOf(skeleton, pose);
        var palette = SkinPalette.Length == worlds.Length ? SkinPalette : new Matrix4x4[worlds.Length];
        for (var index = 0; index < worlds.Length; index++)
            palette[index] = skeleton.Bones[index].InverseWorld * worlds[index];
        SkinPalette = palette;
        PlaceAttachments(skeleton, worlds);
    }

    public bool PoseOther(Gw2CharacterAnimator animator, Gw2CharacterMotion motion, float elapsedSeconds, Matrix4x4 world, Matrix4x4[] palette)
    {
        ArgumentNullException.ThrowIfNull(animator);
        ArgumentNullException.ThrowIfNull(palette);
        if (Skeleton is not { } skeleton || palette.Length != skeleton.Bones.Count) return false;
        var idle = _idle?.Result;
        SelectLocomotionState(animator, motion);
        animator.Advance(elapsedSeconds, motion.GroundSpeed);
        var pose = Gw2SkeletonPose.LocalPoseBlended(skeleton, animator.Samples(idle?.Clip, idle?.Seconds ?? 0f));
        var worlds = Gw2SkeletonPose.WorldMatricesOf(skeleton, pose);
        var placed = Matrix4x4.CreateFromQuaternion(animator.ModelRotation) * world;
        for (var index = 0; index < worlds.Length; index++)
            palette[index] = skeleton.Bones[index].InverseWorld * worlds[index] * placed;
        return true;
    }

    private void SelectLocomotionState(Gw2CharacterAnimator animator, Gw2CharacterMotion motion)
    {
        if (_locomotion?.Result is not { } states) return;
        animator.MoveDirection = Gw2CharacterLocomotion.DirectionOf(motion.Facing, motion.MoveDirection, animator.MoveDirection);
        var stateId = motion.GroundSpeed > 0f
            ? Gw2CharacterLocomotion.MovingStateOf(motion.GroundSpeed, animator.MoveDirection, Gw2CharacterLocomotion.WalkThresholdOf(null), Gw2CharacterLocomotion.RunThresholdOf(null), states.Has)
            : Gw2CharacterLocomotion.GroundIdle;
        var state = states.Find(stateId);
        if (state is null && stateId != Gw2CharacterLocomotion.GroundIdle) return;
        animator.Play(stateId, state, states.IdleBlendInSeconds);
    }

    private void PlaceAttachments(Gw2ModelSkeleton skeleton, Matrix4x4[] worlds)
    {
        foreach (var attachment in _attachments)
        {
            var socket = Gw2ModelLink.ChildBoneIndex(skeleton, attachment.SocketBoneToken);
            if (socket < 0 || attachment.Skeleton.Result is not { } itemSkeleton) continue;
            attachment.World = Gw2ModelLink.ChildWorld(worlds[socket], itemSkeleton, attachment.GripBoneToken, Gw2ModelLinkFlags.EquipmentAttach, Matrix4x4.Identity);
        }
    }

    private Gw2CompositePass[] SkinPasses(int partIndex) =>
    [
        new(null, SkinColor),
        new(_skinPatternMasks![partIndex]?.Result, SkinPatternColor),
        new(_dyeMasks![partIndex]?.Result, DyeColor),
        new(_cutMasks![partIndex]?.Result, null),
    ];

    private Gw2CompositePass[] HairPasses(int partIndex) =>
    [
        new(_cutMasks![partIndex]?.Result, null),
        new(_dyeMasks![partIndex]?.Result, DyeColor),
        .. SecondHairColor is { } secondHairColor ? [new Gw2CompositePass(_secondDyeMasks![partIndex]?.Result, secondHairColor)] : Array.Empty<Gw2CompositePass>(),
    ];

    private Gw2CompositePass[] ArmorPasses(int partIndex) =>
    [
        new(_cutMasks![partIndex]?.Result, null),
        .. new[] { _dyeMasks, _secondDyeMasks, _thirdDyeMasks, _fourthDyeMasks }
            .Select((masks, channel) => new Gw2CompositePass(masks![partIndex]?.Result, ArmorDyeColor(_partData![partIndex], channel))),
    ];

    private Gw2ColorShift ArmorDyeColor(Gw2CompositeFileData part, int channel) =>
        channel == 0 && (part.Flags & FirstDyeIsSkinFlag) != 0 ? SkinColor
        : _armorDyes.TryGetValue(part, out var dyes) ? dyes[channel]
        : channel < UnderwearColors.Count ? UnderwearColors[channel]
        : Gw2ColorShift.Identity;

    private Gw2ContentHandle<IReadOnlyList<WornArmorPart>> RequestArmor(Gw2Composite composite, Gw2CompositeRace race) =>
        _content.Request(
            Gw2ContentName.Hash($"{_raceSexName}:armor:{Armor}"),
            (reader, _) =>
            {
                var skins = new Gw2Skins(reader.ContentIndex, SkinContentLayout.Current);
                var palettes = new Gw2ColorPalettes(reader.ContentIndex, ColorPaletteContentLayout.Current);
                var worn = new List<WornArmorPart>();
                foreach (var piece in Armor.Pieces)
                {
                    if (skins.Find(piece.SkinDataId)?.ArmorPartToken is not { } partToken) continue;
                    if (Gw2CharacterBodyParts.ArmorPartOf(composite, race, partToken) is not { } part) continue;
                    worn.Add(new WornArmorPart(part, piece.DyeDataIds.Select((dye, channel) => ArmorDyeShift(palettes, dye, MaterialKeyOf(part, channel))).ToList()));
                }
                return (IReadOnlyList<WornArmorPart>)worn;
            });

    private static uint MaterialKeyOf(Gw2CompositeFileData part, int channel) =>
        ((part.DyeFlags >> (DyeMaterialBitsPerChannel * channel)) & DyeMaterialMask) + FirstDyeMaterialKey;

    private static Gw2ColorShift ArmorDyeShift(Gw2ColorPalettes palettes, ushort dyeDataId, uint materialKey) =>
        palettes.FindColorByDataId(CharacterUnderwearDyes.Current.ColorContentType, dyeDataId, materialKey) is { } shift
            ? new Gw2ColorShift(shift.Brightness, shift.Contrast, shift.Hue, shift.Saturation, shift.Lightness)
            : Gw2ColorShift.Identity;

    private sealed record WornArmorPart(Gw2CompositeFileData Part, IReadOnlyList<Gw2ColorShift> Dyes);

    private IEnumerable<Gw2ContentHandle<Gw2RgbaImage>?> AllTextures() =>
        (_baseTextures ?? []).Concat(_normalTextures ?? []).Concat(_dyeMasks ?? []).Concat(_secondDyeMasks ?? []).Concat(_thirdDyeMasks ?? []).Concat(_fourthDyeMasks ?? []).Concat(_cutMasks ?? []).Concat(_skinPatternMasks ?? []);

    private Gw2ContentHandle<CharacterColors> RequestColors(Gw2CompositeRace race, bool hasSkinPattern) =>
        _content.Request(
            Gw2ContentName.Hash($"{_raceSexName}:{SkinColorIndex}:{DyeColorIndex}:{SecondHairColorIndex}:{(hasSkinPattern ? SkinPatternColorIndex : -1)}:{EyeColorIndex}"),
            (reader, _) =>
            {
                var palettes = new Gw2ColorPalettes(reader.ContentIndex, ColorPaletteContentLayout.Current);
                return new CharacterColors(
                    FindColor(palettes, race.SkinColorPalette, SkinColorIndex),
                    FindColor(palettes, race.HairColorPalette, DyeColorIndex),
                    hasSkinPattern ? FindColor(palettes, race.SkinPatternPalette, SkinPatternColorIndex) : null,
                    FindColor(palettes, race.EyeColorPalette, EyeColorIndex),
                    SecondHairColorIndex is { } secondHairColorIndex ? FindSecondHairColor(palettes, secondHairColorIndex) : null,
                    UnderwearColorsOf(palettes));
            });

    private IReadOnlyList<Gw2ColorShift> UnderwearColorsOf(Gw2ColorPalettes palettes)
    {
        if (Gw2CharacterRaces.RaceAndSexOf(_raceSexName) is not { } raceAndSex) return [];
        var dyes = CharacterUnderwearDyes.Current;
        return dyes.ColorsOf(raceAndSex.Race, raceAndSex.Sex)
            .Select(color => palettes.FindColorByContentKey(dyes.ColorContentType, color, PlayerDyeMaterialKey) is { } shift
                ? new Gw2ColorShift(shift.Brightness, shift.Contrast, shift.Hue, shift.Saturation, shift.Lightness)
                : Gw2ColorShift.Identity)
            .ToList();
    }

    private Gw2ColorShift? FindSecondHairColor(Gw2ColorPalettes palettes, byte index)
    {
        var dyeSetName = Appearance?.Race == Gw2AppearanceIndices.SylvariRace ? SylvariSecondHairDyeSet : SecondHairDyeSet;
        if (palettes.ColorCount(dyeSetName) is not { } count) return null;
        if (index >= count) return ClearedColorShift;
        return palettes.FindColor(dyeSetName, index, PlayerDyeMaterialKey) is { } shift
            ? new Gw2ColorShift(shift.Brightness, shift.Contrast, shift.Hue, shift.Saturation, shift.Lightness)
            : null;
    }

    private static Gw2ColorShift? FindColor(Gw2ColorPalettes palettes, string paletteName, byte index) =>
        palettes.FindColor(paletteName, index, SkinMaterialKey) is { } shift
            ? new Gw2ColorShift(shift.Brightness, shift.Contrast, shift.Hue, shift.Saturation, shift.Lightness)
            : null;

    private sealed record CharacterColors(Gw2ColorShift? Skin, Gw2ColorShift? Dye, Gw2ColorShift? SkinPattern, Gw2ColorShift? Eye, Gw2ColorShift? SecondHair, IReadOnlyList<Gw2ColorShift> Underwear);

    private Gw2ContentHandle<Gw2RgbaImage>? RequestMask(PackFileReference reference) =>
        reference.TryGetFileId(out var maskFileId) && maskFileId != 0 ? _content.Request(maskFileId, DecodeMaskImage) : null;

    private Gw2ContentHandle<Gw2RgbaImage>? RequestAtlasTexture(PackFileReference reference)
    {
        if (!reference.TryGetFileId(out var textureFileId) || textureFileId == 0) return null;
        var atlasFileId = HighResolutionFiles?.HighResolutionOf(textureFileId) ?? textureFileId;
        return _content.Request(atlasFileId, DecodeRgbaImage);
    }

    private static bool IsSkinPart(Gw2CompositeFileData part) => part.Type is <= 3 or 5 or 7;

    private static DomainModel? DecodeModel(Gw2ContentReader reader, uint fileId)
    {
        var model = reader.ReadModel(fileId);
        return model is null ? null : Gw2ModelBuilder.Build(fileId, model, _ => null);
    }

    private static Gw2RgbaImage? DecodeMaskImage(Gw2ContentReader reader, uint fileId) =>
        reader.ReadTexture(fileId)?.Texture is { } texture ? Gw2RgbaImage.FromMaskTexture(texture) : null;

    private static Gw2RgbaImage? DecodeRgbaImage(Gw2ContentReader reader, uint fileId) =>
        reader.ReadTexture(fileId)?.Texture is { } texture ? Gw2RgbaImage.FromTexture(texture) : null;
}

public sealed class CharacterAttachment(ulong socketBoneToken, ulong gripBoneToken, Gw2ContentHandle<Gw2ModelSkeleton> skeleton)
{
    public ulong SocketBoneToken { get; } = socketBoneToken;

    public ulong GripBoneToken { get; } = gripBoneToken;

    public Gw2ContentHandle<Gw2ModelSkeleton> Skeleton { get; } = skeleton;

    public Matrix4x4 World { get; internal set; } = Matrix4x4.Identity;
}
