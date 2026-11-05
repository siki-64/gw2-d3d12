using Gw2.Formats;
namespace Gw2.Dat.Content;

public sealed record Gw2SceneCurveKey(float Time, float Value, float InTangent, float OutTangent);

public sealed record Gw2SceneFlagKey(float Time, float Value);

public sealed record Gw2SceneTriggerKey(float Time, ulong Token1, ulong Token2, float Value1, float Value2)
{
    public byte Flags1 { get; init; }
    public byte Flags2 { get; init; }
    public byte Flags3 { get; init; }
    public byte Flags4 { get; init; }
    public float Value3 { get; init; }
    public float Value4 { get; init; }
}

public sealed record Gw2SceneProperty(ulong Value, PackFileReference PathValue, byte Type);

public sealed record Gw2SceneColorDefinition(float Intensity, byte[] Color);

public sealed record Gw2SceneAmbientLight(
    ulong Name,
    Gw2SceneColorDefinition AmbientGroundColor,
    Gw2SceneColorDefinition AmbientSkyColor,
    Gw2SceneColorDefinition FillColor,
    Gw2SceneColorDefinition HemisphericalColor);

public sealed record Gw2SceneFileNameReference(ulong Name, PackFileReference FileName);

public sealed record Gw2SceneScript(ulong Name, byte[] ByteCode);

public sealed record Gw2SceneTextResource(ulong Name, uint Index, uint VoiceId, IReadOnlyList<string> Texts);

public sealed record Gw2SceneSpeciesResource(byte[] SpeciesId, ulong Name, ulong ModelId, ulong ModelVariant);

public sealed record Gw2SceneTrack(
    ulong Name,
    uint Type,
    IReadOnlyList<Gw2SceneCurveKey> CurveKeys,
    IReadOnlyList<Gw2SceneFlagKey> FlagKeys,
    IReadOnlyList<Gw2SceneTriggerKey> TriggerKeys);

public sealed record Gw2SceneTrackGroup(ulong Name, uint Flags, IReadOnlyList<Gw2SceneTrack> Tracks)
{
    public byte Type { get; init; }
    public IReadOnlyList<Gw2SceneProperty> Properties { get; init; } = [];
}

public sealed record Gw2SceneSequence(
    ulong Name,
    ulong? PlayScript,
    ulong? UpdateScript,
    string EnvironmentMap,
    string Map,
    string ClientMap,
    float Length,
    uint Flags,
    IReadOnlyList<Gw2SceneTrackGroup> TrackGroups);

public sealed class Gw2Scene
{
    public Gw2Scene(ulong startingSequence, IReadOnlyList<Gw2SceneSequence> sequences)
    {
        StartingSequence = startingSequence;
        Sequences = sequences;
    }

    public ulong StartingSequence { get; }
    public IReadOnlyList<Gw2SceneSequence> Sequences { get; }
    public IReadOnlyList<Gw2SceneAmbientLight> AmbientLights { get; init; } = [];
    public IReadOnlyList<Gw2SceneFileNameReference> FileNameReferences { get; init; } = [];
    public IReadOnlyList<Gw2SceneScript> Scripts { get; init; } = [];
    public IReadOnlyList<Gw2SceneTextResource> TextResources { get; init; } = [];
    public IReadOnlyList<Gw2SceneSpeciesResource> SpeciesResources { get; init; } = [];

    public static Gw2Scene? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.FindRoot("SceneData");
        if (root is null) return null;

