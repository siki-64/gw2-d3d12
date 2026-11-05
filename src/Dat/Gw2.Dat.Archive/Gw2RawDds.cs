using System.Buffers.Binary;
using System.Text;

namespace Gw2.Dat.Archive;

public sealed record Gw2RawDds(
    string FourCc,
    uint? DxgiFormat,
    int Width,
    int Height,
    int MipCount,
    bool IsUncompressed,
    int BitCount = 0,
    uint RBitMask = 0,
    uint GBitMask = 0,
    uint BBitMask = 0,
    uint ABitMask = 0,
    bool HoldsSignedOffsets = false)
{
    private const uint DdsdMipMapCount = 0x20000;
    private const uint DdpfPixelFormat = 0x4;
    private const uint DdpfRgb = 0x40;
    private const uint DdpfBumpDuDv = 0x80000;

    public static bool LooksLike(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 128 && bytes[..4].SequenceEqual("DDS "u8);

    public static bool TryParse(ReadOnlyMemory<byte> data, out Gw2RawDds dds)
    {
        dds = null!;
        var span = data.Span;
        if (!LooksLike(span) || BinaryPrimitives.ReadUInt32LittleEndian(span[4..]) != 124) return false;

        var flags = BinaryPrimitives.ReadUInt32LittleEndian(span[8..]);
        var height = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(span[12..]));
        var width = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(span[16..]));
        if (width <= 0 || height <= 0) return false;

        var mipCount = (flags & DdsdMipMapCount) != 0
            ? checked((int)BinaryPrimitives.ReadUInt32LittleEndian(span[28..]))
            : 1;
        if (mipCount <= 0) mipCount = 1;

        var pixelFlags = BinaryPrimitives.ReadUInt32LittleEndian(span[80..]);
        var fourCc = ReadFourCc(span[84..88]);
        uint? dxgi = fourCc == "DX10" && span.Length >= 148
            ? BinaryPrimitives.ReadUInt32LittleEndian(span[128..])
            : null;
        var signedOffsets = (pixelFlags & DdpfPixelFormat) == 0 && (pixelFlags & DdpfBumpDuDv) != 0;
        var uncompressed = (pixelFlags & DdpfPixelFormat) == 0 && ((pixelFlags & DdpfRgb) != 0 || signedOffsets);
        var bitCount = uncompressed ? checked((int)BinaryPrimitives.ReadUInt32LittleEndian(span[88..])) : 0;
        var rMask = uncompressed ? BinaryPrimitives.ReadUInt32LittleEndian(span[92..]) : 0;
        var gMask = uncompressed ? BinaryPrimitives.ReadUInt32LittleEndian(span[96..]) : 0;
        var bMask = uncompressed ? BinaryPrimitives.ReadUInt32LittleEndian(span[100..]) : 0;
        var aMask = uncompressed ? BinaryPrimitives.ReadUInt32LittleEndian(span[104..]) : 0;

        dds = new(fourCc, dxgi, width, height, mipCount, uncompressed, bitCount, rMask, gMask, bMask, aMask, signedOffsets);
        return true;
    }

    private static string ReadFourCc(ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
            if (value == 0) return string.Empty;
        return Encoding.ASCII.GetString(bytes);
    }
}
