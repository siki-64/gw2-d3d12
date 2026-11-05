using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Gw2.Client.Scene;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Scene.Tests;

[TestClass]
public sealed class ColorShiftTests
{
    private const uint SkinTone = 0xFF4A7AC0;

    [TestMethod]
    public void IdentityShiftKeepsTheColor()
    {
        Assert.AreEqual(SkinTone, Gw2ColorShift.Apply(Gw2ColorShift.Identity.ToMatrix(), SkinTone));
    }

    [TestMethod]
    public void ZeroSaturationGivesTheChannelAverage()
    {
        var shift = Gw2ColorShift.Identity with { Saturation = 0f };
        var grey = Gw2ColorShift.Apply(shift.ToMatrix(), SkinTone);
        var average = (uint)MathF.Round((0xC0 + 0x7A + 0x4A) / 3f);

        Assert.AreEqual(average, grey & 0xFF, 1u);
        Assert.AreEqual(grey & 0xFF, (grey >> 8) & 0xFF);
        Assert.AreEqual(grey & 0xFF, (grey >> 16) & 0xFF);
    }

    [TestMethod]
    public void PositiveHueTurnsRedLeatherTowardOliveLikeTheLiveClient()
    {
        var shift = new Gw2ColorShift(-0.141f, 1f, 0.785f, 0.203f, 1.016f);
        var shifted = Gw2ColorShift.Apply(shift.ToMatrix(), 0xFF1A2A7A);
        var red = shifted & 0xFF;
        var green = (shifted >> 8) & 0xFF;
        var blue = (shifted >> 16) & 0xFF;

        Assert.IsTrue(green > blue, $"green {green} blue {blue}");
        Assert.IsTrue(red > blue, $"red {red} blue {blue}");
    }

    [TestMethod]
    public void BrightnessOffsetsEveryChannel()
    {
        var matrix = (Gw2ColorShift.Identity with { Brightness = 0.1f }).ToMatrix();

        Assert.AreEqual(0.1f, matrix.M14, 1e-4f);
        Assert.AreEqual(0.1f, matrix.M24, 1e-4f);
        Assert.AreEqual(0.1f, matrix.M34, 1e-4f);
    }

    [TestMethod]
    public void HumanFemaleChestNormalMapDecodesNearFlatWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var normals = Gw2RgbaImage.FromTexture(reader.ReadTexture(128677)!.Texture!);
        var texels = Enumerable.Range(0, normals.Width * normals.Height).ToArray();
        var averageRed = texels.Average(texel => (double)normals.Pixels[texel * 4]);
        var averageGreen = texels.Average(texel => (double)normals.Pixels[texel * 4 + 1]);

        Assert.AreEqual(256, normals.Width);
        Assert.AreEqual(128, normals.Height);
        Assert.AreEqual(128.0, averageRed, 16.0);
        Assert.AreEqual(128.0, averageGreen, 16.0);
    }
}
