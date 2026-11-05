using Gw2.Formats;
using System.Linq;
using System.Numerics;

using static Gw2.Formats.PackValue;

namespace Gw2.Dat.Content;

public sealed record Gw2MapWaterSurfaceInfo(ulong Guid, uint Flags, float SurfaceZ, IReadOnlyList<Vector2> Vertices);

public sealed class Gw2MapWater
{
    public Gw2MapWater(uint flags, float planeZ, IReadOnlyList<Gw2MapWaterSurfaceInfo> surfaces)
    {
        Flags = flags;
        PlaneZ = planeZ;
        Surfaces = surfaces;
    }

    public uint Flags { get; }
    public float PlaneZ { get; }
    public IReadOnlyList<Gw2MapWaterSurfaceInfo> Surfaces { get; }

    public static Gw2MapWater? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.FindRoot("PackMapWater");
        if (root is null) return null;

        var surfaces = new List<Gw2MapWaterSurfaceInfo>();
        if (root.Fields.GetValueOrDefault("waterSurfaces") is object?[] surfaceObjects)
        {
            foreach (var surfaceObject in surfaceObjects)
            {
                if (surfaceObject is not PackObject surface) continue;
                surfaces.Add(new(
                    AsU64(surface.Fields.GetValueOrDefault("guid")),
                    AsU32(surface.Fields.GetValueOrDefault("waterSurfaceFlags")),
                    AsF32(surface.Fields.GetValueOrDefault("waterSurfaceZ")),
                    PackValue.AsVec2List(surface.Fields.GetValueOrDefault("vertices"))));
            }
        }

        return new(
            AsU32(root.Fields.GetValueOrDefault("waterFlags")),
            AsF32(root.Fields.GetValueOrDefault("waterPlaneZ")),
            surfaces);
    }
}

public sealed record Gw2MapEnvWaterInfo(
    uint Flags,
    float AnimAmplitude,
    float AnimChoppiness,
    Vector2 AnimWind,
    float BumpAmount,
    float[] BumpAngles,
    float[] BumpScales,
    float[] BumpSpeeds,
    float[] BumpTiles,
    float PatternAngle,
    float PatternTile,
    float PatternSpeed,
    float PatternEdge,
    byte[] ShallowColor,
    byte[] DeepColor,
    byte[] PatternColor,
    float SurfaceFresnel,
    float DistortAmount,
    float DepthAttenuation,
    PackFileReference MaterialFile,
    IReadOnlyList<PackFileReference?> TextureFiles,
    IReadOnlyList<uint> ConstantTokens,
    IReadOnlyList<float[]> ConstantValues,
    float FoamSpawn,
    float FoamDissolve,
    float FoamDepthAttenuation,
    byte[] FoamColor0,
    byte[] FoamColor1);

public sealed record Gw2MapLightInfo(byte[] Color, float Intensity, Vector3 Direction);

public sealed record Gw2MapHazeInfo(
    byte[] NearColor,
    byte[] FarColor,
    float[] DistanceRange,
    float DepthCue,
    byte[] HeightColor,
    float[] HeightRange,
    float[] SunDirectionRange);

public sealed record Gw2MapEnvSkyTex(PackFileReference NorthEast, PackFileReference SouthWest, PackFileReference Top);

public sealed record Gw2MapEnvSkyCubeTex(
    PackFileReference Bottom,
    PackFileReference East,
    PackFileReference North,
    PackFileReference South,
    PackFileReference Top,
    PackFileReference West);

public sealed record Gw2MapEnvSkyPhase(
    float Brightness,
    float HazeBottom,
    float HazeDensity,
    float HazeFalloff,
    float LightIntensity,
    float StarDensity);

public sealed record Gw2MapEnvSkyDome(byte Flags, Gw2MapEnvSkyPhase Day, Gw2MapEnvSkyPhase Night, float VerticalOffset);

public sealed record Gw2MapEnvSky(PackFileReference StarFile, IReadOnlyList<Gw2MapEnvSkyTex> ModeTextures)
{
    public Gw2MapEnvSkyDome? Dome { get; init; }
    public IReadOnlyList<Gw2MapEnvSkyCubeTex> ModeCubeTextures { get; init; } = [];
}

public sealed record Gw2MapEnvEffect(float Saturation, float TintAmount, uint Flags, PackFileReference ClutTexture)
{
    public byte[] TintColor { get; init; } = [];
    public byte[] TintTargetColor { get; init; } = [];
    public float TintFocus { get; init; }
    public byte[] Glow { get; init; } = [];
    public byte[] GlowLevel { get; init; } = [];
    public float GlowAmplify { get; init; }
    public float FocalDepth { get; init; }
    public float FocalRange { get; init; }
    public float SsaoAmount { get; init; }
    public float SsaoBrighten { get; init; }
    public float SsaoContrast { get; init; }
    public float SsaoSunScale { get; init; }
}

public sealed record Gw2MapEnvShape(
    Vector3 Center,
    float Height,
    float FadeHorizInner,
    float FadeHorizOuter,
    float FadeVertical,
    IReadOnlyList<Vector2> Vertices,
    byte ShapeType);

public sealed record Gw2MapEnvZone(
    string Name,
    uint Type,
    ulong Guid,
    uint Flags,
    IReadOnlyList<Gw2MapEnvShape> Shapes,
    IReadOnlyList<Gw2MapLightInfo> Lights,
    Gw2MapHazeInfo? Haze)
{
    public Gw2MapEnvironment? Data { get; init; }
}

public sealed record Gw2MapEnvOverride(ulong Token, ulong Guid, Gw2MapEnvironment Data);

