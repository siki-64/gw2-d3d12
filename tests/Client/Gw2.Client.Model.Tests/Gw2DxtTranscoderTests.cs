using System;
using Gw2.Client.Model;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2DxtTranscoderTests
{
    [TestMethod]
    public void FourColorBlockKeepsColorBytesAndBecomesOpaque()
    {
        var dxt1 = new byte[] { 0x00, 0xF8, 0x1F, 0x00, 0xE4, 0x1B, 0x39, 0x6C };

        var dxt5 = Gw2DxtTranscoder.Dxt1ToDxt5(dxt1);

        Assert.AreEqual(16, dxt5.Length);
        Assert.AreEqual(255, dxt5[0]);
        CollectionAssert.AreEqual(new byte[6], dxt5.AsSpan(2, 6).ToArray());
        CollectionAssert.AreEqual(dxt1, dxt5.AsSpan(8, 8).ToArray());
    }

    [TestMethod]
    public void PunchThroughBlockSwapsEndpointsAndMarksTransparentPixels()
    {
        var dxt1 = new byte[] { 0x1F, 0x00, 0x00, 0xF8, 0b11_10_01_00, 0, 0, 0 };

        var dxt5 = Gw2DxtTranscoder.Dxt1ToDxt5(dxt1);

        Assert.AreEqual(0x00, dxt5[8]);
        Assert.AreEqual(0xF8, dxt5[9]);
        Assert.AreEqual(0x1F, dxt5[10]);
        Assert.AreEqual(0x00, dxt5[11]);
        Assert.AreEqual(0b01_11_00_01, dxt5[12]);
        var alphaIndices = (ulong)dxt5[2] | ((ulong)dxt5[3] << 8) | ((ulong)dxt5[4] << 16);
        Assert.AreEqual(1UL << 9, alphaIndices);
        Assert.AreEqual(255, dxt5[0]);
        Assert.AreEqual(0, dxt5[1]);
    }

    [TestMethod]
    public void RejectsPartialBlocks()
    {
        Assert.ThrowsExactly<ArgumentException>(() => Gw2DxtTranscoder.Dxt1ToDxt5(new byte[7]));
    }
}
