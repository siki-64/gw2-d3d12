using Gw2.Formats;
using System.Numerics;

namespace Gw2.Dat.Content;

public sealed record Gw2AudioRange(float Max, float Min, byte RangeMode);

public sealed record Gw2AudioEnvelopePoint(float Offset, float Value);

public sealed record Gw2AudioEnvelope(
    float InputOffset,
    ulong InputOffsetProperty,
    ulong InputParameter,
    float InputScale,
    ulong InputScaleProperty,
    byte InputType,
    ulong OffsetParameter,
    byte OffsetType,
    float OutputOffset,
    ulong OutputOffsetProperty,
    float OutputScale,
    ulong OutputScaleProperty,
    IReadOnlyList<Gw2AudioEnvelopePoint> Points);

public sealed record Gw2AudioRandomParam(Gw2AudioRange Time, Gw2AudioRange Value);

public sealed record Gw2AudioDynamicParam(float Value, byte Type, Gw2AudioEnvelope? Envelope, Gw2AudioRandomParam? Random);

public sealed record Gw2AudioAttenuation(
    float Doppler,
    Gw2AudioDynamicParam LowPass,
    Gw2AudioDynamicParam HighPass,
    Gw2AudioDynamicParam Pan3D,
    Gw2AudioDynamicParam Reverb,
    Gw2AudioDynamicParam Spread3D,
    Gw2AudioDynamicParam VolumeA,
    Gw2AudioDynamicParam VolumeB,
    Gw2AudioDynamicParam Lfe);

public sealed record Gw2AudioDsp(uint Type, uint Flags, float[] Properties);

public sealed record Gw2AudioFileName(
    ulong Condition,
    ulong Language,
    float Volume,
    float Weight,
    uint FileId,
    byte AudioType,
    byte NoteBase,
    byte NoteMin,
    byte NoteMax);

public sealed record Gw2AudioProperty(ulong Name, ulong TokenValue, float FloatValue);

public sealed record Gw2AudioScriptRef(ulong Name, uint FileId);

public sealed record Gw2AudioHandler(ulong Name, uint Flags, byte[] ByteCode);

public sealed record Gw2AudioTriggerMarker(ulong Cue, ulong End, float Time, byte Type);

public sealed record Gw2AudioTriggerKey(ulong Name, IReadOnlyList<Gw2AudioTriggerMarker> Markers);

public sealed record Gw2AudioMetaSound(
    ulong Name,
    ulong Category,
    ulong EndCue,
    ulong OffsetBone,
    ulong PlaylistId,
    IReadOnlyList<Gw2AudioDsp> Dsp,
    Gw2AudioAttenuation? Attenuation,
    IReadOnlyList<Gw2AudioFileName> Files,
    float ChannelFadeIn,
    float ChannelFadeOut,
    float EndCueOffset,
    float FadeInTime,
    float FadeOutTime,
    float ModelScaleSizeFactor,
    Vector3 PositionOffset,
    uint ChannelMax,
    uint Flags,
    uint LoopCount,
    Gw2AudioDynamicParam Depth,
    Gw2AudioDynamicParam Pan,
    Gw2AudioDynamicParam Pitch,
    Gw2AudioDynamicParam PitchMultiSample,
    Gw2AudioDynamicParam Volume,
    Gw2AudioDynamicParam VolumeMultiSample,
    Gw2AudioRange InitialDelay,
    Gw2AudioRange PlayLength,
    Gw2AudioRange PositionOffsetAngle,
    Gw2AudioRange PositionRange,
    Gw2AudioRange RepeatCount,
    Gw2AudioRange RepeatTime,
    Gw2AudioRange ReplayDelay,
    Gw2AudioRange StartTimeOffset,
    byte ChannelMode,
    byte ChannelPriority,
    byte FileIterateMode,
    byte LoopMode,
    byte MusicPriority,
    byte PlaybackMode,
    byte PositionMode,
    byte RepeatTimeFrom);

public sealed record Gw2AudioBussDynamic(ulong Name, uint Flags, float Volume, IReadOnlyList<Gw2AudioDsp> Dsp);

public sealed record Gw2AudioBuss(
    ulong Name,
    ulong Output,
    uint Flags,
    float NormalizeFadeTime,
    float NormalizeThreshold,
    float NormalizeMaxAmp,
    float CompressorThreshold,
    float CompressorAttack,
    float CompressorRelease,
    float CompressorGainMakeup,
    Gw2AudioBussDynamic? DynamicData);