public sealed record Gw2MapEnvCloudLayerAttributes(
    float Brightness,
    float Density,
    float Haze,
    float LightIntensity,
    Vector2 Velocity,
    float FadeWidth,
    float FadeEnd);

public sealed record Gw2MapEnvCloudLayer(
    string Name,
    float Altitude,
    float CutOut,
    float Depth,
    float Extent,
    float Scale,
    PackFileReference Texture,
    IReadOnlyList<Gw2MapEnvCloudLayerAttributes> Attributes);

public sealed record Gw2MapEnvColoredLightRings(
    Vector2 Range,
    IReadOnlyList<float> Distances,
    IReadOnlyList<byte[]> LightColors,
    IReadOnlyList<byte[]> ShadowColors);

public sealed record Gw2MapEnvSpawnModel(
    uint SpawnRangeMin,
    uint SpawnRangeMax,
    uint LifeSpanMin,
    uint LifeSpanMax,
    Vector2 ScaleRange,
    Vector2 HeightRange,
    Vector2 RotationXRange,
    Vector2 RotationYRange,
    Vector2 RotationZRange,
    float Probability,
    float Delay,
    uint Flags,
    ulong AnimSequence,
    PackFileReference ModelFile,
    uint MaxConcurrent);

public sealed record Gw2MapEnvSpawns(
    IReadOnlyList<IReadOnlyList<Gw2MapEnvSpawnModel>> Groups,
    IReadOnlyList<ulong> Targets);

public sealed record Gw2MapEnvCharacterLighting(
    float SunScale,
    float Saturation,
    float SunFill,
    float AmbientScale,
    float AmbientFill,
    byte Flags);

public sealed record Gw2MapEnvAmbience(
    IReadOnlyList<Gw2MapEnvCloudLayer> CloudLayers,
    Gw2MapEnvColoredLightRings? ColoredLightRings,
    Gw2MapEnvSpawns? Spawns,
    ulong AudioToken,
    byte[] NightMods,
    ulong BindTarget,
    uint Flags,
    IReadOnlyList<IReadOnlyList<Gw2MapEnvCharacterLighting>> CharacterLightingGroups);

public sealed record Gw2MapEnvParticleField(
    string Name,
    byte Type,
    uint Flags,
    uint Seed,
    uint ParticleCount,
    uint ClusterCount,
    uint Extent,
    float Altitude,
    float Depth,
    float Deviation,
    float Fade,
    Vector3 FieldDirection,
    Vector2 Angle,
    Vector2 Clustering,
    Vector2 DeviationSpeed,
    Vector2 Lifetime,
    Vector2 Opacity,
    Vector2 Period,
    Vector2 Rotation,
    Vector2 ScaleX,
    Vector2 ScaleY,
    Vector2 Speed,
    uint TextureColumns,
    uint TextureRows,
    float TextureFps,
    PackFileReference Texture);

public sealed record Gw2MapEnvParticleFieldCutout(string Name, float[] TransformRowX, float[] TransformRowY, float[] TransformRowZ);

public sealed record Gw2MapEnvWind(
    byte Azimuth,
    byte Elevation,
    byte Noise,
    byte Speed,
    byte Gust,
    byte GustFreq)
{
    public byte GustSpeed { get; init; }
}

public sealed record Gw2MapEnvExposure(
    float BrightTime,
    float DimTime,
    float DarkCoeff,
    float DarkExp,
    float DarkMin,
    float DarkMax,
    float BrightMin,
    float BrightMax,
    float BrightScale,
    float DarkScale,
    float[] WaterReflectionParams);

public sealed record Gw2MapBacklight(
    float ShadowInfluence,
    byte[] Color,
    float Intensity);

public sealed record Gw2MapEnvShaderConstant(uint Token, float[] Value);

public sealed record Gw2MapEnvShaderTexture(PackFileReference File, float[] TextureUV);

public sealed record Gw2MapEnvSkyCardAnimation(
    Vector2 Translation,
    Vector2 ScaleRangeX,
    Vector2 ScaleRangeY,
    Vector2 ScaleSpeed,
    float Rotation,
    uint TexCoords);

public sealed record Gw2MapEnvSkyCardFlipbook(uint Rows, uint Columns, uint Start, uint Count, float Fps);

public sealed record Gw2MapEnvSkyCardMaterial(
    PackFileReference MaterialFile,
    IReadOnlyList<Gw2MapEnvShaderConstant> Constants,
    IReadOnlyList<Gw2MapEnvShaderTexture> Textures,
    Gw2MapEnvSkyCardAnimation? TextureAnimation,
    Gw2MapEnvSkyCardFlipbook? Flipbook);

public sealed record Gw2MapEnvLensFlareAtom(
    uint Rows,
    uint Columns,
    uint Start,
    float Fps,
    byte[] Color,
    Vector2 Offset,
    Vector2 Scale,
    float BaseRotation,
    float CameraRotation,
    byte Flags);

public sealed record Gw2MapEnvLensFlare(
    IReadOnlyList<Gw2MapEnvLensFlareAtom> Atoms,
    IReadOnlyList<PackFileReference> Textures,
    PackFileReference MaterialFile,
    Vector2 FadeBand,
    Vector2 ReverseFadeBand,
    float OpacityCoeff,
    byte Flags,
    IReadOnlyList<Gw2MapEnvShaderConstant> Constants);

