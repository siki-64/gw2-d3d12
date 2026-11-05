using Gw2.Formats;
using Gw2.Dat.Content;
using Gw2.Dat.Archive;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.DatSurvey.Tests;

[TestClass]
public sealed class Gw2BoneNameTokenTests
{
    [TestMethod]
    public void EncodesLettersAsFiveBitCaseInsensitiveCodes()
    {
        Assert.AreEqual(0x004134UL, Token64.Encode("tip"));
        Assert.AreEqual(0x02CC22UL, Token64.Encode("base"));
        Assert.AreEqual(0x558513UL, Token64.Encode("shake"));
        Assert.AreEqual(0x00B533UL, Token64.Encode("sima"));
        Assert.AreEqual(0x013533UL, Token64.Encode("simb"));
    }

    [TestMethod]
    public void EncodesTrailingTwoDigitsIntoTheTopNibble()
    {
        Assert.AreEqual(0x1000000000004134UL, Token64.Encode("tip01"));
        Assert.AreEqual(0x7000000000013533UL, Token64.Encode("simb07"));
        Assert.AreEqual(0x5000000000004134UL, Token64.Encode("tip05"));
    }

    [TestMethod]
    public void MatchesTheTextAfterTheFirstColon()
    {
        Assert.AreEqual(Token64.Encode("tip01"), Token64.EncodeAfterColon("bone:tip01"));
        Assert.AreEqual(Token64.Encode("root"), Token64.EncodeAfterColon("bone:root"));
    }

    [TestMethod]
    public void LimitsTheLetterCodeToTwelveCharacters()
    {
        var twelve = Token64.Encode("abcdefghijkl");
        var thirteen = Token64.Encode("abcdefghijklm");
        Assert.AreEqual(twelve, thirteen);
    }

    [TestMethod]
    public void DecodesTheFiveBitLetterCode()
    {
        Assert.AreEqual("tip", Token64.Decode(0x004134UL));
        Assert.AreEqual("zeropose", Token64.Decode(0x2CDF07C8BAUL));
        Assert.AreEqual("lookup", Token64.Decode(0x2155BDECUL));
        Assert.AreEqual("ugmidle", Token64.Decode(Token64.Encode("ugmidle")));
        Assert.AreEqual(string.Empty, Token64.Decode(0));
    }
}
