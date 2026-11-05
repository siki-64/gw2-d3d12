using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace Gw2.Client.Model;

public static class Gw2PngEncoder
{
    private const int RgbaBytesPerPixel = 4;

    private static readonly byte[] Signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(index =>
    {
        var value = (uint)index;
        for (var bit = 0; bit < 8; bit++) value = (value & 1) != 0 ? 0xEDB88320 ^ (value >> 1) : value >> 1;
        return value;
    }).ToArray();

    public static byte[] EncodeRgba(int width, int height, ReadOnlySpan<byte> rgba)
    {
        var rowBytes = width * RgbaBytesPerPixel;
        if (rgba.Length != rowBytes * height)
            throw new ArgumentException($"Expected {rowBytes * height} RGBA bytes for {width}x{height}, got {rgba.Length}.", nameof(rgba));

        var rows = new byte[height * (1 + rowBytes)];
        for (var row = 0; row < height; row++)
            rgba.Slice(row * rowBytes, rowBytes).CopyTo(rows.AsSpan(row * (1 + rowBytes) + 1));

        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)height);
        header[8] = 8;
        header[9] = 6;

        using var png = new MemoryStream();
        png.Write(Signature);
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", Zlib(rows));
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WriteChunk(Stream png, string type, byte[] data)
    {
        var typeBytes = Encoding.ASCII.GetBytes(type);
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(word, (uint)data.Length);
        png.Write(word);
        png.Write(typeBytes);
        png.Write(data);
        BinaryPrimitives.WriteUInt32BigEndian(word, Crc32(typeBytes, data));
        png.Write(word);
    }

    private static byte[] Zlib(byte[] data)
    {
        using var output = new MemoryStream();
        output.WriteByte(0x78);
        output.WriteByte(0x9C);
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, true))
            deflate.Write(data, 0, data.Length);
        uint low = 1, high = 0;
        foreach (var value in data)
        {
            low = (low + value) % 65521;
            high = (high + low) % 65521;
        }
        var adler = (high << 16) | low;
        output.WriteByte((byte)(adler >> 24));
        output.WriteByte((byte)(adler >> 16));
        output.WriteByte((byte)(adler >> 8));
        output.WriteByte((byte)adler);
        return output.ToArray();
    }

    private static uint Crc32(byte[] type, byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in type) crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        foreach (var value in data) crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFF;
    }
}
