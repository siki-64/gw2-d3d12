using System.Linq;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2TextLayoutTests
{
    private const uint White = 0xFFFFFFFF;

    [TestMethod]
    public void WrapsAtTheLastSpaceThatFits()
    {
        var face = MonospaceFace();
        var frame = new Gw2TextFrame(Width: 40, Height: 100, FontHeight: face.CellHeight, LineHeight: face.CellHeight, SingleLine: false);

        var blocks = Gw2TextLayout.Blocks(face, [new Gw2TextSnippet("aaa bbb ccc", White, 0, true)], Gw2TextLayoutFlags.None, frame);

        CollectionAssert.AreEqual(new[] { "aaa", "bbb", "ccc" }, blocks.Select(block => block.Text).ToArray());
    }

    [TestMethod]
    public void LineFeedEndsTheLineAndIsNotDrawn()
    {
        var face = MonospaceFace();
        var frame = new Gw2TextFrame(Width: 400, Height: 100, FontHeight: face.CellHeight, LineHeight: face.CellHeight, SingleLine: false);

        var blocks = Gw2TextLayout.Blocks(face, [new Gw2TextSnippet("aa\nbb", White, 0, true)], Gw2TextLayoutFlags.None, frame);

        CollectionAssert.AreEqual(new[] { "aa", "bb" }, blocks.Select(block => block.Text).ToArray());
    }

    [TestMethod]
    public void SingleLineCutsAtTheWidthWithoutWrapping()
    {
        var face = MonospaceFace();
        var frame = new Gw2TextFrame(Width: 40, Height: 100, FontHeight: face.CellHeight, LineHeight: face.CellHeight, SingleLine: true);

        var blocks = Gw2TextLayout.Blocks(face, [new Gw2TextSnippet("aaa bbb ccc", White, 0, true)], Gw2TextLayoutFlags.None, frame);

        Assert.AreEqual(1, blocks.Count);
        Assert.IsTrue(blocks[0].Text.StartsWith("aaa"));
    }

    [TestMethod]
    public void LinesStopAtTheFrameHeight()
    {
        var face = MonospaceFace();
        var frame = new Gw2TextFrame(Width: 40, Height: face.CellHeight * 2, FontHeight: face.CellHeight, LineHeight: face.CellHeight, SingleLine: false);

        var blocks = Gw2TextLayout.Blocks(face, [new Gw2TextSnippet("aaa bbb ccc", White, 0, true)], Gw2TextLayoutFlags.None, frame);

        Assert.AreEqual(2, blocks.Count);
    }

    [TestMethod]
    public void NoBreakBeforeClosingPunctuation()
    {
        Assert.IsFalse(Gw2TextLayout.CanBreakBefore("a!", 1, 0));
        Assert.IsTrue(Gw2TextLayout.CanBreakBefore("a b", 1, 0));
        Assert.IsTrue(Gw2TextLayout.CanBreakBefore("a-b", 2, 0));
        Assert.IsFalse(Gw2TextLayout.CanBreakBefore("(b", 1, 0));
    }

    [TestMethod]
    public void MarkupColorsAndStripsTags()
    {
        var snippets = Gw2TextMarkup.Split("Go <c=@Quest>north</c> now<br>next", White, markupDisabled: false);

        CollectionAssert.AreEqual(new[] { "Go ", "north", " now", "next" }, snippets.Select(snippet => snippet.Text).ToArray());
        Assert.AreEqual(0xFF00FF00u, snippets[1].Argb);
        Assert.AreEqual(White, snippets[2].Argb);
        Assert.AreEqual(1, snippets[3].LinesBefore);
    }

    [TestMethod]
    public void HexColorsGetFullAlpha()
    {
        Assert.AreEqual(0xFF32F00Du, Gw2TextMarkup.ParseColor("#32F00d"));
    }

    [TestMethod]
    public void DisabledMarkupKeepsTheTextAsIs()
    {
        var snippets = Gw2TextMarkup.Split("a <c=#FF0000>b", White, markupDisabled: true);

        Assert.AreEqual("a <c=#FF0000>b", snippets.Single().Text);
    }

    private static Gw2FontFace MonospaceFace()
    {
        var glyphs = "abc!-()".Select(character => new Gw2FontGlyph(character, 0, 8, 8, Enumerable.Repeat((byte)0xFF, 64).ToArray()));
        return new Gw2FontFace(16, glyphs);
    }
}