public sealed record Gw2MapEnvSkyCardAttributes(
    float Azimuth,
    float Density,
    float HazeDensity,
    float Latitude,
    float LightIntensity,
    float MinHaze,
    Vector2 Scale,
    float Speed,
    PackFileReference Texture,
    float[] TextureUV,
    float Brightness,
    byte Flags,
    Gw2MapEnvLensFlare? LensFlare);

public sealed record Gw2MapEnvSkyCard(
    string Name,
    uint Flags,
    Vector3 Location,
    Gw2MapEnvSkyCardAttributes? Day,
    Gw2MapEnvSkyCardAttributes? Night,
    Gw2MapEnvSkyCardMaterial? Material);

public sealed class Gw2MapEnvironment
{
    public Gw2MapEnvironment(
        IReadOnlyList<Gw2MapLightInfo> lights,
        Gw2MapHazeInfo? haze,
        Gw2MapEnvSky? sky,
        Gw2MapEnvEffect? effect,
        Gw2MapEnvWind? wind,
        IReadOnlyList<Gw2MapEnvZone> zones,
        Gw2MapEnvWaterInfo? water = null,
        Gw2MapBacklight? backlight = null)
    {
        Backlight = backlight;
        Lights = lights;
        Haze = haze;
        Sky = sky;
        Effect = effect;
        Wind = wind;
        Zones = zones;
        Water = water;
    }

    public IReadOnlyList<Gw2MapLightInfo> Lights { get; }
    public IReadOnlyList<Gw2MapLightInfo> NightLights { get; init; } = [];
    public Gw2MapHazeInfo? NightHaze { get; init; }

    public Gw2MapEnvEffect? NightEffect { get; init; }
    public IReadOnlyList<Gw2MapLightInfo> UnderwaterLights { get; init; } = [];
    public Gw2MapBacklight? NightBacklight { get; init; }
    public Gw2MapHazeInfo? Haze { get; }
    public Gw2MapEnvSky? Sky { get; }
    public Gw2MapEnvEffect? Effect { get; }
    public Gw2MapEnvWind? Wind { get; }
    public IReadOnlyList<Gw2MapEnvZone> Zones { get; }
    public Gw2MapEnvWaterInfo? Water { get; }
    public Gw2MapEnvWaterInfo? NightWater { get; init; }
    public Gw2MapEnvWaterInfo? UnderwaterWater { get; init; }
    public Gw2MapBacklight? Backlight { get; }
    public Gw2MapEnvExposure? Exposure { get; init; }
    public IReadOnlyList<Gw2MapEnvSkyCard> SkyCards { get; init; } = [];
    public IReadOnlyList<Gw2MapEnvParticleField> ParticleFields { get; init; } = [];
    public IReadOnlyList<Gw2MapEnvParticleFieldCutout> ParticleFieldCutouts { get; init; } = [];
    public Gw2MapEnvAmbience? Ambience { get; init; }
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<Gw2MapEnvOverride> Overrides { get; init; } = [];

    public static Gw2MapEnvironment? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.FindRoot("PackMapEnvironment");
        if (root is null) return null;

        var global = root.Fields.GetValueOrDefault("dataGlobal") as PackObject;

        var zones = new List<Gw2MapEnvZone>();
        if (root.Fields.GetValueOrDefault("dataLocalArray") is object?[] zoneObjects)
            foreach (var zoneObject in zoneObjects)
                if (zoneObject is PackObject zone)
                    zones.Add(new(
                        zone.Fields.GetValueOrDefault("name") as string ?? string.Empty,
                        PackValue.AsU32(zone.Fields.GetValueOrDefault("type")),
                        PackValue.AsU64(zone.Fields.GetValueOrDefault("guid")),
                        PackValue.AsU32(zone.Fields.GetValueOrDefault("flags")),
                        ParseShapes(zone.Fields.GetValueOrDefault("shapeArray")),
                        ParseLights(zone.Fields.GetValueOrDefault("lighting")),
                        ParseHaze(zone.Fields.GetValueOrDefault("haze")))
                    {
                        Data = ParseData(zone, [], []),
                    });

        var overrides = PackValue.AsObjects(root.Fields.GetValueOrDefault("dataOverrideArray"))
            .Select(dataOverride => new Gw2MapEnvOverride(
                PackValue.RequireU64(dataOverride, "token"),
                PackValue.RequireU64(dataOverride, "guid"),
                ParseData(dataOverride, [], [])))
            .ToArray();

