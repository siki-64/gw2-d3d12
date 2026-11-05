using Gw2.Formats;
namespace Gw2.Dat.Content;

public sealed record Gw2CompositeColor(float Brightness, float Contrast, float Hue, float Saturation, float Lightness)
{
    public static Gw2CompositeColor Identity { get; } = new(0f, 1f, 0f, 1f, 1f);
}

public sealed record Gw2CompositeVariantComponent(
    ulong NameToken,
    Gw2CompositeColor Color0,
    Gw2CompositeColor Color1,
    Gw2CompositeColor Color2,
    Gw2CompositeColor Color3);

public sealed record Gw2CompositeFileData(
    string Name,
    uint Type,
    uint Flags,
    ulong AnimRoleOverride,
    PackFileReference MeshBase,
    PackFileReference MeshOverlap,
    PackFileReference MaskDye1,
    PackFileReference MaskDye2,
    PackFileReference MaskDye3,
    PackFileReference MaskDye4,
    PackFileReference MaskCut,
    PackFileReference TextureBase,
    PackFileReference TextureNormal,
    uint DyeFlags,
    uint HideFlags,
    uint SkinFlags,
    uint BlitRectIndex);

public sealed record Gw2CompositeSkinPattern(
    PackFileReference Chest,
    PackFileReference Face,
    PackFileReference Feet,
    PackFileReference Hands,
    PackFileReference Legs,
    PackFileReference Ears);

public sealed record Gw2CompositeSkinStyle(
    PackFileReference Chest,
    PackFileReference Feet,
    PackFileReference Hands,
    PackFileReference Legs);

public sealed record Gw2CompositeBoneScaleParam(
    ulong Name,
    byte Flags,
    float Max,
    float Min,
    IReadOnlyList<float> Rotate,
    IReadOnlyList<float> Scale,
    IReadOnlyList<float> Translate);

public sealed record Gw2CompositeBoneScaleRegion(
    ulong Name,
    float Value,
    IReadOnlyList<Gw2CompositeBoneScaleParam> Bones);

public sealed record Gw2CompositeMorphWeight(float Value, ulong Name);

public sealed record Gw2CompositeBoneScale(
    IReadOnlyList<Gw2CompositeBoneScaleRegion> BodyRegion,
    IReadOnlyList<Gw2CompositeMorphWeight> MorphWeight);

public sealed record Gw2CompositeBoneScaleFile(PackFileReference FileName);

public sealed record Gw2CompositeAnimOverride(ulong AnimRole, PackFileReference Filepath);

public sealed record Gw2CompositeVariant(
    ulong Token,
    uint BoneScaleIndex,
    IReadOnlyList<Gw2CompositeVariantComponent> Components,
    Gw2CompositeColor EyeColor,
    Gw2CompositeColor HairColor,
    Gw2CompositeColor HairColor2,
    Gw2CompositeColor PatternColor,
    Gw2CompositeColor SkinColor,
    uint SkinIndex,
    uint SkinStyle);

public sealed record Gw2CompositeRace(
    string Name,
    ulong NameToken,
    ulong BaseHeadToken,
    IReadOnlyList<ulong> Beard,
    IReadOnlyList<Gw2CompositeBoneScale> BodyBoneScales,
    IReadOnlyList<Gw2CompositeBoneScaleFile> BodyBoneScaleFiles,
    IReadOnlyList<ulong> Ears,
    string EyeColorPalette,
    IReadOnlyList<Gw2CompositeBoneScale> FaceBoneScales,
    IReadOnlyList<ulong> Faces,
    IReadOnlyList<Gw2CompositeFileData> FileData,
    uint Flags,
    IReadOnlyList<ulong> HairStyles,
    string HairColorPalette,
    PackFileReference SkeletonFile,
    IReadOnlyList<Gw2CompositeSkinPattern> SkinPatterns,
    string SkinColorPalette,
    string SkinPatternPalette,
    IReadOnlyList<Gw2CompositeSkinStyle> SkinStyles,
    uint Type,
    ulong VariantRefRace,
    IReadOnlyList<Gw2CompositeVariant> Variants,
    IReadOnlyList<Gw2CompositeAnimOverride> AnimOverrides);

