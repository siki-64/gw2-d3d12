using System.Buffers.Binary;
using System.Text;

namespace Gw2.Dat.Archive;

public static class Gw2DatFileKind
{
    public const string Empty = "empty";
    public const string Pack = "pack";
    public const string Texture = "texture";
    public const string Dds = "dds";
    public const string Strings = "strings";
    public const string Audio = "audio";
    public const string Image = "image";
    public const string Font = "font";
    public const string Video = "video";
    public const string Flash = "flash";
    public const string Executable = "executable";
    public const string IcuData = "icu-data";
    public const string V8Snapshot = "v8-snapshot";
    public const string ChromiumPak = "chromium-pak";
    public const string Text = "text";
    public const string GlyphStream = "glyph-stream";
    public const string Unknown = "unknown";

    private static readonly HashSet<string> TextureMagics =
        ["ATEX", "ATTX", "ATEC", "ATEP", "ATEU", "ATET", "3DCX", "CTEX", "CTEU"];

    public static string Classify(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0) return Empty;
        if (bytes.Length >= 2 && bytes[..2].SequenceEqual("PF"u8)) return Pack;
        if (bytes.Length >= 3 && (bytes[..3].SequenceEqual("CWS"u8) || bytes[..3].SequenceEqual("FWS"u8))) return Flash;
        if (bytes.Length >= 3 && bytes[..3].SequenceEqual("KB2"u8)) return Video;
        if (bytes.Length >= 3 && bytes[..3].SequenceEqual("ID3"u8)) return Audio;
        if (bytes.Length >= 2 && bytes[0] == 0xFF && (bytes[1] & 0xE0) == 0xE0) return Audio;
        if (bytes.Length >= 2 && bytes[..2].SequenceEqual("MZ"u8)) return Executable;
        if (bytes.Length < 4) return LooksLikeText(bytes) ? Text : Unknown;
        var magic = Encoding.ASCII.GetString(bytes[..4]);
        if (TextureMagics.Contains(magic)) return Texture;
        var kind = magic switch
        {
            "DDS " => Dds,
            "RIFF" or "OggS" or "asnd" => Audio,
            "strs" => Strings,
            "OTTO" or "true" or "wOFF" => Font,
            "CmnD" => IcuData,
            _ => null
        };
        if (kind is not null) return kind;
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47])) return Image;
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF])) return Image;
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0x00, 0x01, 0x00, 0x00])) return Font;
        if (bytes[2] == 0xDA && bytes[3] == 0x27) return IcuData;
        if (bytes.Length >= 36 && bytes.Slice(34, 2).SequenceEqual("LP"u8)) return Font;
        if (IsV8Snapshot(bytes)) return V8Snapshot;
        if (IsChromiumPak(bytes)) return ChromiumPak;
        return LooksLikeText(bytes) ? Text : Unknown;
    }

    private const int V8VersionOffset = 12;

    private static bool IsV8Snapshot(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length <= V8VersionOffset) return false;
        var version = bytes[V8VersionOffset..];
        var end = version.IndexOf((byte)0);
        if (end <= 0) return false;
        var dots = 0;
        foreach (var value in version[..end])
        {
            if (value == (byte)'.') dots++;
            else if (value is < (byte)'0' or > (byte)'9') return false;
        }
        return dots == 3;
    }

    private static bool IsChromiumPak(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 12 && BinaryPrimitives.ReadUInt32LittleEndian(bytes) is 4 or 5 &&
        bytes[4] <= 2 && bytes[5] == 0 && bytes[6] == 0 && bytes[7] == 0;

    private const int TextProbeLength = 1024;

    public static bool LooksLikeText(ReadOnlySpan<byte> bytes)
    {
        var probe = bytes[..Math.Min(bytes.Length, TextProbeLength)];
        if (probe.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF])) probe = probe[3..];
        foreach (var value in probe)
            if (value < 0x20 && value is not (0x09 or 0x0A or 0x0C or 0x0D)) return false;
        return true;
    }
}
