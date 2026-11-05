using Gw2.Formats;
using System;
using System.IO;
using Gw2.Dat.Archive;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Scene.Tests;

[TestClass]
public sealed class UiContentTextureTests
{
    [TestMethod]
    [DataRow(156482u, 1024, 128)]
    [DataRow(156483u, 1024, 128)]
    [DataRow(156488u, 1024, 128)]
    [DataRow(156083u, 256, 256)]
    public void SkillBarAndMinimapTexturesDecode(uint fileId, int width, int height)
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var image = Gw2RgbaImage.FromTexture(reader.ReadTexture(fileId)!.Texture!);

        Assert.AreEqual((width, height), (image.Width, image.Height));
    }
}