public sealed record Gw2CompositeBlitRectSet(string Name, uint Width, uint Height, byte[] RectIndices, IReadOnlyList<IReadOnlyList<uint>> Rects);

public sealed class Gw2Composite
{
    public Gw2Composite(IReadOnlyList<uint> armorColorIds, ushort configVersion, IReadOnlyList<Gw2CompositeRace> races)
    {
        ArmorColorIds = armorColorIds;
        ConfigVersion = configVersion;
        Races = races;
    }

    public IReadOnlyList<uint> ArmorColorIds { get; }
    public ushort ConfigVersion { get; }
    public IReadOnlyList<Gw2CompositeRace> Races { get; }
    public IReadOnlyList<Gw2CompositeBlitRectSet> BlitRects { get; init; } = [];
    public IReadOnlyList<Gw2CompositeBoneScale> BoneScaleSets { get; init; } = [];

    public static Gw2Composite? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.FindRoot("PackComposite");
        if (root is null) return null;

        var races = new List<Gw2CompositeRace>();
        if (root.Fields.GetValueOrDefault("raceSexData") is object?[] raceObjects)
            foreach (var raceObject in raceObjects)
                if (raceObject is PackObject race)
                    races.Add(new(
                        Str(race, "name"),
                        PackValue.AsU64(race.Fields.GetValueOrDefault("nameToken")),
                        PackValue.AsU64(race.Fields.GetValueOrDefault("baseHeadToken")),
                        PackValue.AsU64List(race.Fields.GetValueOrDefault("beard")),
                        BoneScales(race.Fields.GetValueOrDefault("bodyBoneScales")),
                        BoneScaleFiles(race.Fields.GetValueOrDefault("bodyBoneScaleFiles")),
                        PackValue.AsU64List(race.Fields.GetValueOrDefault("ears")),
                        Str(race, "eyeColorPalette"),
                        BoneScales(race.Fields.GetValueOrDefault("faceBoneScales")),
                        PackValue.AsU64List(race.Fields.GetValueOrDefault("faces")),
                        FileDataList(race.Fields.GetValueOrDefault("fileData")),
                        PackValue.AsU32(race.Fields.GetValueOrDefault("flags")),
                        PackValue.AsU64List(race.Fields.GetValueOrDefault("hairStyles")),
                        Str(race, "hairColorPalette"),
                        Ref(race, "skeletonFile"),
                        SkinPatterns(race.Fields.GetValueOrDefault("skinPatterns")),
                        Str(race, "skinColorPalette"),
                        Str(race, "skinPatternPalette"),
                        SkinStyles(race.Fields.GetValueOrDefault("skinStyles")),
                        PackValue.AsU32(race.Fields.GetValueOrDefault("type")),
                        PackValue.AsU64(race.Fields.GetValueOrDefault("variantRefRace")),
                        Variants(race.Fields.GetValueOrDefault("variants")),
                        AnimOverrides(race.Fields.GetValueOrDefault("animOverrides"))));

