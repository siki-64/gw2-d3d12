using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Gw2.Client.Model;
using Gw2.Dat.Content;
using Gw2.Dat.Archive;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2FontBakerTests
{
    private static Gw2FontGlyph Solid(int codePoint, int topOffset, int width, int height) =>
        new(codePoint, topOffset, width, height, Enumerable.Repeat((byte)0xFF, width * height).ToArray());

    private static List<Gw2FontGlyph> SampleGlyphs() => new()
    {
        Solid('1', 4, 6, 16),
        Solid('.', 18, 2, 2),
        Solid('A', 4, 10, 16),
        Solid('H', 4, 9, 16),
        new('V', 4, 10, 16, Enumerable.Range(0, 160).Select(index => (byte)(index % 10 >= index / 16 && index % 10 < 10 - index / 16 / 2 ? 0xFF : 0)).ToArray()),
    };

    [TestMethod]
    public void AdvancesBearingsAndPairsReproduceThePenLayout()
    {
        var metrics = new Gw2FontMetrics(24);
        var glyphs = SampleGlyphs();

        var font = Gw2FontBaker.Bake(glyphs, metrics, Gw2FontBaker.BaselineOf(glyphs, metrics.CellHeight), true);

        var byCodePoint = font.Glyphs.ToDictionary(glyph => glyph.CodePoint);
        var profiles = glyphs.ToDictionary(glyph => glyph.CodePoint, metrics.BuildProfile);
        foreach (var first in glyphs)
        foreach (var second in glyphs)
        {
            var adjustment = font.PairAdjustments
                .Where(pair => pair.FirstCodePoint == first.CodePoint && pair.SecondCodePoint == second.CodePoint)
                .Sum(pair => pair.AdvanceAdjustment);
            var bakedDistance = byCodePoint[first.CodePoint].Advance + adjustment
                + byCodePoint[second.CodePoint].BearingX - byCodePoint[first.CodePoint].BearingX;
            var gameDistance = Gw2FontBaker.PairDistance(metrics, first.CodePoint, profiles[first.CodePoint], second.CodePoint, profiles[second.CodePoint]);
            Assert.AreEqual(gameDistance, bakedDistance, $"{(char)first.CodePoint}{(char)second.CodePoint}");
        }
        Assert.AreEqual(metrics.MissingCharacterAdvance, byCodePoint[Gw2FontArchive.SpaceCodePoint].Advance);
        Assert.AreEqual(20, font.Baseline);
        Assert.AreEqual(16, byCodePoint['A'].BearingY);
    }

    [TestMethod]
    public void OpenTypeFontHasAValidChecksumAndMapsCharactersToBitmapGlyphs()
    {
        var metrics = new Gw2FontMetrics(24);
        var glyphs = SampleGlyphs();
        var font = Gw2FontBaker.Bake(glyphs, metrics, Gw2FontBaker.BaselineOf(glyphs, metrics.CellHeight), true);

        var bytes = Gw2OpenTypeFont.Write(font, new Gw2OpenTypeFontNames("GW2 Test", "GW2-Test"));

        var tables = ReadTableDirectory(bytes);
        CollectionAssert.IsSubsetOf(new[] { "CBDT", "CBLC", "GPOS", "OS/2", "cmap", "glyf", "head", "hhea", "hmtx", "loca", "maxp", "name", "post" }, tables.Keys.ToArray());
        Assert.AreEqual(0xB1B0AFBAu, Checksum(bytes));
        Assert.AreEqual((ushort)(24 * Gw2OpenTypeFont.UnitsPerPixel), BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(tables["head"].Offset + 18)));

        var glyphIdOfA = MapCharacter(bytes, tables["cmap"].Offset, 'A');
        Assert.AreEqual(font.Glyphs.ToList().FindIndex(glyph => glyph.CodePoint == 'A') + 1, glyphIdOfA);

        var cblc = tables["CBLC"].Offset;
        var cbdt = tables["CBDT"].Offset;
        var subtable = cblc + 56 + 8;
        Assert.AreEqual(17, BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(subtable + 2)));
        var imageDataOffset = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(subtable + 4));
        var glyphOffset = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(subtable + 8 + (glyphIdOfA - 1) * 4));
        var glyph = cbdt + imageDataOffset + glyphOffset;
        Assert.AreEqual(16, bytes[glyph]);
        Assert.AreEqual(10, bytes[glyph + 1]);
        Assert.AreEqual(16, (sbyte)bytes[glyph + 3]);
        CollectionAssert.AreEqual(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' }, bytes.AsSpan(glyph + 9, 4).ToArray());
    }

    private static Dictionary<string, (int Offset, int Length)> ReadTableDirectory(byte[] font)
    {
        var tables = new Dictionary<string, (int, int)>();
        var count = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
        for (var index = 0; index < count; index++)
        {
            var record = 12 + index * 16;
            tables[Encoding.ASCII.GetString(font, record, 4)] =
                ((int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(record + 8)), (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(record + 12)));
        }
        return tables;
    }

    private static int MapCharacter(byte[] font, int cmap, int codePoint)
    {
        var subtable = cmap + (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(cmap + 8));
        var segmentCount = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(subtable + 6)) / 2;
        var ends = subtable + 14;
        var starts = ends + segmentCount * 2 + 2;
        var deltas = starts + segmentCount * 2;
        for (var segment = 0; segment < segmentCount; segment++)
        {
            var end = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(ends + segment * 2));
            if (codePoint > end) continue;
            var start = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(starts + segment * 2));
            if (codePoint < start) return 0;
            return (ushort)(codePoint + BinaryPrimitives.ReadInt16BigEndian(font.AsSpan(deltas + segment * 2)));
        }
        return 0;
    }

    private static uint Checksum(byte[] font)
    {
        uint sum = 0;
        for (var index = 0; index < font.Length; index += 4)
            sum = unchecked(sum + BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(index, 4)));
        return sum;
    }
}
