using Gw2.Formats;
namespace Gw2.Dat.Content;

public sealed class Gw2Waveform
{
    public Gw2Waveform(
        float length,
        float offset,
        uint crc,
        uint sampleCount,
        uint loopStart,
        uint loopEnd,
        uint flags,
        uint format,
        uint channelCount,
        byte[] audioData,
        byte[] otherData)
    {
        Length = length;
        Offset = offset;
        Crc = crc;
        SampleCount = sampleCount;
        LoopStart = loopStart;
        LoopEnd = loopEnd;
        Flags = flags;
        Format = format;
        ChannelCount = channelCount;
        AudioData = audioData;
        OtherData = otherData;
    }

    public float Length { get; }
    public float Offset { get; }
    public uint Crc { get; }
    public uint SampleCount { get; }
    public uint LoopStart { get; }
    public uint LoopEnd { get; }
    public uint Flags { get; }
    public uint Format { get; }
    public uint ChannelCount { get; }
    public byte[] AudioData { get; }
    public byte[] OtherData { get; }

    public static Gw2Waveform? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.FindRoot("WaveformData");
        if (root is null) return null;

        return new(
            PackValue.AsF32(root.Fields.GetValueOrDefault("length")),
            PackValue.AsF32(root.Fields.GetValueOrDefault("offset")),
            PackValue.AsU32(root.Fields.GetValueOrDefault("crc")),
            PackValue.AsU32(root.Fields.GetValueOrDefault("numSamples")),
            PackValue.AsU32(root.Fields.GetValueOrDefault("loopStart")),
            PackValue.AsU32(root.Fields.GetValueOrDefault("loopEnd")),
            PackValue.AsU32(root.Fields.GetValueOrDefault("flags")),
            PackValue.AsU32(root.Fields.GetValueOrDefault("format")),
            PackValue.AsU32(root.Fields.GetValueOrDefault("numChannels")),
            PackValue.AsBytes(root.Fields.GetValueOrDefault("audioData")),
            PackValue.AsBytes(root.Fields.GetValueOrDefault("otherData")));
    }
}
