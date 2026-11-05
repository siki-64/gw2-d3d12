using Gw2.Dat.Content;
using Gw2.Dat.Archive;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.DatSurvey.Tests;

[TestClass]
public sealed class Gw2FourCcTests
{
    [TestMethod]
    public void ReadsPackTypeFromTheDecodedPrefix()
    {
        var prefix = "PF\u0000\u0000\u0000\u0000\u0000\u0000MODL"u8.ToArray();
        Assert.IsTrue(Gw2FourCc.IsPack(prefix));
        Assert.AreEqual(Gw2FourCc.Model, Gw2FourCc.PackType(prefix));
    }

    [TestMethod]
    public void ReadsEveryRecoveredPackClass()
    {
        Assert.AreEqual(Gw2FourCc.Material, PackType(Gw2FourCc.Material));
        Assert.AreEqual(Gw2FourCc.ContentStore, PackType(Gw2FourCc.ContentStore));
        Assert.AreEqual(Gw2FourCc.Composite, PackType(Gw2FourCc.Composite));
        Assert.AreEqual(Gw2FourCc.EmoteAnimations, PackType(Gw2FourCc.EmoteAnimations));
        Assert.AreEqual(Gw2FourCc.AnimSequences, PackType(Gw2FourCc.AnimSequences));
        Assert.AreEqual(Gw2FourCc.Map, PackType(Gw2FourCc.Map));
    }

    [TestMethod]
    public void RejectsBytesWithoutThePackMagic()
    {
        var raw = "DDS \u0000\u0000\u0000\u0000\u0000\u0000\u0000\u0000"u8.ToArray();
        Assert.IsFalse(Gw2FourCc.IsPack(raw));
        Assert.AreEqual(string.Empty, Gw2FourCc.PackType(raw));
    }

    [TestMethod]
    public void DecodesOnlyPrintableCodes()
    {
        Assert.AreEqual("comp", Gw2FourCc.Decode("comp"u8));
        Assert.AreEqual(string.Empty, Gw2FourCc.Decode(new byte[] { 0x10, 0x02, 0x00, 0x00 }));
        Assert.AreEqual(string.Empty, Gw2FourCc.Decode(new byte[] { 1, 2, 3 }));
    }

    private static string PackType(string type)
    {
        var prefix = new byte[12];
        prefix[0] = (byte)'P';
        prefix[1] = (byte)'F';
        for (var i = 0; i < 4; i++) prefix[8 + i] = (byte)type[i];
        return Gw2FourCc.PackType(prefix);
    }
}
