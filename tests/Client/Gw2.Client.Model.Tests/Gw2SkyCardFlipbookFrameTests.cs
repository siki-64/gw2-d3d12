using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2SkyCardFlipbookFrameTests
{
    private const uint SkyCardMapFileId = 1839188;

    [TestMethod]
    public void FrameWalksTheAtlasFromStartAndBlendsIntoTheNextCell()
    {
        var flipbook = new Gw2MapEnvSkyCardFlipbook(Rows: 2, Columns: 4, Start: 6, Count: 3, Fps: 2f);

        var frame = Gw2SkyCardFlipbookFrame.At(flipbook, 0.75f);

        Assert.AreEqual(new Vector4(0.75f, 1f, 0.5f, 1f), frame.Cell);
        Assert.AreEqual(new Vector4(0f, 0.25f, 0f, 0.5f), frame.NextCell);
        Assert.AreEqual(0.5f, frame.Blend, 1e-6f);
    }

    [TestMethod]
    public void FlipbookCardsInTheMapCarryAMaterialFlipbook()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var cards = reader.ReadMap(SkyCardMapFileId)!.Environment!.SkyCards;
        var flipbookCards = cards.Where(card => (card.Flags & Gw2SkyCardFlipbookFrame.FlipbookCardFlag) != 0).ToArray();
        Assert.AreEqual(4, flipbookCards.Length);
        foreach (var card in flipbookCards)
            Assert.AreEqual(new Gw2MapEnvSkyCardFlipbook(2, 2, 1, 4, 2f), card.Material!.Flipbook);
    }
}
