using Gw2.Formats;
using System.Numerics;
using Gw2.Dat.Archive;

namespace Gw2.Dat.Content;

public sealed record Gw2MapPointLight(
    Vector3 Position,
    byte[] Color,
    float Intensity,
    float NearDistance,
    float FarDistance,
    uint Flags);

public sealed record Gw2MapSpotLight(
    Vector3 Position,
    byte[] Color,
    float Intensity,
    float NearDistance,
    float FarDistance,
    uint Flags,
    Vector3 Direction,
    Vector3 UpDirection,
    float InnerAngle,
    float OuterAngle,
    string TextureName,
    uint ShadowFileId,
    IReadOnlyList<Gw2MapSpotLightLod> Lods);

public sealed record Gw2MapSpotLightLod(
    float Distance,
    uint Flags,
    uint LightFlags,
    byte[] Color,
    float Intensity,
    float NearDistance,
    float FarDistance,
    float InnerAngle,
    float OuterAngle);

public sealed record Gw2MapLightCurve(
    float Gust,
    float GustFrequency,
    float Noise,
    float Phase,
    float Offset,
    float Amplitude,
    uint CurveType);

public sealed record Gw2MapPortalLight(
    ulong Guid,
    IReadOnlyList<Vector3> Points,
    float FadeCamera,
    float FadeLight,
    float Lighten,
    float Darken)
{
    public uint BroadId { get; init; }
}

public sealed record Gw2MapVolumeLightImage(uint FileId, IReadOnlyList<uint> Dimensions, uint Format);

public sealed record Gw2MapVolumeLight(
    ulong Guid,
    string Name,
    Vector3 Position,
    Vector3 Extents,
    Vector3 Rotation,
    float[] Intensities,
    float[] Pack,
    IReadOnlyList<Vector3> FloodPoints,
    uint Flags,
    IReadOnlyList<Gw2MapVolumeLightImage> Images,
    uint BroadId);

public sealed record Gw2MapLightBroadphase(byte[] Data, uint MaxBroadId)
{
    public Gw2Broadphase? Decoded => Gw2Broadphase.TryParse(Data);
}

public sealed record Gw2MapLightGroup<T>(ulong Guid, IReadOnlyList<T> Lights, IReadOnlyList<Gw2MapLightCurve> Curves)
{
    public uint BroadId { get; init; }
}

public sealed class Gw2MapLights
{
    public Gw2MapLights(
        IReadOnlyList<Gw2MapLightGroup<Gw2MapPointLight>> pointLights,
        IReadOnlyList<Gw2MapLightGroup<Gw2MapSpotLight>> spotLights,
        IReadOnlyList<Gw2MapPortalLight> portalLights,
        IReadOnlyList<Gw2MapVolumeLight>? volumeLights = null,
        Gw2MapLightBroadphase? broadphase = null)
    {
        VolumeLights = volumeLights ?? [];
        Broadphase = broadphase;
        PointLights = pointLights;
        SpotLights = spotLights;
        PortalLights = portalLights;
    }

    public IReadOnlyList<Gw2MapLightGroup<Gw2MapPointLight>> PointLights { get; }
    public IReadOnlyList<Gw2MapLightGroup<Gw2MapSpotLight>> SpotLights { get; }
    public IReadOnlyList<Gw2MapPortalLight> PortalLights { get; }
    public uint MaxLightVsConstants { get; init; }
    public IReadOnlyList<Gw2MapVolumeLight> VolumeLights { get; }
    public Gw2MapLightBroadphase? Broadphase { get; }

    public static Gw2MapLights? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.FindRoot("PackMapLights");
        if (root is null) return null;

