using System.Buffers.Binary;

namespace Gw2.Formats;

public static class ArchiveEntry
{
    private const int CrcChunkSize = 0x10000;
    private const int CrcSize = 4;

    public static byte[] StripCrc32(ReadOnlySpan<byte> raw)
    {
        if (raw.Length == 0)
            return [];

        if (raw.Length == CrcChunkSize)
            return raw[..^CrcSize].ToArray();

        if (raw.Length < CrcChunkSize)
            return raw.Length > CrcSize ? raw[..^CrcSize].ToArray() : [];

        var blockCount = raw.Length / CrcChunkSize;
        var intermediateLength = checked(raw.Length - blockCount * CrcSize);
        var remainderLength = raw.Length % CrcChunkSize;
        if (remainderLength is > 0 and < CrcSize)
            throw new InvalidDataException("truncated GW2 CRC word");
        var outputLength = intermediateLength - (remainderLength == 0 ? 0 : CrcSize);
        var output = new byte[outputLength];
        var written = 0;

        for (var block = 0; block < blockCount && written < output.Length; block++)
        {
            var source = raw.Slice(block * CrcChunkSize, CrcChunkSize - CrcSize);
            var count = Math.Min(source.Length, output.Length - written);
            source[..count].CopyTo(output.AsSpan(written));
            written += count;
        }

        var remainderOffset = blockCount * CrcChunkSize;
        if (remainderOffset < raw.Length && written < output.Length)
        {
            var remainder = raw[remainderOffset..];
            var count = Math.Min(remainder.Length, output.Length - written);
            remainder[..count].CopyTo(output.AsSpan(written));
        }

        return output;
    }

    public static void DecodeEntry(ReadOnlySpan<byte> rawEntry, ushort compression, Stream destination)
        => DecodeEntry(rawEntry, compression, destination, []);

    public static void DecodeEntry(
        ReadOnlySpan<byte> rawEntry,
        ushort compression,
        Stream destination,
        ReadOnlySpan<byte> oldSource)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (compression == 0)
        {
            destination.Write(StripCrc32(rawEntry));
            return;
        }

        if (compression != 8)
            throw new NotSupportedException($"GW2 compression flag 0x{compression:X4} is not supported");
        var stripped = StripCrc32(rawEntry);
        if (stripped.Length < 8)
            throw new InvalidDataException("compressed GW2 entry is missing its 8-byte header");

        var expectedSize = BinaryPrimitives.ReadUInt32LittleEndian(stripped.AsSpan(4, 4));
        DecodeCompressed(stripped.AsSpan(8), expectedSize, oldSource, destination);
    }

    public static void DecodeCompressed(
        ReadOnlySpan<byte> compressed,
        uint outputSize,
        ReadOnlySpan<byte> oldSource,
        Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var output = new byte[outputSize];
        if (!CmpApi.Decompress(output, compressed, oldSource))
            throw new InvalidDataException("CmpDecompress(): corrupt data");
        destination.Write(output);
    }

    public static bool TryDecodeCompressedPrefix(ReadOnlySpan<byte> compressed, Span<byte> prefix) =>
        CmpApi.DecompressPrefix(prefix, compressed);
}