public sealed record Gw2AudioCategoryDynamic(
    ulong Name,
    float Volume,
    float NonFocusGain,
    float LowPass,
    float HighPass,
    float ReverbDirect,
    float ReverbRoom,
    uint Flags,
    uint MinAudible,
    uint MaxAudibleLowQuality,
    uint MaxAudibleHighQuality);

public sealed record Gw2AudioCategory(
    ulong Name,
    ulong VolumeGroupName,
    ulong OutputBussName,
    Gw2AudioAttenuation? Attenuation,
    Gw2AudioCategoryDynamic? DynamicData,
    float FocusReserve,
    float MuteFadeTime,
    uint Flags,
    uint MaxAudible,
    byte MaxAudibleBehavior,
    byte Priority);

public sealed record Gw2AudioMaterial(ulong Name, uint Flags, float AbsorptionLow, float AbsorptionMid, float AbsorptionHigh, float Occlusion);

public sealed record Gw2AudioMusicCondition(ulong Name, uint Flags, byte[] ByteCode);

public sealed record Gw2AudioMusicPlaylist(
    ulong Name,
    ulong Category,
    ulong PrimaryPlaylistId,
    ulong SecondaryPlaylistId,
    IReadOnlyList<Gw2AudioFileName> Files,
    float FadeInTime,
    float FadeOutTime,
    uint Flags,
    Gw2AudioRange InitialSilence,
    Gw2AudioRange IntervalSilence,
    Gw2AudioRange MaxPlayLength,
    Gw2AudioDynamicParam Volume,
    byte FileIterateMode);

public sealed record Gw2AudioReverb(
    ulong Name,
    uint Flags,
    float Room,
    float RoomHF,
    float RoomLF,
    float DecayTime,
    float DecayHFRatio,
    float Reflections,
    float ReflectionsDelay,
    float Reverb,
    float ReverbDelay,
    float ReferenceHF,
    float ReferenceLF,
    float Diffusion,
    float Density,
    float EchoDelay,
    float EchoDecayRatio,
    float EchoWetMix,
    float EchoDryMix);

public sealed record Gw2AudioSnapshot(
    ulong Name,
    float BlendInTime,
    float BlendOutTime,
    uint Flags,
    IReadOnlyList<Gw2AudioBussDynamic> Busses,
    IReadOnlyList<Gw2AudioCategoryDynamic> Categories,
    byte Priority);

public sealed record Gw2AudioMusicExternal(ulong Name, string ExternalPlaylist);

public sealed record Gw2AudioSettings(
    ulong DefaultSnapshot,
    ulong EffectsBuss,
    float DistanceScale,
    float DopplerScale,
    float EchoLevel,
    float FocusTransition,
    float MemoryPool,
    float ReverbLevel,
    uint MinChannelsLowQuality,
    uint MaxChannelsLowQuality,
    IReadOnlyList<Gw2AudioBuss> Busses,
    IReadOnlyList<Gw2AudioCategory> Categories,
    IReadOnlyList<Gw2AudioMaterial> Materials,
    IReadOnlyList<Gw2AudioMusicCondition> MusicConditions,
    IReadOnlyList<Gw2AudioMusicPlaylist> MusicPlaylists,
    IReadOnlyList<Gw2AudioProperty> Properties,
    IReadOnlyList<Gw2AudioReverb> Reverbs,
    IReadOnlyList<Gw2AudioScriptRef> ScriptRefs,
    IReadOnlyList<Gw2AudioSnapshot> Snapshots,
    uint BankIndexFileId,
    uint BankScriptFileId,
    uint MusicScriptFileId,
    IReadOnlyList<Gw2AudioMusicExternal> MusicExternals);

