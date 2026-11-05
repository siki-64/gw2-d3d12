using Gw2.Formats;
using System.Numerics;

namespace Gw2.Dat.Content;

public sealed record Gw2MapCubeMapSample(
    Vector3 Position,
    string DayDefault,
    string NightDefault,
    string DayScript,
    string NightScript,
    string DayDefaultHiRes,
    string NightDefaultHiRes,
    string DayScriptHiRes,
    string NightScriptHiRes,
    ulong EnvId);

public sealed record Gw2MapCubeMapParams(
    uint ModulateColor,
    float Brightness,
    float Contrast,
    uint BlurPasses,
    string EnvVolume);

public sealed class Gw2MapCubeMap
{
    public Gw2MapCubeMap(IReadOnlyList<Gw2MapCubeMapSample> samples, IReadOnlyList<Gw2MapCubeMapParams> parameters)
    {
        Samples = samples;
        Parameters = parameters;
    }

    public IReadOnlyList<Gw2MapCubeMapSample> Samples { get; }
    public IReadOnlyList<Gw2MapCubeMapParams> Parameters { get; }

    public static Gw2MapCubeMap? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.FindRoot("PackMapCubeMap");
        if (root is null) return null;

        var samples = new List<Gw2MapCubeMapSample>();
        if (root.Fields.GetValueOrDefault("sampleArray") is object?[] sampleObjects)
            foreach (var sampleObject in sampleObjects)
                if (sampleObject is PackObject sample)
                    samples.Add(new(
                        Vec3(sample, "position"),
                        Str(sample, "filenameDayDefault"),
                        Str(sample, "filenameNightDefault"),
                        Str(sample, "filenameDayScript"),
                        Str(sample, "filenameNightScript"),
                        Str(sample, "filenameDayDefaultHiRes"),
                        Str(sample, "filenameNightDefaultHiRes"),
                        Str(sample, "filenameDayScriptHiRes"),
                        Str(sample, "filenameNightScriptHiRes"),
                        PackValue.AsU64(sample.Fields.GetValueOrDefault("envID"))));

        var parameters = new List<Gw2MapCubeMapParams>();
        if (root.Fields.GetValueOrDefault("paramsArray") is object?[] paramObjects)
            foreach (var paramObject in paramObjects)
                if (paramObject is PackObject param)
                    parameters.Add(new(
                        PackValue.AsU32(param.Fields.GetValueOrDefault("modulateColor")),
                        PackValue.AsF32(param.Fields.GetValueOrDefault("brightness")),
                        PackValue.AsF32(param.Fields.GetValueOrDefault("contrast")),
                        PackValue.AsU32(param.Fields.GetValueOrDefault("blurPasses")),
                        Str(param, "envVolume")));

        return new(samples, parameters);
    }

    private static string Str(PackObject obj, string name) => obj.Fields.GetValueOrDefault(name) as string ?? string.Empty;

    private static Vector3 Vec3(PackObject obj, string name) =>
        obj.Fields.GetValueOrDefault(name) is float[] values && values.Length >= 3
            ? new Vector3(values[0], values[1], values[2])
            : Vector3.Zero;
}