        return new(
            ParseGroups(root, "pointLights", ParsePointLight),
            ParseGroups(root, "spotLights", ParseSpotLight),
            Objects(root.Fields.GetValueOrDefault("portalLights")).Select(ParsePortalLight).ToArray(),
            Objects(root.Fields.GetValueOrDefault("volumeLights")).Select(ParseVolumeLight).ToArray(),
            ParseBroadphase(root))
        {
            MaxLightVsConstants = PackValue.AsU32(root.Optional("maxLightVsConstants")),
        };
    }

    private static Gw2MapVolumeLight ParseVolumeLight(PackObject volume) => new(
        PackValue.RequireU64(volume, "guid"),
        volume.Fields.GetValueOrDefault("name") as string ?? "",
        Vec3(volume, "position"),
        Vec3(volume, "extents"),
        Vec3(volume, "rotation"),
        PackValue.AsFloats(volume.Fields.GetValueOrDefault("intensities")),
        PackValue.AsFloats(volume.Fields.GetValueOrDefault("pack")),
        PackValue.AsVec3List(volume.Fields.GetValueOrDefault("floodPoints")),
        PackValue.RequireU32(volume, "flags"),
        Objects(volume.Fields.GetValueOrDefault("images")).Select(ParseVolumeLightImage).ToArray(),
        PackValue.RequireU32(volume, "broadId"));

    private static Gw2MapVolumeLightImage ParseVolumeLightImage(PackObject image) => new(
        image.Fields.GetValueOrDefault("filename") is PackFileReference reference && reference.TryGetFileId(out var fileId) ? fileId : 0,
        image.Fields.GetValueOrDefault("dims") is uint[] dimensions ? dimensions : [],
        PackValue.RequireU32(image, "format"));

    private static Gw2MapLightBroadphase? ParseBroadphase(PackObject root) =>
        root.Optional("broadPhase") is PackObject broadphase
            ? new(PackValue.AsBytes(broadphase.Fields.GetValueOrDefault("broadphaseData")), PackValue.OptionalU32(root, "maxBroadId") ?? 0)
            : null;

    private static IReadOnlyList<Gw2MapLightGroup<T>> ParseGroups<T>(
        PackObject root,
        string groupField,
        Func<PackObject, T> parse)
    {
        var groups = new List<Gw2MapLightGroup<T>>();
        if (root.Fields.GetValueOrDefault(groupField) is not object?[] groupObjects) return groups;

        foreach (var groupObject in groupObjects)
        {
            if (groupObject is not PackObject group) continue;
            var lights = new List<T>();
            if (group.Fields.GetValueOrDefault("lights") is object?[] lightObjects)
                foreach (var lightObject in lightObjects)
                    if (lightObject is PackObject light)
                        lights.Add(parse(light));
            groups.Add(new(
                PackValue.AsU64(group.Fields.GetValueOrDefault("guid")),
                lights,
                Objects(group.Fields.GetValueOrDefault("curves")).Select(ParseCurve).ToArray())
            {
                BroadId = PackValue.AsU32(group.Optional("broadId")),
            });
        }

        return groups;
    }

    private static Gw2MapPointLight ParsePointLight(PackObject light) => new(
        Vec3(light, "position"),
        PackValue.AsBytes(light.Fields.GetValueOrDefault("color")),
        PackValue.AsF32(light.Fields.GetValueOrDefault("intensity")),
        PackValue.AsF32(light.Fields.GetValueOrDefault("nearDistance")),
        PackValue.AsF32(light.Fields.GetValueOrDefault("farDistance")),
        PackValue.AsU32(light.Fields.GetValueOrDefault("flags")));

    private static Gw2MapSpotLight ParseSpotLight(PackObject light) => new(
        Vec3(light, "position"),
        PackValue.AsBytes(light.Fields.GetValueOrDefault("color")),
        PackValue.AsF32(light.Fields.GetValueOrDefault("intensity")),
        PackValue.AsF32(light.Fields.GetValueOrDefault("nearDistance")),
        PackValue.AsF32(light.Fields.GetValueOrDefault("farDistance")),
        PackValue.AsU32(light.Fields.GetValueOrDefault("flags")),
        Vec3(light, "direction"),
        Vec3(light, "upDirection"),
        PackValue.AsF32(light.Fields.GetValueOrDefault("innerAngle")),
        PackValue.AsF32(light.Fields.GetValueOrDefault("outerAngle")),
        light.Fields.GetValueOrDefault("textureName") as string ?? string.Empty,
        light.Fields.GetValueOrDefault("shadowData") is PackObject shadow &&
            shadow.Fields.GetValueOrDefault("shadowFilename") is PackFileReference shadowFile &&
            shadowFile.TryGetFileId(out var shadowFileId) ? shadowFileId : 0,
        Objects(light.Fields.GetValueOrDefault("lodData")).Select(ParseSpotLightLod).ToArray());

    private static Gw2MapSpotLightLod ParseSpotLightLod(PackObject lod) => new(
        PackValue.AsF32(lod.Fields.GetValueOrDefault("distance")),
        PackValue.AsU32(lod.Fields.GetValueOrDefault("flags")),
        PackValue.AsU32(lod.Fields.GetValueOrDefault("lightFlags")),
        PackValue.AsBytes(lod.Fields.GetValueOrDefault("color")),
        PackValue.AsF32(lod.Fields.GetValueOrDefault("intensity")),
        PackValue.AsF32(lod.Fields.GetValueOrDefault("nearDist")),
        PackValue.AsF32(lod.Fields.GetValueOrDefault("farDist")),
        PackValue.AsF32(lod.Fields.GetValueOrDefault("innerAngle")),
        PackValue.AsF32(lod.Fields.GetValueOrDefault("outerAngle")));

    private static Gw2MapLightCurve ParseCurve(PackObject curve) => new(
        PackValue.AsF32(curve.Fields.GetValueOrDefault("gust")),
        PackValue.AsF32(curve.Fields.GetValueOrDefault("gustFreq")),
        PackValue.AsF32(curve.Fields.GetValueOrDefault("noise")),
        PackValue.AsF32(curve.Fields.GetValueOrDefault("phase")),
        PackValue.AsF32(curve.Fields.GetValueOrDefault("offset")),
        PackValue.AsF32(curve.Fields.GetValueOrDefault("amplitude")),
        PackValue.AsU32(curve.Fields.GetValueOrDefault("curveType")));

    private static Gw2MapPortalLight ParsePortalLight(PackObject portal)
    {
        var data = portal.Fields.GetValueOrDefault("portalData") is object?[] dataObjects
            ? dataObjects.OfType<PackObject>().FirstOrDefault()
            : null;
        return new(
            PackValue.AsU64(portal.Fields.GetValueOrDefault("guid")),
            portal.Fields.GetValueOrDefault("points") is object?[] points
                ? points.OfType<float[]>().Where(point => point.Length >= 3).Select(point => new Vector3(point[0], point[1], point[2])).ToArray()
                : [],
            PackValue.AsF32(data?.Fields.GetValueOrDefault("fadeCamera")),
            PackValue.AsF32(data?.Fields.GetValueOrDefault("fadeLight")),
            PackValue.AsF32(data?.Fields.GetValueOrDefault("lighten")),
            PackValue.AsF32(data?.Fields.GetValueOrDefault("darken")))
        {
            BroadId = PackValue.AsU32(portal.Optional("broadId")),
        };
    }

    private static IEnumerable<PackObject> Objects(object? value) =>
        value is object?[] items ? items.OfType<PackObject>() : [];

    private static Vector3 Vec3(PackObject obj, string name) =>
        obj.Fields.GetValueOrDefault(name) is float[] values && values.Length >= 3
            ? new Vector3(values[0], values[1], values[2])
            : Vector3.Zero;
}
