using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2FontFaceTests
{
    private const ulong DefaultStyleSizeKey = 0x00000301DC9204A8;

    [TestMethod]
    public void LayoutPlacesEachGlyphWithThePenAndSkipsMissingCharacters()
    {
        var face = new Gw2FontFace(24, [Solid('A', 10), Solid('B', 8)]);

        var line = face.Layout("A B");

        Assert.AreEqual(2, line.Glyphs.Count);
        Assert.AreEqual(0, line.Glyphs[0].X);
        Assert.IsTrue(line.Glyphs[1].X >= 10 + face.Metrics.MissingCharacterAdvance);
        Assert.AreEqual(line.Glyphs[1].X + 8 + face.Metrics.LetterSpacing, line.Width);
    }

    [TestMethod]
    public void DefaultUiStyleFontLaysOutTheCharacterSelectLabelsWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var manifest = Gw2FontManifest.Parse(reader.ReadBytes(Gw2FontManifest.ManifestFileId));
        var entry = Gw2FontFace.FindEntry(manifest, 0, 0, DefaultStyleSizeKey);
        Assert.IsNotNull(entry);
        var face = Gw2FontFace.Load(reader, entry);

        Assert.AreEqual(24, face.CellHeight);
        foreach (var label in new[] { "Play", "Log Out", "Region Selection", "Create", "Delete" })
        {
            var line = face.Layout(label);
            Assert.AreEqual(label.Count(character => character != ' '), line.Glyphs.Count, label);
            Assert.IsTrue(line.Width > 0 && line.Glyphs.Zip(line.Glyphs.Skip(1)).All(pair => pair.First.X < pair.Second.X), label);
        }
    }

    private static Gw2FontGlyph Solid(int codePoint, int width) =>
        new(codePoint, 4, width, 16, Enumerable.Repeat((byte)0xFF, width * 16).ToArray());
}