        var sequences = new List<Gw2SceneSequence>();
        if (root.Fields.GetValueOrDefault("sequence") is object?[] sequenceObjects)
            foreach (var sequenceObject in sequenceObjects)
                if (sequenceObject is PackObject sequence)
                    sequences.Add(new(
                        PackValue.AsU64(sequence.Fields.GetValueOrDefault("name")),
                        PackValue.OptionalU64(sequence, "playScript"),
                        PackValue.OptionalU64(sequence, "updateScript"),
                        sequence.Fields.GetValueOrDefault("environmentMap") as string ?? string.Empty,
                        sequence.Fields.GetValueOrDefault("map") as string ?? string.Empty,
                        sequence.Fields.GetValueOrDefault("clientMap") as string ?? string.Empty,
                        PackValue.AsF32(sequence.Fields.GetValueOrDefault("length")),
                        PackValue.OptionalU32(sequence, "flags") ?? 0,
                        TrackGroups(sequence.Fields.GetValueOrDefault("trackGroup"))));

        var resources = root.Optional("resources") as PackObject;
        return new(PackValue.AsU64(root.Fields.GetValueOrDefault("startingSequence")), sequences)
        {
            AmbientLights = PackValue.AsObjects(resources?.Optional("ambientLightResource"))
                .Select(light => new Gw2SceneAmbientLight(
                    PackValue.AsU64(light.Fields.GetValueOrDefault("name")),
                    ColorDefinition(light, "ambientGroundColor"),
                    ColorDefinition(light, "ambientSkyColor"),
                    ColorDefinition(light, "fillColor"),
                    ColorDefinition(light, "hemisphericalColor")))
                .ToArray(),
            FileNameReferences = PackValue.AsObjects(resources?.Optional("fileNameRef"))
                .Select(reference => new Gw2SceneFileNameReference(
                    PackValue.AsU64(reference.Fields.GetValueOrDefault("name")),
                    FileReference(reference, "fileName")))
                .ToArray(),
            Scripts = PackValue.AsObjects(resources?.Optional("script"))
                .Select(script => new Gw2SceneScript(
                    PackValue.AsU64(script.Fields.GetValueOrDefault("name")),
                    PackValue.AsBytes(script.Fields.GetValueOrDefault("byteCode"))))
                .ToArray(),
            TextResources = PackValue.AsObjects(resources?.Optional("textResource"))
                .Select(text => new Gw2SceneTextResource(
                    PackValue.AsU64(text.Fields.GetValueOrDefault("name")),
                    PackValue.AsU32(text.Fields.GetValueOrDefault("index")),
                    PackValue.AsU32(text.Fields.GetValueOrDefault("voiceId")),
                    PackValue.AsObjects(text.Fields.GetValueOrDefault("textEntry"))
                        .Select(entry => entry.Fields.GetValueOrDefault("text") as string ?? string.Empty)
                        .ToArray()))
                .ToArray(),
            SpeciesResources = PackValue.AsObjects(resources?.Optional("speciesResource"))
                .Select(species => new Gw2SceneSpeciesResource(
                    PackValue.AsBytes(species.Fields.GetValueOrDefault("speciesId")),
                    PackValue.AsU64(species.Fields.GetValueOrDefault("name")),
                    PackValue.AsU64(species.Fields.GetValueOrDefault("modelId")),
                    PackValue.AsU64(species.Fields.GetValueOrDefault("modelVariant"))))
                .ToArray(),
        };
    }

    private static Gw2SceneColorDefinition ColorDefinition(PackObject owner, string name) =>
        owner.Fields.GetValueOrDefault(name) is PackObject color
            ? new(PackValue.AsF32(color.Fields.GetValueOrDefault("intensity")), PackValue.AsBytes(color.Fields.GetValueOrDefault("color")))
            : new(0, []);

    private static PackFileReference FileReference(PackObject owner, string name) =>
        owner.Fields.GetValueOrDefault(name) is PackFileReference reference ? reference : default;

    private static IReadOnlyList<Gw2SceneTrackGroup> TrackGroups(object? value)
    {
        if (value is not object?[] values) return [];
        var result = new List<Gw2SceneTrackGroup>(values.Length);
        foreach (var item in values)
            if (item is PackObject group)
                result.Add(new(
                    PackValue.AsU64(group.Fields.GetValueOrDefault("name")),
                    PackValue.AsU32(group.Fields.GetValueOrDefault("flags")),
                    Tracks(group.Fields.GetValueOrDefault("track")))
                {
                    Type = PackValue.AsU8(group.Fields.GetValueOrDefault("type")),
                    Properties = PackValue.AsObjects(group.Fields.GetValueOrDefault("prop"))
                        .Select(property => new Gw2SceneProperty(
                            PackValue.AsU64(property.Fields.GetValueOrDefault("value")),
                            FileReference(property, "pathVal"),
                            PackValue.AsU8(property.Fields.GetValueOrDefault("type"))))
                        .ToArray(),
                });
        return result;
    }

    private static IReadOnlyList<Gw2SceneTrack> Tracks(object? value)
    {
        if (value is not object?[] values) return [];
        var result = new List<Gw2SceneTrack>(values.Length);
        foreach (var item in values)
            if (item is PackObject track)
                result.Add(new(
                    PackValue.AsU64(track.Fields.GetValueOrDefault("name")),
                    PackValue.AsU32(track.Fields.GetValueOrDefault("type")),
                    CurveKeys(track.Fields.GetValueOrDefault("curveKey")),
                    FlagKeys(track.Fields.GetValueOrDefault("flagKey")),
                    TriggerKeys(track.Fields.GetValueOrDefault("triggerKey"))));
        return result;
    }

    private static IReadOnlyList<Gw2SceneCurveKey> CurveKeys(object? value)
    {
        if (value is not object?[] values) return [];
        var result = new List<Gw2SceneCurveKey>(values.Length);
        foreach (var item in values)
            if (item is PackObject key)
                result.Add(new(
                    PackValue.AsF32(key.Fields.GetValueOrDefault("time")),
                    PackValue.AsF32(key.Fields.GetValueOrDefault("value")),
                    PackValue.AsF32(key.Fields.GetValueOrDefault("inTangent")),
                    PackValue.AsF32(key.Fields.GetValueOrDefault("outTangent"))));
        return result;
    }

    private static IReadOnlyList<Gw2SceneFlagKey> FlagKeys(object? value)
    {
        if (value is not object?[] values) return [];
        var result = new List<Gw2SceneFlagKey>(values.Length);
        foreach (var item in values)
            if (item is PackObject key)
                result.Add(new(
                    PackValue.AsF32(key.Fields.GetValueOrDefault("time")),
                    PackValue.AsF32(key.Fields.GetValueOrDefault("value"))));
        return result;
    }

    private static IReadOnlyList<Gw2SceneTriggerKey> TriggerKeys(object? value)
    {
        if (value is not object?[] values) return [];
        var result = new List<Gw2SceneTriggerKey>(values.Length);
        foreach (var item in values)
            if (item is PackObject key)
                result.Add(new(
                    PackValue.AsF32(key.Fields.GetValueOrDefault("time")),
                    PackValue.AsU64(key.Fields.GetValueOrDefault("token1")),
                    PackValue.AsU64(key.Fields.GetValueOrDefault("token2")),
                    PackValue.AsF32(key.Fields.GetValueOrDefault("value1")),
                    PackValue.AsF32(key.Fields.GetValueOrDefault("value2")))
                {
                    Flags1 = PackValue.AsU8(key.Fields.GetValueOrDefault("flags1")),
                    Flags2 = PackValue.AsU8(key.Fields.GetValueOrDefault("flags2")),
                    Flags3 = PackValue.AsU8(key.Fields.GetValueOrDefault("flags3")),
                    Flags4 = PackValue.AsU8(key.Fields.GetValueOrDefault("flags4")),
                    Value3 = PackValue.AsF32(key.Fields.GetValueOrDefault("value3")),
                    Value4 = PackValue.AsF32(key.Fields.GetValueOrDefault("value4")),
                });
        return result;
    }
}
