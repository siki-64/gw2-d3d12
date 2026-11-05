using Gw2.Formats;
using System;
using System.IO;
using Gw2.Dat.Content;
using Gw2.Dat.Archive;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Scene.Tests;

[TestClass]
public sealed class AtexBlitTests
{
    [TestMethod]
    [DataRow("noise-13x7", 13, 7)]
    public void OpaqueDxt5EncodeMatchesTheExeBlitterByteForByte(string name, int width, int height)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "fixtures", "dxt5-encode");
        var bgraBytes = File.ReadAllBytes(Path.Combine(directory, name + ".bgra"));
        var bgra = new uint[bgraBytes.Length / 4];
        Buffer.BlockCopy(bgraBytes, 0, bgra, 0, bgraBytes.Length);

        CollectionAssert.AreEqual(File.ReadAllBytes(Path.Combine(directory, name + ".dxt5")), Gw2Dxt5Encoder.EncodeOpaque(bgra, width, height));
    }

    [TestMethod]
    public void HumanFemaleChestNormalMapBlitsToBc5xWithChannelBlocksSwappedWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var texture = reader.ReadTexture(128677)!.Texture!;
        var threeDcx = texture.DecodeGpuBlocks();
        var bc5x = (byte[])threeDcx.Clone();
        Gw2AtexBlit.ThreeDcxToBc5x(bc5x);

        Assert.AreEqual(Gw2AtexFormat.ThreeDcxId, texture.AtexFormat!.Id);
        Assert.AreEqual(256 / 4 * (128 / 4) * 16, bc5x.Length);
        for (var block = 0; block < bc5x.Length; block += 16)
        {
            CollectionAssert.AreEqual(threeDcx[(block + 8)..(block + 16)], bc5x[block..(block + 8)]);
            CollectionAssert.AreEqual(threeDcx[block..(block + 8)], bc5x[(block + 8)..(block + 16)]);
        }
    }

    [TestMethod]
    public void SharedInventorySlotOverlayBlitsFromDxtaToWhiteDxt5KeepingTheAlphaBlocks()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var texture = reader.ReadTexture(1314215)!.Texture!;
        var dxta = texture.DecodeGpuBlocks();
        var dxt5 = Gw2AtexBlit.DxtaToDxt5(dxta);

        Assert.AreEqual(Gw2AtexFormat.DxtaId, texture.AtexFormat!.Id);
        Assert.AreEqual(64 / 4 * (64 / 4) * 8, dxta.Length);
        for (var block = 0; block < dxta.Length / 8; block++)
        {
            CollectionAssert.AreEqual(dxta[(block * 8)..(block * 8 + 8)], dxt5[(block * 16)..(block * 16 + 8)]);
            CollectionAssert.AreEqual(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0, 0, 0, 0 }, dxt5[(block * 16 + 8)..(block * 16 + 16)]);
        }
    }
}
