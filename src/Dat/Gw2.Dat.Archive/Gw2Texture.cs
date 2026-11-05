using System.Buffers.Binary;
using System.Text;

namespace Gw2.Dat.Archive;

public sealed record Gw2TextureMip(int Width, int Height, uint EncodingFlags, ReadOnlyMemory<byte> EncodedBytes);

public sealed record Gw2Texture(string Container, string Format, int Width, int Height, IReadOnlyList<Gw2TextureMip> Mips)
{
    public static bool LooksLike(ReadOnlySpan<byte> bytes) => bytes.Length >= 4 &&
        (bytes[..4].SequenceEqual("ATEX"u8) || bytes[..4].SequenceEqual("ATTX"u8) ||
         bytes[..4].SequenceEqual("ATEC"u8) || bytes[..4].SequenceEqual("ATEP"u8) ||
         bytes[..4].SequenceEqual("ATEU"u8) || bytes[..4].SequenceEqual("ATET"u8));

    public static Gw2Texture Parse(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        if (span.Length < 12 || !LooksLike(span)) throw new InvalidDataException("Missing ArenaNet texture header");
        int width = BinaryPrimitives.ReadUInt16LittleEndian(span[8..]);
        int height = BinaryPrimitives.ReadUInt16LittleEndian(span[10..]);
        if (width == 0 || height == 0) throw new InvalidDataException("Zero-sized texture");
        int w = width, h = height, offset = 12;
        var mips = new List<Gw2TextureMip>();
        int maxMips = 1 + Gw2Binary.Log2((uint)Math.Max(width, height));
        while (offset < span.Length)
        {
            if (span.Length - offset < 8 || mips.Count >= maxMips) throw new InvalidDataException("Invalid texture mip chain");
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
            if (size < 8 || size > span.Length - offset || size % 4 != 0) throw new InvalidDataException("Invalid texture mip size");
            mips.Add(new(w, h, BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 4)..]), data.Slice(offset, (int)size)));
            offset += (int)size;
            w = Math.Max(1, w / 2); h = Math.Max(1, h / 2);
        }
        if (mips.Count == 0) throw new InvalidDataException("Texture has no mip levels");
        return new(Encoding.ASCII.GetString(span[..4]), Encoding.ASCII.GetString(span[4..8]), width, height, mips.AsReadOnly());
    }

    public Gw2AtexFormat? AtexFormat => Gw2AtexFormat.Of(Format);

    public byte[] DecodeGpuBlocks(int mipLevel = 0, int maxOutputBytes = 256 * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxOutputBytes);
        if ((uint)mipLevel >= Mips.Count) throw new ArgumentOutOfRangeException(nameof(mipLevel));
        var format = AtexFormat ?? throw new NotSupportedException($"Texture format {Format} is not an ATEX format");
        return Gw2AtexDecoder.Decode(format, Mips[mipLevel], maxOutputBytes);
    }
}