        return new(
            PackValue.AsU32List(root.Fields.GetValueOrDefault("armorColorIds")),
            (ushort)PackValue.AsU32(root.Fields.GetValueOrDefault("configVersion")),
            races)
        {
            BlitRects = PackValue.AsObjects(root.Fields.GetValueOrDefault("blitRects"))
                .Select(set =>
                {
                    var size = PackValue.AsU32List(set.Fields.GetValueOrDefault("size"));
                    return new Gw2CompositeBlitRectSet(
                        Str(set, "name"),
                        size.Count > 0 ? size[0] : 0,
                        size.Count > 1 ? size[1] : 0,
                        PackValue.AsBytes(set.Fields.GetValueOrDefault("rectIndex")),
                        set.Fields.GetValueOrDefault("rectArray") is object?[] rects ? rects.Select(PackValue.AsU32List).ToArray() : []);
                })
                .ToArray(),
            BoneScaleSets = BoneScales(root.Fields.GetValueOrDefault("boneScales")),
        };
    }

    private static IReadOnlyList<Gw2CompositeFileData> FileDataList(object? value)
    {
        if (value is not object?[] values) return [];
        var result = new List<Gw2CompositeFileData>(values.Length);
        foreach (var item in values)
            if (item is PackObject entry)
                result.Add(FileDataEntry(entry));
        return result;
    }

    private static Gw2CompositeFileData FileDataEntry(PackObject o) => new(
        Token64.Decode(PackValue.AsU64(o.Fields.GetValueOrDefault("name"))),
        PackValue.AsU32(o.Fields.GetValueOrDefault("type")),
        PackValue.AsU32(o.Fields.GetValueOrDefault("flags")),
        PackValue.AsU64(o.Fields.GetValueOrDefault("animRoleOverride")),
        Ref(o, "meshBase"),
        Ref(o, "meshOverlap"),
        Ref(o, "maskDye1"),
        Ref(o, "maskDye2"),
        Ref(o, "maskDye3"),
        Ref(o, "maskDye4"),
        Ref(o, "maskCut"),
        Ref(o, "textureBase"),
        Ref(o, "textureNormal"),
        PackValue.AsU32(o.Fields.GetValueOrDefault("dyeFlags")),
        PackValue.AsU32(o.Fields.GetValueOrDefault("hideFlags")),
        PackValue.AsU32(o.Fields.GetValueOrDefault("skinFlags")),
        PackValue.AsU32(o.Fields.GetValueOrDefault("blitRectIndex")));

    private static IReadOnlyList<Gw2CompositeSkinPattern> SkinPatterns(object? value)
    {
        if (value is not object?[] values) return [];
        var result = new List<Gw2CompositeSkinPattern>(values.Length);
        foreach (var item in values)
            if (item is PackObject o)
                result.Add(new(
                    Ref(o, "chest"), Ref(o, "face"), Ref(o, "feet"), Ref(o, "hands"), Ref(o, "legs"), Ref(o, "ears")));
        return result;
    }

    private static IReadOnlyList<Gw2CompositeSkinStyle> SkinStyles(object? value)
    {
        if (value is not object?[] values) return [];
        var result = new List<Gw2CompositeSkinStyle>(values.Length);
        foreach (var item in values)
            if (item is PackObject o)
                result.Add(new(Ref(o, "chest"), Ref(o, "feet"), Ref(o, "hands"), Ref(o, "legs")));
        return result;
    }

    private static IReadOnlyList<Gw2CompositeBoneScaleFile> BoneScaleFiles(object? value)
    {
        if (value is not object?[] values) return [];
        var result = new List<Gw2CompositeBoneScaleFile>(values.Length);
        foreach (var item in values)
            if (item is PackObject o)
                result.Add(new(Ref(o, "fileName")));
        return result;
    }

    private static IReadOnlyList<Gw2CompositeBoneScale> BoneScales(object? value)
    {
        if (value is not object?[] values) return [];
        var result = new List<Gw2CompositeBoneScale>(values.Length);
        foreach (var item in values)
            if (item is PackObject o)
                result.Add(BoneScale(o));
        return result;
    }

    internal static Gw2CompositeBoneScale BoneScale(PackObject o) =>
        new(BoneScaleRegions(o.Fields.GetValueOrDefault("BodyRegion")), MorphWeights(o.Fields.GetValueOrDefault("MorphWeight")));

    private static IReadOnlyList<Gw2CompositeBoneScaleRegion> BoneScaleRegions(object? value)
    {
        if (value is not object?[] values) return [];
        var result = new List<Gw2CompositeBoneScaleRegion>(values.Length);
        foreach (var item in values)
            if (item is PackObject o)
                result.Add(new(
                    PackValue.AsU64(o.Fields.GetValueOrDefault("name")),
                    F32(o.Fields.GetValueOrDefault("value")),
                    BoneScaleParams(o.Fields.GetValueOrDefault("Bone"))));
        return result;
    }

    private static IReadOnlyList<Gw2CompositeBoneScaleParam> BoneScaleParams(object? value)
    {
        if (value is not object?[] values) return [];
        var result = new List<Gw2CompositeBoneScaleParam>(values.Length);
        foreach (var item in values)
            if (item is PackObject o)
                result.Add(new(
                    PackValue.AsU64(o.Fields.GetValueOrDefault("name")),
                    (byte)PackValue.AsU32(o.Fields.GetValueOrDefault("flags")),
                    F32(o.Fields.GetValueOrDefault("max")),
                    F32(o.Fields.GetValueOrDefault("min")),
                    PackValue.AsFloats(o.Fields.GetValueOrDefault("rotate")),
                    PackValue.AsFloats(o.Fields.GetValueOrDefault("scale")),
                    PackValue.AsFloats(o.Fields.GetValueOrDefault("translate"))));
        return result;
    }

    private static IReadOnlyList<Gw2CompositeMorphWeight> MorphWeights(object? value)
    {
        if (value is not object?[] values) return [];
        var result = new List<Gw2CompositeMorphWeight>(values.Length);
        foreach (var item in values)
            if (item is PackObject o)
                result.Add(new(F32(o.Fields.GetValueOrDefault("value")), PackValue.AsU64(o.Fields.GetValueOrDefault("name"))));
        return result;
    }

    private static IReadOnlyList<Gw2CompositeAnimOverride> AnimOverrides(object? value)
    {
        if (value is not object?[] values) return [];
        var result = new List<Gw2CompositeAnimOverride>(values.Length);
        foreach (var item in values)
            if (item is PackObject o)
                result.Add(new(PackValue.AsU64(o.Fields.GetValueOrDefault("animRole")), Ref(o, "filepath")));
        return result;
    }

    private static IReadOnlyList<Gw2CompositeVariant> Variants(object? value)
    {
        if (value is not object?[] values) return [];
        var result = new List<Gw2CompositeVariant>(values.Length);
        foreach (var item in values)
            if (item is PackObject variant)
                result.Add(new(
                    PackValue.AsU64(variant.Fields.GetValueOrDefault("token")),
                    PackValue.AsU32(variant.Fields.GetValueOrDefault("boneScaleIndex")),
                    Components(variant.Fields.GetValueOrDefault("components")),
                    Color(variant.Fields.GetValueOrDefault("eyeColor")),
                    Color(variant.Fields.GetValueOrDefault("hairColor")),
                    Color(variant.Fields.GetValueOrDefault("hairColor2")),
                    Color(variant.Fields.GetValueOrDefault("patternColor")),
                    Color(variant.Fields.GetValueOrDefault("skinColor")),
                    PackValue.AsU32(variant.Fields.GetValueOrDefault("skinIndex")),
                    PackValue.AsU32(variant.Fields.GetValueOrDefault("skinStyle"))));
        return result;
    }

    private static IReadOnlyList<Gw2CompositeVariantComponent> Components(object? value)
    {
        if (value is not object?[] values) return [];
        var result = new List<Gw2CompositeVariantComponent>(values.Length);
        foreach (var item in values)
            if (item is PackObject o)
                result.Add(new(
                    PackValue.AsU64(o.Fields.GetValueOrDefault("nameToken")),
                    Color(o.Fields.GetValueOrDefault("color0")),
                    Color(o.Fields.GetValueOrDefault("color1")),
                    Color(o.Fields.GetValueOrDefault("color2")),
                    Color(o.Fields.GetValueOrDefault("color3"))));
        return result;
    }

    private const float ChannelCenter = 128f;
    private const float ChannelScale = 1f / 128f;
    private const float HueStepsPerTurn = 256f;

    private static Gw2CompositeColor Color(object? value) =>
        value is PackObject o
            ? new((Channel(o, "brightness") - ChannelCenter) * ChannelScale, Channel(o, "contrast") * ChannelScale,
                Channel(o, "hue") * MathF.Tau / HueStepsPerTurn, Channel(o, "saturation") * ChannelScale,
                Channel(o, "lightness") * ChannelScale)
            : Gw2CompositeColor.Identity;

    private static float Channel(PackObject o, string name) => o.Fields.GetValueOrDefault(name) is byte channel ? channel : 0f;

    private static string Str(PackObject o, string name) => o.Fields.GetValueOrDefault(name) as string ?? string.Empty;

    private static PackFileReference Ref(PackObject o, string name) =>
        o.Fields.GetValueOrDefault(name) is PackFileReference reference ? reference : default;

    private static float F32(object? value) => value switch
    {
        float single => single,
        double wide => (float)wide,
        _ => 0,
    };
}