public sealed record Gw2AudioScript(
    ulong MusicCue,
    ulong ReverbOverride,
    ulong Snapshot,
    Gw2AudioSettings? Settings,
    IReadOnlyList<Gw2AudioHandler> Handlers,
    IReadOnlyList<Gw2AudioMetaSound> MetaSounds,
    IReadOnlyList<Gw2AudioScriptRef> ScriptRefs,
    IReadOnlyList<Gw2AudioTriggerKey> TriggerKeys,
    IReadOnlyList<Gw2AudioProperty> Properties,
    uint Flags,
    uint SoundPoolCount,
    float FadeInTime,
    float SoundPoolDelay,
    float Volume,
    byte MusicCuePriority,
    byte MusicMutePriority,
    byte SoundPoolMode,
    byte Interpolation,
    float RadialLimit,
    Gw2AudioRange ScaleBucketRange,
    byte MultipointGranularity)
{
    public static Gw2AudioScript? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.FindRoot("ScriptFileData");
        if (root is null) return null;

        return new(
            U64(root, "musicCue"),
            U64(root, "reverbOverride"),
            U64(root, "snapshot"),
            Child(root, "audioSettings") is { } settings ? ReadSettings(settings) : null,
            List(root, "handler", handler => new Gw2AudioHandler(U64(handler, "name"), U32(handler, "flags"), PackValue.AsBytes(Value(handler, "byteCode")))),
            List(root, "metaSound", ReadMetaSound),
            List(root, "scriptRef", ReadScriptRef),
            List(root, "triggerKey", key => new Gw2AudioTriggerKey(
                U64(key, "name"),
                List(key, "triggerMarker", marker => new Gw2AudioTriggerMarker(U64(marker, "cue"), U64(marker, "end"), F32(marker, "time"), U8(marker, "type"))))),
            List(root, "property", ReadProperty),
            U32(root, "flags"),
            U32(root, "soundPoolCount"),
            F32(root, "fadeInTime"),
            F32(root, "soundPoolDelay"),
            F32(root, "volume"),
            U8(root, "musicCuePriority"),
            U8(root, "musicMutePriority"),
            U8(root, "soundPoolMode"),
            U8(root, "interpolation"),
            F32(root, "radialLimit"),
            ReadRange(Child(root, "scaleBucketRange")),
            U8(root, "multipointGranularity"));
    }

    private static Gw2AudioSettings ReadSettings(PackObject settings) => new(
        U64(settings, "defaultSnapshot"),
        U64(settings, "effectsBuss"),
        F32(settings, "distanceScale"),
        F32(settings, "dopplerScale"),
        F32(settings, "echoLevel"),
        F32(settings, "focusTransition"),
        F32(settings, "memoryPool"),
        F32(settings, "reverbLevel"),
        U32(settings, "minChannelsLQ"),
        U32(settings, "maxChannelsLQ"),
        List(settings, "buss", ReadBuss),
        List(settings, "category", ReadCategory),
        List(settings, "material", material => new Gw2AudioMaterial(
            U64(material, "name"),
            U32(material, "flags"),
            F32(material, "absorptionLF"),
            F32(material, "absorptionMF"),
            F32(material, "absorptionHF"),
            F32(material, "occlusion"))),
        List(settings, "musicCondition", condition => new Gw2AudioMusicCondition(
            U64(condition, "name"),
            U32(condition, "flags"),
            PackValue.AsBytes(Value(condition, "byteCode")))),
        List(settings, "musicPlaylist", ReadMusicPlaylist),
        List(settings, "property", ReadProperty),
        List(settings, "reverb", ReadReverb),
        List(settings, "scriptRef", ReadScriptRef),
        List(settings, "snapshot", snapshot => new Gw2AudioSnapshot(
            U64(snapshot, "name"),
            F32(snapshot, "blendInTime"),
            F32(snapshot, "blendOutTime"),
            U32(snapshot, "flags"),
            List(snapshot, "buss", ReadBussDynamic),
            List(snapshot, "category", ReadCategoryDynamic),
            U8(snapshot, "priority"))),
        FileId(settings, "bankIndexFileName"),
        FileId(settings, "bankScriptFileName"),
        FileId(settings, "musicScriptFileName"),
        List(settings, "musicExternal", external => new Gw2AudioMusicExternal(
            U64(external, "name"),
            Value(external, "externalPlaylist") as string ?? string.Empty)));

    private static Gw2AudioBuss ReadBuss(PackObject buss) => new(
        U64(buss, "name"),
        U64(buss, "output"),
        U32(buss, "flags"),
        F32(buss, "normalizeFadeTime"),
        F32(buss, "normalizeThreshold"),
        F32(buss, "normalizeMaxAmp"),
        F32(buss, "compressorThreshold"),
        F32(buss, "compressorAttack"),
        F32(buss, "compressorRelease"),
        F32(buss, "compressorGainMakeup"),
        Child(buss, "dynamicData") is { } dynamicData ? ReadBussDynamic(dynamicData) : null);

    private static Gw2AudioBussDynamic ReadBussDynamic(PackObject buss) => new(
        U64(buss, "name"),
        U32(buss, "flags"),
        F32(buss, "volume"),
        List(buss, "dsp", ReadDsp));

    private static Gw2AudioDsp ReadDsp(PackObject dsp) => new(
        U32(dsp, "type"),
        U32(dsp, "flags"),
        PackValue.AsFloats(Value(dsp, "property")));

    private static Gw2AudioCategory ReadCategory(PackObject category) => new(
        U64(category, "name"),
        U64(category, "volumeGroupName"),
        U64(category, "outputBussName"),
        Child(category, "attenuation") is { } attenuation ? ReadAttenuation(attenuation) : null,
        Child(category, "dynamicData") is { } dynamicData ? ReadCategoryDynamic(dynamicData) : null,
        F32(category, "focusReserve"),
        F32(category, "muteFadeTime"),
        U32(category, "flags"),
        U32(category, "maxAudible"),
        U8(category, "maxAudibleBehavior"),
        U8(category, "priority"));

    private static Gw2AudioCategoryDynamic ReadCategoryDynamic(PackObject category) => new(
        U64(category, "name"),
        F32(category, "volume"),
        F32(category, "nonFocusGain"),
        F32(category, "lowPass"),
        F32(category, "highPass"),
        F32(category, "reverbDirect"),
        F32(category, "reverbRoom"),
        U32(category, "flags"),
        U32(category, "minAudible"),
        U32(category, "maxAudibleLQ"),
        U32(category, "maxAudibleHG"));

    private static Gw2AudioMusicPlaylist ReadMusicPlaylist(PackObject playlist) => new(
        U64(playlist, "name"),
        U64(playlist, "category"),
        U64(playlist, "primaryPlaylistId"),
        U64(playlist, "secondaryPlaylistId"),
        List(playlist, "fileName", ReadFileName),
        F32(playlist, "fadeInTime"),
        F32(playlist, "fadeOutTime"),
        U32(playlist, "flags"),
        ReadRange(Child(playlist, "initialSilence")),
        ReadRange(Child(playlist, "intervalSilence")),
        ReadRange(Child(playlist, "maxPlayLength")),
        ReadDynamicParam(Child(playlist, "volume")),
        U8(playlist, "fileIterateMode"));

    private static Gw2AudioReverb ReadReverb(PackObject reverb) => new(
        U64(reverb, "name"),
        U32(reverb, "flags"),
        F32(reverb, "room"),
        F32(reverb, "roomHF"),
        F32(reverb, "roomLF"),
        F32(reverb, "decayTime"),
        F32(reverb, "decayHFRatio"),
        F32(reverb, "reflections"),
        F32(reverb, "reflectionsDelay"),
        F32(reverb, "reverb"),
        F32(reverb, "reverbDelay"),
        F32(reverb, "referenceHF"),
        F32(reverb, "referenceLF"),
        F32(reverb, "diffusion"),
        F32(reverb, "density"),
        F32(reverb, "echoDelay"),
        F32(reverb, "echoDecayRatio"),
        F32(reverb, "echoWetMix"),
        F32(reverb, "echoDryMix"));

    private static Gw2AudioMetaSound ReadMetaSound(PackObject sound) => new(
        U64(sound, "name"),
        U64(sound, "category"),
        U64(sound, "endCue"),
        U64(sound, "offsetBone"),
        U64(sound, "playlistId"),
        List(sound, "dsp", ReadDsp),
        Child(sound, "attenuation") is { } attenuation ? ReadAttenuation(attenuation) : null,
        List(sound, "fileName", ReadFileName),
        F32(sound, "channelFadeIn"),
        F32(sound, "channelFadeOut"),
        F32(sound, "endCueOffset"),
        F32(sound, "fadeInTime"),
        F32(sound, "fadeOutTime"),
        F32(sound, "modelScaleSizeFactor"),
        Value(sound, "positionOffset") is float[] { Length: >= 3 } offset ? new Vector3(offset[0], offset[1], offset[2]) : Vector3.Zero,
        U32(sound, "channelMax"),
        U32(sound, "flags"),
        U32(sound, "loopCount"),
        ReadDynamicParam(Child(sound, "depth")),
        ReadDynamicParam(Child(sound, "pan")),
        ReadDynamicParam(Child(sound, "pitch")),
        ReadDynamicParam(Child(sound, "pitchMS")),
        ReadDynamicParam(Child(sound, "volume")),
        ReadDynamicParam(Child(sound, "volumeMS")),
        ReadRange(Child(sound, "initialDelay")),
        ReadRange(Child(sound, "playLength")),
        ReadRange(Child(sound, "positionOffsetAngle")),
        ReadRange(Child(sound, "positionRange")),
        ReadRange(Child(sound, "repeatCount")),
        ReadRange(Child(sound, "repeatTime")),
        ReadRange(Child(sound, "replayDelay")),
        ReadRange(Child(sound, "startTimeOffset")),
        U8(sound, "channelMode"),
        U8(sound, "channelPriority"),
        U8(sound, "fileIterateMode"),
        U8(sound, "loopMode"),
        U8(sound, "musicPriority"),
        U8(sound, "playbackMode"),
        U8(sound, "positionMode"),
        U8(sound, "repeatTimeFrom"));

    private static Gw2AudioAttenuation ReadAttenuation(PackObject attenuation) => new(
        F32(attenuation, "doppler"),
        ReadDynamicParam(Child(attenuation, "lowPass")),
        ReadDynamicParam(Child(attenuation, "highPass")),
        ReadDynamicParam(Child(attenuation, "pan3D")),
        ReadDynamicParam(Child(attenuation, "reverb")),
        ReadDynamicParam(Child(attenuation, "spread3D")),
        ReadDynamicParam(Child(attenuation, "volumeA")),
        ReadDynamicParam(Child(attenuation, "volumeB")),
        ReadDynamicParam(Child(attenuation, "lfe")));

    private static Gw2AudioDynamicParam ReadDynamicParam(PackObject? param) => param is null
        ? new(0, 0, null, null)
        : new(
            F32(param, "value"),
            U8(param, "type"),
            Child(param, "envelopeData") is { } envelope ? ReadEnvelope(envelope) : null,
            Child(param, "randomParamData") is { } random ? new Gw2AudioRandomParam(ReadRange(Child(random, "time")), ReadRange(Child(random, "value"))) : null);

    private static Gw2AudioEnvelope ReadEnvelope(PackObject envelope) => new(
        F32(envelope, "inputOffset"),
        U64(envelope, "inputOffsetProperty"),
        U64(envelope, "inputParameter"),
        F32(envelope, "inputScale"),
        U64(envelope, "inputScaleProperty"),
        U8(envelope, "inputType"),
        U64(envelope, "offsetParameter"),
        U8(envelope, "offsetType"),
        F32(envelope, "outputOffset"),
        U64(envelope, "outputOffsetProperty"),
        F32(envelope, "outputScale"),
        U64(envelope, "outputScaleProperty"),
        List(envelope, "envelopePoint", point => new Gw2AudioEnvelopePoint(F32(point, "offset"), F32(point, "value"))));

    private static Gw2AudioRange ReadRange(PackObject? range) => range is null
        ? new(0, 0, 0)
        : new(F32(range, "max"), F32(range, "min"), U8(range, "min#2"));

    private static Gw2AudioFileName ReadFileName(PackObject file) => new(
        U64(file, "condition"),
        U64(file, "language"),
        F32(file, "volume"),
        F32(file, "weight"),
        FileId(file, "fileName"),
        U8(file, "audioType"),
        U8(file, "noteBase"),
        U8(file, "noteMin"),
        U8(file, "noteMax"));

    private static Gw2AudioProperty ReadProperty(PackObject property) =>
        new(U64(property, "name"), U64(property, "tokenValue"), F32(property, "floatValue"));

    private static Gw2AudioScriptRef ReadScriptRef(PackObject scriptRef) =>
        new(U64(scriptRef, "name"), FileId(scriptRef, "fileName"));

    private static IReadOnlyList<T> List<T>(PackObject owner, string name, Func<PackObject, T> read) =>
        PackValue.AsObjects(Value(owner, name)).Select(read).ToArray();

    private static object? Value(PackObject owner, string name) => owner.Optional(name);

    private static PackObject? Child(PackObject owner, string name) => Value(owner, name) as PackObject;

    private static uint FileId(PackObject owner, string name) =>
        Value(owner, name) is PackFileReference reference && reference.TryGetFileId(out var fileId) ? fileId : 0;

    private static ulong U64(PackObject owner, string name) => PackValue.OptionalU64(owner, name) ?? 0;

    private static uint U32(PackObject owner, string name) => PackValue.OptionalU32(owner, name) ?? 0;

    private static byte U8(PackObject owner, string name) => (byte)U32(owner, name);

    private static float F32(PackObject owner, string name) => PackValue.OptionalF32(owner, name) ?? 0;
}