        return ParseData(global, zones, overrides);
    }

    private static Gw2MapEnvironment ParseData(
        PackObject? data,
        IReadOnlyList<Gw2MapEnvZone> zones,
        IReadOnlyList<Gw2MapEnvOverride> overrides) =>
        new(
            ParseLights(data?.Fields.GetValueOrDefault("lighting")),
            ParseHaze(data?.Fields.GetValueOrDefault("haze")),
            ParseSky(data),
            ParseEffect(data?.Fields.GetValueOrDefault("effect")),
            ParseWind(data?.Fields.GetValueOrDefault("wind")),
            zones,
            ParseWater(data?.Fields.GetValueOrDefault("water")),
            ParseBacklight(data?.Fields.GetValueOrDefault("lighting")))
        {
            Name = data?.Fields.GetValueOrDefault("name") as string ?? string.Empty,
            NightLights = ParseLights(data?.Fields.GetValueOrDefault("lighting"), NightLightingGroup),
            NightHaze = ParseHaze(data?.Fields.GetValueOrDefault("haze"), NightLightingGroup),
            NightEffect = ParseEffect(data?.Fields.GetValueOrDefault("effect"), NightLightingGroup),
            UnderwaterLights = ParseLights(data?.Fields.GetValueOrDefault("lighting"), UnderwaterLightingGroup),
            NightWater = ParseWater(data?.Fields.GetValueOrDefault("water"), NightWaterEntry),
            UnderwaterWater = ParseWater(data?.Fields.GetValueOrDefault("water"), UnderwaterWaterEntry),
            NightBacklight = ParseBacklight(data?.Fields.GetValueOrDefault("lighting"), NightLightingGroup),
            Exposure = ParseExposure(data?.Fields.GetValueOrDefault("ext")),
            SkyCards = ParseSkyCards(data?.Fields.GetValueOrDefault("skyCards")),
            ParticleFields = ParseParticleFields(data?.Fields.GetValueOrDefault("particleFields")),
            ParticleFieldCutouts = ParseParticleFieldCutouts(data?.Fields.GetValueOrDefault("particleFieldCutouts")),
            Ambience = data is null ? null : ParseAmbience(data),
            Overrides = overrides,
        };

    private static Gw2MapEnvAmbience ParseAmbience(PackObject data) =>
        new(
            ParseCloudLayers(data.Fields.GetValueOrDefault("clouds")),
            ParseColoredLightRings(data.Fields.GetValueOrDefault("coloredLightRings")),
            ParseSpawns(data.Fields.GetValueOrDefault("spawns")),
            Embedded(data.Fields.GetValueOrDefault("audio")) is { } audio ? PackValue.RequireU64(audio, "token") : 0,
            PackValue.AsBytes(data.Fields.GetValueOrDefault("nightMods")),
            PackValue.RequireU64(data, "bindTarget"),
            PackValue.RequireU32(data, "flags"),
            PackValue.AsObjects(data.Fields.GetValueOrDefault("lightingCharGroups"))
                .Select(ParseCharacterLightingGroup)
                .ToArray());

    private static IReadOnlyList<Gw2MapEnvCharacterLighting> ParseCharacterLightingGroup(PackObject group) =>
        PackValue.AsObjects(group.Fields.GetValueOrDefault("lightingChar"))
            .Select(lighting => new Gw2MapEnvCharacterLighting(
                PackValue.RequireF32(lighting, "sunScale"),
                PackValue.RequireF32(lighting, "saturation"),
                PackValue.RequireF32(lighting, "sunFill"),
                PackValue.RequireF32(lighting, "ambScale"),
                PackValue.RequireF32(lighting, "ambFill"),
                (byte)PackValue.RequireU32(lighting, "flags")))
            .ToArray();

    private static IReadOnlyList<Gw2MapEnvCloudLayer> ParseCloudLayers(object? cloudsValue)
    {
        if (Embedded(cloudsValue) is not { } clouds) return [];
        return PackValue.AsObjects(clouds.Fields.GetValueOrDefault("layers")).Select(layer => new Gw2MapEnvCloudLayer(
            layer.Fields.GetValueOrDefault("name") as string ?? string.Empty,
            PackValue.RequireF32(layer, "altitude"),
            PackValue.RequireF32(layer, "cutOut"),
            PackValue.RequireF32(layer, "depth"),
            PackValue.RequireF32(layer, "extent"),
            PackValue.RequireF32(layer, "scale"),
            FileRef(layer, "texture"),
            PackValue.AsObjects(layer.Fields.GetValueOrDefault("attributes")).Select(attributes => new Gw2MapEnvCloudLayerAttributes(
                PackValue.RequireF32(attributes, "brightness"),
                PackValue.RequireF32(attributes, "density"),
                PackValue.RequireF32(attributes, "haze"),
                PackValue.RequireF32(attributes, "lightIntensity"),
                Vec2(attributes, "velocity"),
                PackValue.RequireF32(attributes, "fadeWidth"),
                PackValue.RequireF32(attributes, "fadeEnd"))).ToArray())).ToArray();
    }

    private static Gw2MapEnvColoredLightRings? ParseColoredLightRings(object? ringsValue)
    {
        if (Embedded(ringsValue) is not { } rings) return null;
        return new(
            Vec2(rings, "range"),
            rings.Fields.GetValueOrDefault("distances") is object?[] distances ? distances.Select(PackValue.AsF32).ToArray() : [],
            ColorList(rings, "lightColors"),
            ColorList(rings, "shadowColors"));
    }

    private static IReadOnlyList<byte[]> ColorList(PackObject obj, string name) =>
        obj.Fields.GetValueOrDefault(name) is object?[] colors ? colors.Select(PackValue.AsBytes).ToArray() : [];

    private static Gw2MapEnvSpawns? ParseSpawns(object? spawnsValue)
    {
        if (Embedded(spawnsValue) is not { } spawns) return null;
        return new(
            PackValue.AsObjects(spawns.Fields.GetValueOrDefault("spawnGroups"))
                .Select(group => PackValue.AsObjects(group.Fields.GetValueOrDefault("spawns")).Select(ParseSpawnModel).ToArray())
                .ToArray(),
            PackValue.AsU64List(spawns.Fields.GetValueOrDefault("targets")));
    }

    private static Gw2MapEnvSpawnModel ParseSpawnModel(PackObject spawn)
    {
        var spawnRange = spawn.Fields.GetValueOrDefault("spawnRange") as uint[] ?? [0, 0];
        var lifeSpan = spawn.Fields.GetValueOrDefault("lifeSpan") as uint[] ?? [0, 0];
        return new(
            spawnRange[0],
            spawnRange[1],
            lifeSpan[0],
            lifeSpan[1],
            Vec2(spawn, "scaleRange"),
            Vec2(spawn, "heightRange"),
            Vec2(spawn, "rotXRange"),
            Vec2(spawn, "rotYRange"),
            Vec2(spawn, "rotZRange"),
            PackValue.RequireF32(spawn, "probability"),
            PackValue.RequireF32(spawn, "delay"),
            PackValue.RequireU32(spawn, "flags"),
            PackValue.RequireU64(spawn, "animSequence"),
            FileRef(spawn, "modelFile"),
            PackValue.RequireU32(spawn, "maxConcurrent"));
    }

    private static IReadOnlyList<Gw2MapEnvParticleField> ParseParticleFields(object? particleFieldsValue) =>
        PackValue.AsObjects(particleFieldsValue).Select(field =>
        {
            var textureColumnsRows = field.Fields.GetValueOrDefault("texColRow") as uint[] ?? [0, 0];
            return new Gw2MapEnvParticleField(
                field.Fields.GetValueOrDefault("name") as string ?? string.Empty,
                (byte)PackValue.RequireU32(field, "type"),
                PackValue.RequireU32(field, "flags"),
                PackValue.RequireU32(field, "seed"),
                PackValue.RequireU32(field, "particleCount"),
                PackValue.RequireU32(field, "clusterCount"),
                PackValue.RequireU32(field, "extent"),
                PackValue.RequireF32(field, "altitude"),
                PackValue.RequireF32(field, "depth"),
                PackValue.RequireF32(field, "deviation"),
                PackValue.RequireF32(field, "fade"),
                Vec3(field, "fieldDirection"),
                Vec2(field, "angle"),
                Vec2(field, "clustering"),
                Vec2(field, "deviationSpeed"),
                Vec2(field, "lifetime"),
                Vec2(field, "opacity"),
                Vec2(field, "period"),
                Vec2(field, "rotation"),
                Vec2(field, "scaleX"),
                Vec2(field, "scaleY"),
                Vec2(field, "speed"),
                textureColumnsRows[0],
                textureColumnsRows[1],
                PackValue.RequireF32(field, "texFPS"),
                FileRef(field, "texPath"));
        }).ToArray();

    private static IReadOnlyList<Gw2MapEnvParticleFieldCutout> ParseParticleFieldCutouts(object? cutoutsValue) =>
        PackValue.AsObjects(cutoutsValue).Select(cutout => new Gw2MapEnvParticleFieldCutout(
            cutout.Fields.GetValueOrDefault("name") as string ?? string.Empty,
            PackValue.AsFloats(cutout.Fields.GetValueOrDefault("x")),
            PackValue.AsFloats(cutout.Fields.GetValueOrDefault("y")),
            PackValue.AsFloats(cutout.Fields.GetValueOrDefault("z")))).ToArray();

    private static IReadOnlyList<Gw2MapEnvSkyCard> ParseSkyCards(object? skyCardsValue)
    {
        if (Embedded(skyCardsValue) is not { } skyCards) return [];
        var cards = new List<Gw2MapEnvSkyCard>();
        foreach (var card in PackValue.AsObjects(skyCards.Fields.GetValueOrDefault("cards")))
            cards.Add(new(
                card.Fields.GetValueOrDefault("name") as string ?? string.Empty,
                PackValue.RequireU32(card, "flags"),
                Vec3(card, "location"),
                ParseSkyCardAttributes(card.Fields.GetValueOrDefault("day")),
                ParseSkyCardAttributes(card.Fields.GetValueOrDefault("night")),
                ParseSkyCardMaterial(card.Fields.GetValueOrDefault("material"))));
        return cards;
    }

    private static Gw2MapEnvSkyCardAttributes? ParseSkyCardAttributes(object? attributesValue)
    {
        if (Embedded(attributesValue) is not { } attributes) return null;
        return new(
            PackValue.RequireF32(attributes, "azimuth"),
            PackValue.RequireF32(attributes, "density"),
            PackValue.RequireF32(attributes, "hazeDensity"),
            PackValue.RequireF32(attributes, "latitude"),
            PackValue.RequireF32(attributes, "lightIntensity"),
            PackValue.RequireF32(attributes, "minHaze"),
            Vec2(attributes, "scale"),
            PackValue.RequireF32(attributes, "speed"),
            FileRef(attributes, "texture"),
            PackValue.AsFloats(attributes.Fields.GetValueOrDefault("textureUV")),
            PackValue.RequireF32(attributes, "brightness"),
            (byte)(PackValue.OptionalU32(attributes, "flags") ?? 0),
            ParseLensFlare(attributes.Fields.GetValueOrDefault("lensFlare")));
    }

    private static Gw2MapEnvLensFlare? ParseLensFlare(object? lensFlareValue)
    {
        if (Embedded(lensFlareValue) is not { } lensFlare) return null;
        var atoms = new List<Gw2MapEnvLensFlareAtom>();
        foreach (var atom in PackValue.AsObjects(lensFlare.Fields.GetValueOrDefault("atoms")))
            atoms.Add(new(
                PackValue.RequireU32(atom, "rows"),
                PackValue.RequireU32(atom, "columns"),
                PackValue.RequireU32(atom, "start"),
                PackValue.RequireF32(atom, "fps"),
                PackValue.AsBytes(atom.Fields.GetValueOrDefault("color")),
                Vec2(atom, "offset"),
                Vec2(atom, "scale"),
                PackValue.RequireF32(atom, "baseRotation"),
                PackValue.RequireF32(atom, "cameraRotation"),
                (byte)PackValue.RequireU32(atom, "flags")));
        var textures = PackValue.AsObjects(lensFlare.Fields.GetValueOrDefault("textures"))
            .Select(texture => FileRef(texture, "texture"))
            .ToArray();
        return new(
            atoms,
            textures,
            FileRef(lensFlare, "material"),
            Vec2(lensFlare, "fadeBand"),
            Vec2(lensFlare, "reverseFadeBand"),
            PackValue.RequireF32(lensFlare, "opacityCoeff"),
            (byte)PackValue.RequireU32(lensFlare, "flags"),
            ParseShaderConstants(lensFlare.Fields.GetValueOrDefault("constants")));
    }

    private static Gw2MapEnvSkyCardMaterial? ParseSkyCardMaterial(object? materialValue)
    {
        if (Embedded(materialValue) is not { } material) return null;
        var textures = PackValue.AsObjects(material.Fields.GetValueOrDefault("textures"))
            .Select(texture => new Gw2MapEnvShaderTexture(
                FileRef(texture, "filename"),
                PackValue.AsFloats(texture.Fields.GetValueOrDefault("textureUV"))))
            .ToArray();
        return new(
            FileRef(material, "filename"),
            ParseShaderConstants(material.Fields.GetValueOrDefault("constants")),
            textures,
            ParseSkyCardAnimation(material.Fields.GetValueOrDefault("textureAnimation")),
            ParseSkyCardFlipbook(material.Fields.GetValueOrDefault("flipbook")));
    }

    private static IReadOnlyList<Gw2MapEnvShaderConstant> ParseShaderConstants(object? constantsValue) =>
        PackValue.AsObjects(constantsValue)
            .Select(constant => new Gw2MapEnvShaderConstant(
                PackValue.RequireU32(constant, "token"),
                PackValue.AsFloats(constant.Fields.GetValueOrDefault("value"))))
            .ToArray();

    private static Gw2MapEnvSkyCardAnimation? ParseSkyCardAnimation(object? animationValue)
    {
        if (Embedded(animationValue) is not { } animation) return null;
        return new(
            Vec2(animation, "textureAnimTranslation"),
            Vec2(animation, "textureAnimScaleRangeX"),
            Vec2(animation, "textureAnimScaleRangeY"),
            Vec2(animation, "textureAnimScaleSpeed"),
            PackValue.RequireF32(animation, "textureAnimRotation"),
            PackValue.RequireU32(animation, "texCoords"));
    }

    private static Gw2MapEnvSkyCardFlipbook? ParseSkyCardFlipbook(object? flipbookValue)
    {
        if (Embedded(flipbookValue) is not { } flipbook) return null;
        return new(
            PackValue.RequireU32(flipbook, "rows"),
            PackValue.RequireU32(flipbook, "columns"),
            PackValue.RequireU32(flipbook, "start"),
            PackValue.RequireU32(flipbook, "count"),
            PackValue.RequireF32(flipbook, "fps"));
    }

    private static Gw2MapEnvExposure? ParseExposure(object? extValue)
    {
        if (Embedded(extValue) is not { } ext) return null;
        return new(
            PackValue.RequireF32(ext, "brightTime"),
            PackValue.RequireF32(ext, "dimTime"),
            PackValue.RequireF32(ext, "darkCoeff"),
            PackValue.RequireF32(ext, "darkExp"),
            PackValue.RequireF32(ext, "darkMin"),
            PackValue.RequireF32(ext, "darkMax"),
            PackValue.RequireF32(ext, "brightMin"),
            PackValue.RequireF32(ext, "brightMax"),
            PackValue.RequireF32(ext, "brightScale"),
            PackValue.RequireF32(ext, "darkScale"),
            PackValue.AsFloats(ext.Fields.GetValueOrDefault("waterReflectionParams")));
    }

    private static Gw2MapBacklight? ParseBacklight(object? lightingValue, int group = DayLightingGroup)
    {
        if (lightingValue is not object?[] lightingArray || lightingArray.Length <= group || lightingArray[group] is not PackObject lighting)
            return null;
        return new(
            PackValue.RequireF32(lighting, "shadowInfluence"),
            PackValue.AsBytes(lighting.Fields.GetValueOrDefault("backlightColor")),
            PackValue.RequireF32(lighting, "backlightIntensity"));
    }

    private static Gw2MapEnvWaterInfo? ParseWater(object? waterValue, int entry = 0)
    {
        if (EmbeddedAt(waterValue, entry) is not { } water) return null;
        return new(
            PackValue.AsU32(water.Fields.GetValueOrDefault("waterFlags")),
            PackValue.AsF32(water.Fields.GetValueOrDefault("animAmplitude")),
            PackValue.AsF32(water.Fields.GetValueOrDefault("animChoppiness")),
            Vec2(water, "animWind"),
            PackValue.AsF32(water.Fields.GetValueOrDefault("bumpAmount")),
            Pair(water, "bumpAngle0", "bumpAngle1"),
            Pair(water, "bumpScale0", "bumpScale1"),
            Pair(water, "bumpSpeed0", "bumpSpeed1"),
            Pair(water, "bumpTile0", "bumpTile1"),
            PackValue.AsF32(water.Fields.GetValueOrDefault("patternAngle")),
            PackValue.AsF32(water.Fields.GetValueOrDefault("patternTile")),
            PackValue.AsF32(water.Fields.GetValueOrDefault("patternSpeed")),
            PackValue.AsF32(water.Fields.GetValueOrDefault("patternEdge")),
            PackValue.AsBytes(water.Fields.GetValueOrDefault("surfaceShallowColor")),
            PackValue.AsBytes(water.Fields.GetValueOrDefault("surfaceDeepColor")),
            PackValue.AsBytes(water.Fields.GetValueOrDefault("patternColor")),
            PackValue.AsF32(water.Fields.GetValueOrDefault("surfaceFresnel")),
            PackValue.AsF32(water.Fields.GetValueOrDefault("distortAmount")),
            PackValue.AsF32(water.Fields.GetValueOrDefault("depthAttenuation")),
            FileRef(water, "materialFilename"),
            PositionalFileList(water, "textureFilenames"),
            U32List(water, "constantTokens"),
            Vec4List(water, "constantValues"),
            PackValue.AsF32(water.Fields.GetValueOrDefault("foamSpawn")),
            PackValue.AsF32(water.Fields.GetValueOrDefault("foamDissolve")),
            PackValue.AsF32(water.Fields.GetValueOrDefault("foamDepthAttenuation")),
            PackValue.AsBytes(water.Fields.GetValueOrDefault("foamColor0")),
            PackValue.AsBytes(water.Fields.GetValueOrDefault("foamColor1")));
    }

    private static float[] Pair(PackObject obj, string first, string second) =>
        new[]
        {
            PackValue.AsF32(obj.Fields.GetValueOrDefault(first)),
            PackValue.AsF32(obj.Fields.GetValueOrDefault(second)),
        };

    private static Vector2 Vec2(PackObject obj, string name) =>
        obj.Fields.GetValueOrDefault(name) is float[] values && values.Length >= 2
            ? new Vector2(values[0], values[1])
            : Vector2.Zero;

    private static PackFileReference FileRef(PackObject obj, string name) =>
        obj.Fields.GetValueOrDefault(name) is PackFileReference reference ? reference : default;

    private static IReadOnlyList<PackFileReference?> PositionalFileList(PackObject obj, string name) =>
        obj.Fields.GetValueOrDefault(name) is object?[] values ? values.Select(value => value is PackFileReference reference ? reference : (PackFileReference?)null).ToArray() : [];

    private static IReadOnlyList<PackFileReference> FileList(PackObject obj, string name)
    {
        if (obj.Fields.GetValueOrDefault(name) is not object?[] values) return [];
        var result = new List<PackFileReference>(values.Length);
        foreach (var value in values)
            if (value is PackFileReference reference) result.Add(reference);
        return result;
    }

    private static IReadOnlyList<uint> U32List(PackObject obj, string name) =>
        obj.Fields.GetValueOrDefault(name) is object?[] values
            ? values.Select(PackValue.AsU32).ToArray()
            : [];

    private static IReadOnlyList<float[]> Vec4List(PackObject obj, string name)
    {
        if (obj.Fields.GetValueOrDefault(name) is not object?[] values) return [];
        var result = new List<float[]>(values.Length);
        foreach (var value in values)
            if (value is float[] floats) result.Add(floats);
        return result;
    }

    private static Gw2MapEnvWind? ParseWind(object? windValue)
    {
        if (Embedded(windValue) is not { } wind) return null;
        return new(
            AsByte(wind.Fields.GetValueOrDefault("azimuth")),
            AsByte(wind.Fields.GetValueOrDefault("elevation")),
            AsByte(wind.Fields.GetValueOrDefault("noise")),
            AsByte(wind.Fields.GetValueOrDefault("speed")),
            AsByte(wind.Fields.GetValueOrDefault("gust")),
            AsByte(wind.Fields.GetValueOrDefault("gustFreq")))
        {
            GustSpeed = AsByte(wind.Fields.GetValueOrDefault("gustSpeed")),
        };
    }

    private static byte AsByte(object? value) => value switch
    {
        byte byteValue => byteValue,
        uint unsigned => (byte)unsigned,
        int signed => (byte)signed,
        ulong wide => (byte)wide,
        _ => 0,
    };

    private const int DayLightingGroup = 0;
    private const int NightLightingGroup = 1;
    private const int UnderwaterLightingGroup = 2;
    private const int NightWaterEntry = 1;
    private const int UnderwaterWaterEntry = 2;

    private static IReadOnlyList<Gw2MapLightInfo> ParseLights(object? lightingValue, int group = DayLightingGroup)
    {
        var lights = new List<Gw2MapLightInfo>();
        var lighting = lightingValue is object?[] lightingArray && lightingArray.Length > group
            ? lightingArray[group] as PackObject
            : null;
        if (lighting?.Fields.GetValueOrDefault("lights") is object?[] lightObjects)
            foreach (var lightObject in lightObjects)
                if (lightObject is PackObject light)
                    lights.Add(new(
                        PackValue.AsBytes(light.Fields.GetValueOrDefault("color")),
                        PackValue.AsF32(light.Fields.GetValueOrDefault("intensity")),
                        Vec3(light, "direction")));
        return lights;
    }

    private static Gw2MapHazeInfo? ParseHaze(object? hazeValue, int group = DayLightingGroup)
    {
        var haze = hazeValue is object?[] hazeArray ? hazeArray.Length > group ? hazeArray[group] as PackObject : null : group == DayLightingGroup ? Embedded(hazeValue) : null;
        if (haze is null) return null;
        return new(
            PackValue.AsBytes(haze.Fields.GetValueOrDefault("nearColor")),
            PackValue.AsBytes(haze.Fields.GetValueOrDefault("farColor")),
            PackValue.AsFloats(haze.Fields.GetValueOrDefault("distRange")),
            PackValue.AsF32(haze.Fields.GetValueOrDefault("depthCue")),
            PackValue.AsBytes(haze.Fields.GetValueOrDefault("heightColor")),
            PackValue.AsFloats(haze.Fields.GetValueOrDefault("heightRange")),
            PackValue.AsFloats(haze.Fields.GetValueOrDefault("sunDirRange")));
    }

    private static Gw2MapEnvSky? ParseSky(PackObject? global)
    {
        if (global is null) return null;
        var textures = new List<Gw2MapEnvSkyTex>();
        if (global.Fields.GetValueOrDefault("skyModeTex") is object?[] textureObjects)
            foreach (var textureObject in textureObjects)
                if (textureObject is PackObject texture)
                    textures.Add(new(
                        FileRef(texture, "texPathNE"),
                        FileRef(texture, "texPathSW"),
                        FileRef(texture, "texPathT")));
        var cubeTextures = new List<Gw2MapEnvSkyCubeTex>();
        foreach (var cube in PackValue.AsObjects(global.Fields.GetValueOrDefault("skyModeCubeTex")))
            cubeTextures.Add(new(
                FileRef(cube, "texPathB"),
                FileRef(cube, "texPathE"),
                FileRef(cube, "texPathN"),
                FileRef(cube, "texPathS"),
                FileRef(cube, "texPathT"),
                FileRef(cube, "texPathW")));
        return new(FileRef(global, "starFile"), textures)
        {
            Dome = ParseSkyDome(global.Fields.GetValueOrDefault("sky")),
            ModeCubeTextures = cubeTextures,
        };
    }

    private static Gw2MapEnvSkyDome? ParseSkyDome(object? skyValue)
    {
        if (Embedded(skyValue) is not { } sky) return null;
        return new(
            PackValue.AsU8(sky.Fields.GetValueOrDefault("flags")),
            ParseSkyPhase(sky, "day"),
            ParseSkyPhase(sky, "night"),
            PackValue.RequireF32(sky, "verticalOffset"));
    }

    private static Gw2MapEnvSkyPhase ParseSkyPhase(PackObject sky, string phase) =>
        new(
            PackValue.RequireF32(sky, phase + "Brightness"),
            PackValue.RequireF32(sky, phase + "HazeBottom"),
            PackValue.RequireF32(sky, phase + "HazeDensity"),
            PackValue.RequireF32(sky, phase + "HazeFalloff"),
            PackValue.RequireF32(sky, phase + "LightIntensity"),
            PackValue.RequireF32(sky, phase + "StarDensity"));

    private static Gw2MapEnvEffect? ParseEffect(object? effectValue, int group = DayLightingGroup)
    {
        var effect = effectValue is object?[] effectArray ? effectArray.Length > group ? effectArray[group] as PackObject : null : group == DayLightingGroup ? Embedded(effectValue) : null;
        if (effect is null) return null;
        return new(
            PackValue.AsF32(effect.Fields.GetValueOrDefault("saturation")),
            PackValue.AsF32(effect.Fields.GetValueOrDefault("tintAmount")),
            PackValue.AsU32(effect.Fields.GetValueOrDefault("flags")),
            FileRef(effect, "clutTexturePath"))
        {
            TintColor = PackValue.AsBytes(effect.Optional("tintColor")),
            TintTargetColor = PackValue.AsBytes(effect.Optional("tintTargetColor")),
            TintFocus = PackValue.OptionalF32(effect, "tintFocus") ?? 0,
            Glow = PackValue.AsBytes(effect.Optional("glow")),
            GlowLevel = PackValue.AsBytes(effect.Optional("glowLevel")),
            GlowAmplify = PackValue.OptionalF32(effect, "glowAmplify") ?? 0,
            FocalDepth = PackValue.OptionalF32(effect, "focalDepth") ?? 0,
            FocalRange = PackValue.OptionalF32(effect, "focalRange") ?? 0,
            SsaoAmount = PackValue.OptionalF32(effect, "ssaoAmount") ?? 0,
            SsaoBrighten = PackValue.OptionalF32(effect, "ssaoBrighten") ?? 0,
            SsaoContrast = PackValue.OptionalF32(effect, "ssaoContrast") ?? 0,
            SsaoSunScale = PackValue.OptionalF32(effect, "ssaoSunScale") ?? 0,
        };
    }

    private static IReadOnlyList<Gw2MapEnvShape> ParseShapes(object? value)
    {
        if (value is not object?[] values) return [];
        var shapes = new List<Gw2MapEnvShape>(values.Length);
        foreach (var item in values)
            if (item is PackObject shape)
                shapes.Add(new(
                    Vec3(shape, "center"),
                    PackValue.AsF32(shape.Fields.GetValueOrDefault("height")),
                    PackValue.AsF32(shape.Fields.GetValueOrDefault("fadeHorizInner")),
                    PackValue.AsF32(shape.Fields.GetValueOrDefault("fadeHorizOuter")),
                    PackValue.AsF32(shape.Fields.GetValueOrDefault("fadeVertical")),
                    PackValue.AsVec2List(shape.Fields.GetValueOrDefault("vertexArray")),
                    AsByte(shape.Fields.GetValueOrDefault("shapeType"))));
        return shapes;
    }

    private static PackObject? EmbeddedAt(object? value, int entry) => value switch
    {
        PackObject obj when entry == 0 => obj,
        object?[] array when entry < array.Length => array[entry] as PackObject,
        _ => null,
    };

    private static PackObject? Embedded(object? value) => value switch
    {
        PackObject obj => obj,
        object?[] { Length: > 0 } array => array[0] as PackObject,
        _ => null,
    };

    private static Vector3 Vec3(PackObject obj, string name) =>
        obj.Fields.GetValueOrDefault(name) is float[] values && values.Length >= 3
            ? new Vector3(values[0], values[1], values[2])
            : Vector3.Zero;
}
