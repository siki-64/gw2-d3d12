using Gw2.Formats;
using System.Numerics;

namespace Gw2.Dat.Content;

public sealed record Gw2MapAudioRegion(
    uint RegionType,
    uint OverrideMode,
    string SourceDay,
    string AmbientDay,
    string SourceNight,
    string AmbientNight,
    string Interior,
    float ExteriorVolume,
    uint Priority,
    IReadOnlyList<Vector3> Points,
    Vector3 Position,
    Vector3 Orientation,
    float FadeBand,
    float Height,
    float Radius,
    ulong Guid,
    uint Flags);

public sealed record Gw2MapAudioRegionTool(string Annotation);

public sealed record Gw2MapAudioDependency(string Dependency, uint Flags);

public sealed class Gw2MapAudio
{
    public Gw2MapAudio(
        string ambientDaySurface,
        string ambientDayUnderwater,
        string ambientNightSurface,
        string ambientNightUnderwater,
        IReadOnlyList<Gw2MapAudioRegion> regions,
        IReadOnlyList<Gw2MapAudioRegionTool> regionTools,
        IReadOnlyList<Gw2MapAudioDependency> dependencies)
    {
        AmbientDaySurface = ambientDaySurface;
        AmbientDayUnderwater = ambientDayUnderwater;
        AmbientNightSurface = ambientNightSurface;
        AmbientNightUnderwater = ambientNightUnderwater;
        Regions = regions;
        RegionTools = regionTools;
        Dependencies = dependencies;
    }

    public string AmbientDaySurface { get; }
    public string AmbientDayUnderwater { get; }
    public string AmbientNightSurface { get; }
    public string AmbientNightUnderwater { get; }
    public IReadOnlyList<Gw2MapAudioRegion> Regions { get; }
    public IReadOnlyList<Gw2MapAudioRegionTool> RegionTools { get; }
    public IReadOnlyList<Gw2MapAudioDependency> Dependencies { get; }

    public static Gw2MapAudio? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.Chunks
            .Select(chunk => chunk.Root)
            .FirstOrDefault(candidate => candidate is not null && candidate.TypeName == "MapAudio");
        if (root is null) return null;

        var regions = new List<Gw2MapAudioRegion>();
        if (root.Fields.GetValueOrDefault("audioRegions") is object?[] regionObjects)
            foreach (var regionObject in regionObjects)
                if (regionObject is PackObject region)
                    regions.Add(new(
                        PackValue.AsU32(region.Fields.GetValueOrDefault("regionType")),
                        PackValue.AsU32(region.Fields.GetValueOrDefault("overrideMode")),
                        Str(region, "filenameSourceDay"),
                        Str(region, "filenameAmbientDay"),
                        Str(region, "filenameSourceNight"),
                        Str(region, "filenameAmbientNight"),
                        Str(region, "filenameInterior"),
                        PackValue.AsF32(region.Fields.GetValueOrDefault("exteriorVolume")),
                        PackValue.AsU32(region.Fields.GetValueOrDefault("priority")),
                        PackValue.AsVec3List(region.Fields.GetValueOrDefault("points")),
                        Vec3(region, "position"),
                        Vec3(region, "orientation"),
                        PackValue.AsF32(region.Fields.GetValueOrDefault("fadeBand")),
                        PackValue.AsF32(region.Fields.GetValueOrDefault("height")),
                        PackValue.AsF32(region.Fields.GetValueOrDefault("radius")),
                        PackValue.AsU64(region.Fields.GetValueOrDefault("guid")),
                        PackValue.AsU32(region.Fields.GetValueOrDefault("flags"))));

        var tools = new List<Gw2MapAudioRegionTool>();
        if (root.Fields.GetValueOrDefault("audioRegionTools") is object?[] toolObjects)
            foreach (var toolObject in toolObjects)
                if (toolObject is PackObject tool)
                    tools.Add(new(tool.Fields.GetValueOrDefault("annotation") as string ?? string.Empty));

        var dependencies = new List<Gw2MapAudioDependency>();
        if (root.Fields.GetValueOrDefault("audioDepArray") is object?[] dependencyObjects)
            foreach (var dependencyObject in dependencyObjects)
                if (dependencyObject is PackObject dependency)
                    dependencies.Add(new(
                        dependency.Fields.GetValueOrDefault("dependency") as string ?? string.Empty,
                        PackValue.AsU32(dependency.Fields.GetValueOrDefault("flags"))));

        return new(
            Str(root, "filenameAmbientDaySurface"),
            Str(root, "filenameAmbientDayUnderwater"),
            Str(root, "filenameAmbientNightSurface"),
            Str(root, "filenameAmbientNightUnderwater"),
            regions,
            tools,
            dependencies);
    }

    private static string Str(PackObject obj, string name) => obj.Fields.GetValueOrDefault(name) as string ?? string.Empty;

    private static Vector3 Vec3(PackObject obj, string name) =>
        obj.Fields.GetValueOrDefault(name) is float[] values && values.Length >= 3
            ? new Vector3(values[0], values[1], values[2])
            : Vector3.Zero;
}
