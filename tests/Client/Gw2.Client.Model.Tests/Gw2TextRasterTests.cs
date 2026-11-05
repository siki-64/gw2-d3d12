using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2TextRasterTests
{
    private const uint White = 0xFFFFFFFF;
    private const ulong ButtonLabelSizeKey = 0x000000001816C9EE;

    [TestMethod]
    public void ShortTextFitsOneBandWithAOneColumnMargin()
    {
        var face = new Gw2FontFace(16, [Solid('A', 6, 2, 10)]);

        var result = Gw2TextRaster.Rasterize(face, "A", Gw2TextLayoutFlags.None, White);

        Assert.AreEqual(1, result.SegmentCount);
        Assert.AreEqual(32, result.BandHeight);
        var image = result.Images.Single();
        Assert.AreEqual((32, 32), (image.Width, image.Height));
        Assert.AreEqual(0, AlphaAt(image, 0, 2));
        Assert.AreEqual(255, AlphaAt(image, 1, 2));
        Assert.AreEqual(255, AlphaAt(image, 6, 11));
        Assert.AreEqual(0, AlphaAt(image, 1, 1));
    }

    [TestMethod]
    public void LongTextIsCutIntoSegmentsStackedInThePage()
    {
        var face = new Gw2FontFace(16, [Solid('A', 30, 0, 16)]);

        var result = Gw2TextRaster.Rasterize(face, new string('A', 12), Gw2TextLayoutFlags.None, White);

        Assert.IsTrue(result.TextWidth > Gw2TextRaster.PageSize);
        Assert.AreEqual(256, result.BandWidth);
        Assert.AreEqual(2, result.SegmentCount);
        var image = result.Images.Single();
        Assert.AreEqual(256, image.Width);
        Assert.AreEqual(64, image.Height);
        Assert.AreEqual(255, AlphaAt(image, 1, 32));
    }

    [TestMethod]
    public void ShadowDownRightDarkensThePixelBelowAndRightOfInk()
    {
        var face = new Gw2FontFace(16, [Solid('A', 1, 0, 1)]);

        var image = Gw2TextRaster.Rasterize(face, "A", Gw2TextLayoutFlags.ShadowDownRight, White).Images.Single();

        Assert.AreEqual(255, AlphaAt(image, 1, 0));
        var shadow = PixelAt(image, 2, 1);
        Assert.AreEqual(240, shadow.Alpha);
        Assert.AreEqual(0, shadow.Red);
        Assert.AreEqual(0, AlphaAt(image, 0, 1));
    }

    [TestMethod]
    public void MnemonicMarkerIsSkippedAndUnderlinesTheNextCharacter()
    {
        var face = new Gw2FontFace(8, [Solid('_', 3, 7, 1), Empty('A', 4, 0, 4)]);

        var marked = Gw2TextRaster.Rasterize(face, "_A", Gw2TextLayoutFlags.Mnemonics, White).Images.Single();
        var doubled = Gw2TextRaster.Rasterize(face, "__", Gw2TextLayoutFlags.Mnemonics, White).Images.Single();

        Assert.AreEqual(255, AlphaAt(marked, 1, 7));
        Assert.AreEqual(255, AlphaAt(marked, 3, 7));
        Assert.AreEqual(255, AlphaAt(doubled, 1, 7));
    }

    [TestMethod]
    public void PaletteKeepsTheColourAndScalesAlphaWithCoverage()
    {
        var palette = Gw2TextRaster.BuildPalette(0xFFFFEAB8);

        Assert.AreEqual(0x00FFEAB8u, palette[0]);
        Assert.AreEqual(0x80FFEAB8u, palette[128]);
        Assert.AreEqual(0xFFFFEAB8u, palette[255]);
        Assert.AreEqual(0x00FFEAB8u, palette[256]);
        Assert.AreEqual(240u, palette[256 * 16] >> 24);
    }

    [TestMethod]
    public void ButtonLabelFontRastersPlayWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var manifest = Gw2FontManifest.Parse(reader.ReadBytes(Gw2FontManifest.ManifestFileId));
        var entry = Gw2FontFace.FindEntry(manifest, 0, 0, ButtonLabelSizeKey);
        Assert.IsNotNull(entry);
        var face = Gw2FontFace.Load(reader, entry);

        var result = Gw2TextRaster.Rasterize(face, "Play", Gw2TextLayoutFlags.None, White);

        Assert.AreEqual(16, face.CellHeight);
        Assert.AreEqual(26, result.TextWidth);
        var image = result.Images.Single();
        Assert.AreEqual((32, 32), (image.Width, image.Height));
        var inkColumns = Enumerable.Range(0, image.Width).Where(x => Enumerable.Range(0, image.Height).Any(y => AlphaAt(image, x, y) > 0)).ToArray();
        Assert.AreEqual(25, inkColumns.Last() - inkColumns.First() + 1);
    }

    [TestMethod]
    public void GlowWinsOverTheShadowFromAbove()
    {
        var kept = Gw2TextRaster.KeepOneEffect(Gw2TextLayoutFlags.ShadowDownRight | Gw2TextLayoutFlags.Glow | Gw2TextLayoutFlags.ShadowUpLeft);

        Assert.AreEqual(Gw2TextLayoutFlags.Glow | Gw2TextLayoutFlags.ShadowUpLeft, kept);
    }

    [TestMethod]
    public void OnlyTheFirstEffectByPriorityIsKept()
    {
        var kept = Gw2TextRaster.KeepOneEffect(Gw2TextLayoutFlags.Glow | Gw2TextLayoutFlags.Halo | Gw2TextLayoutFlags.Underline);

        Assert.AreEqual(Gw2TextLayoutFlags.Halo | Gw2TextLayoutFlags.Underline, kept);
    }

    private static int AlphaAt(Gw2TextImage image, int x, int y) => image.Rgba[(y * image.Width + x) * 4 + 3];

    private static (int Red, int Alpha) PixelAt(Gw2TextImage image, int x, int y) =>
        (image.Rgba[(y * image.Width + x) * 4], image.Rgba[(y * image.Width + x) * 4 + 3]);

    private static Gw2FontGlyph Solid(int codePoint, int width, int top, int height) =>
        new(codePoint, top, width, height, Enumerable.Repeat((byte)0xFF, width * height).ToArray());

    private static Gw2FontGlyph Empty(int codePoint, int width, int top, int height) =>
        new(codePoint, top, width, height, new byte[width * height]);
}
