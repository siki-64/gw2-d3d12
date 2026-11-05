using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Gw2.Client.Model;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2PngEncoderTests
{
    [TestMethod]
    public void RgbaRowsRoundTripThroughIdat()
    {
        const int width = 3;
        const int height = 2;
        var rgba = Enumerable.Range(0, width * height * 4).Select(index => (byte)(index * 7)).ToArray();

        var png = Gw2PngEncoder.EncodeRgba(width, height, rgba);

        CollectionAssert.AreEqual(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A }, png[..8]);
        var header = Chunk(png, "IHDR");
        Assert.AreEqual((uint)width, BinaryPrimitives.ReadUInt32BigEndian(header));
        Assert.AreEqual((uint)height, BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4)));
        Assert.AreEqual(8, header[8]);
        Assert.AreEqual(6, header[9]);

        using var inflated = new MemoryStream();
        using (var zlib = new ZLibStream(new MemoryStream(Chunk(png, "IDAT")), CompressionMode.Decompress))
            zlib.CopyTo(inflated);
        var rows = inflated.ToArray();
        Assert.HasCount(height * (1 + width * 4), rows);
        for (var row = 0; row < height; row++)
        {
            Assert.AreEqual(0, rows[row * (1 + width * 4)]);
            CollectionAssert.AreEqual(rgba.AsSpan(row * width * 4, width * 4).ToArray(), rows.AsSpan(row * (1 + width * 4) + 1, width * 4).ToArray());
        }
    }

    [TestMethod]
    public void WrongPixelCountThrows()
    {
        Assert.ThrowsExactly<ArgumentException>(() => Gw2PngEncoder.EncodeRgba(2, 2, new byte[15]));
    }

    private static byte[] Chunk(byte[] png, string type)
    {
        var offset = 8;
        while (offset < png.Length)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset));
            if (Encoding.ASCII.GetString(png, offset + 4, 4) == type) return png.AsSpan(offset + 8, length).ToArray();
            offset += 12 + length;
        }
        throw new InvalidDataException($"No {type} chunk.");
    }
}
