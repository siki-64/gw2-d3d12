using System;
using System.IO;
using System.Linq;
using Gw2.Dat.Content;
using Gw2.Dat.Archive;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2BitmapFontTests
{
    private static readonly Gw2FontCodePointRange Ascii = Gw2FontCodePointRanges.All[0];

    [TestMethod]
    public void CoverageStreamMixesSingleValuesAndSpans()
    {
        var stream = new byte[] { 2, 2, 1, 0x01, 0x00, 0x01, 0x80, 0xFF, 0x01, 0x40 };

        var glyph = Gw2FontGlyphStream.Decode(stream, Ascii).Single();

        Assert.AreEqual(0x21, glyph.CodePoint);
        Assert.AreEqual(2, glyph.TopOffset);
        Assert.AreEqual(3, glyph.Width);
        Assert.AreEqual(2, glyph.Height);
        CollectionAssert.AreEqual(new byte[] { 0x00, 0x00, 0x80, 0xFF, 0xFF, 0x40 }, glyph.Coverage);
    }

    [TestMethod]
    public void AlternatingSpansStartFromTheInvertedTypeAndChainGlyphs()
    {
        var stream = new byte[] { 0, 1, 0, 0x00, 0x00, 0x00, 1, 1, 0, 0xFF, 0x01 };

        var glyphs = Gw2FontGlyphStream.Decode(stream, Ascii);

        CollectionAssert.AreEqual(new byte[] { 0xFF, 0x00 }, glyphs[0].Coverage);
        CollectionAssert.AreEqual(new byte[] { 0x00, 0x00 }, glyphs[1].Coverage);
        Assert.AreEqual(0x22, glyphs[1].CodePoint);
    }

    [TestMethod]
    public void LongSpanAddsContinuationBytes()
    {
        var stream = new byte[] { 0, 255, 1, 0x00, 0xFF, 0x00, 0xFF, 0x00 };

        var glyph = Gw2FontGlyphStream.Decode(stream, Ascii).Single();

        Assert.AreEqual(512, glyph.Coverage.Length);
        Assert.IsTrue(glyph.Coverage.Take(256).All(value => value == 0xFF));
        Assert.IsTrue(glyph.Coverage.Skip(256).All(value => value == 0x00));
    }

    [TestMethod]
    public void UnknownRleTypeIsRejected()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => Gw2FontGlyphStream.Decode(new byte[] { 0, 0, 0, 0x40, 0 }, Ascii));
    }

    [TestMethod]
    public void FileReferenceCombinesBothWords()
    {
        var reference = new byte[] { 0x37, 0xBD, 0x06, 0x01, 0x00, 0x00 };

        Assert.AreEqual(439864u, Gw2FontManifest.ReadFileReference(reference));
    }

    [TestMethod]
    public void ManifestResolvesEntriesAndRangeReferences()
    {
        const int chunkOffset = 12;
        const int payloadOffset = chunkOffset + 16;
        const int entryOffset = payloadOffset + 8;
        const int entrySize = 0x10 + 13 * 4;
        const int referenceOffset = entryOffset + entrySize;
        var pack = new byte[referenceOffset + 6];
        "PF"u8.CopyTo(pack);
        pack[6] = 12;
        "AFNT"u8.CopyTo(pack.AsSpan(8));
        "AFNT"u8.CopyTo(pack.AsSpan(chunkOffset));
        BitConverter.GetBytes(pack.Length - chunkOffset - 8).CopyTo(pack, chunkOffset + 4);
        BitConverter.GetBytes((ushort)16).CopyTo(pack, chunkOffset + 10);
        BitConverter.GetBytes(1).CopyTo(pack, payloadOffset);
        BitConverter.GetBytes(entryOffset - (payloadOffset + 4)).CopyTo(pack, payloadOffset + 4);
        pack[entryOffset] = 6;
        pack[entryOffset + 1] = 2;
        BitConverter.GetBytes(0x1122334455667788UL).CopyTo(pack, entryOffset + 2);
        pack[entryOffset + 14] = 20;
        pack[entryOffset + 15] = 18;
        var rangeFourField = entryOffset + 0x10 + 4 * 4;
        BitConverter.GetBytes(referenceOffset - rangeFourField).CopyTo(pack, rangeFourField);
        new byte[] { 0x37, 0xBD, 0x06, 0x01 }.CopyTo(pack, referenceOffset);

        var entry = Gw2FontManifest.Parse(pack).Single();

        Assert.AreEqual(6, entry.FontIndex);
        Assert.AreEqual(2, entry.Variant);
        Assert.AreEqual(0x1122334455667788UL, entry.SizeKey);
        Assert.AreEqual(18, entry.CellHeight);
        Assert.AreEqual(439864u, entry.RangeFileIds[4]);
        Assert.AreEqual(12, entry.RangeFileIds.Count(id => id is null));
    }

    [TestMethod]
    public void SpacingConstantsFollowTheCellHeight()
    {
        var metrics = new Gw2FontMetrics(18);

        Assert.AreEqual(1, metrics.LetterSpacing);
        Assert.AreEqual(5, metrics.MissingCharacterAdvance);
    }

    [TestMethod]
    public void LetterProfileRecordsInkPerBand()
    {
        var metrics = new Gw2FontMetrics(4);
        var coverage = new byte[]
        {
            0xFF, 0x00, 0x00,
            0x00, 0xFF, 0x00,
            0x00, 0x00, 0x10,
            0xFF, 0x00, 0xFF,
        };

        var profile = metrics.BuildProfile(new Gw2FontGlyph('A', 0, 3, 4, coverage));

        Assert.IsTrue(profile.IsLetter);
        CollectionAssert.AreEqual(new uint[] { 0, 1, uint.MaxValue, 0 }, profile.BandInkLeft);
        CollectionAssert.AreEqual(new uint[] { 1, 1, 0, 3 }, profile.BandInkWidth);
    }

    [TestMethod]
    public void NonLettersAreSolidBlocksWithoutSpacing()
    {
        var metrics = new Gw2FontMetrics(18);
        var digit = new Gw2FontGlyph('1', 0, 5, 18, new byte[5 * 18]);
        var pen = new Gw2FontPen(metrics);

        var first = pen.Place('1', metrics.BuildProfile(digit));
        var second = pen.Place('1', metrics.BuildProfile(digit));

        Assert.AreEqual(0u, first);
        Assert.AreEqual(5u, second);
    }

    [TestMethod]
    public void LettersTuckIntoTheEmptyBandsOfThePreviousLetter()
    {
        var metrics = new Gw2FontMetrics(4);
        var slopeDown = new Gw2FontGlyph('A', 0, 4, 4, new byte[]
        {
            0xFF, 0, 0, 0,
            0, 0xFF, 0, 0,
            0, 0, 0xFF, 0,
            0, 0, 0, 0xFF,
        });
        var slopeUp = new Gw2FontGlyph('B', 0, 4, 4, new byte[]
        {
            0, 0, 0, 0xFF,
            0, 0, 0xFF, 0,
            0, 0xFF, 0, 0,
            0xFF, 0, 0, 0,
        });
        var pen = new Gw2FontPen(metrics);

        pen.Place('A', metrics.BuildProfile(slopeDown));
        var x = pen.Place('B', metrics.BuildProfile(slopeUp));

        Assert.AreEqual(5u, x);
    }
}
