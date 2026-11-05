using System;
using System.IO;
using Gw2.Formats;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Formats.Tests;

[TestClass]
public sealed class ColorPaletteContentLayoutTests
{
    [TestMethod]
    public void ReadsThePaletteAndDyeVariantFieldsFromTheExecutable()
    {
        var path = Gw2Installation.ExecutablePath();
        if (path is null || !File.Exists(path)) Assert.Inconclusive("Gw2-64.exe is not installed.");

        var layout = ColorPaletteContentLayout.FromExecutable(path);

        Assert.AreEqual(new ColorPaletteContentLayout(0x93, 0x30, 0x38, 0x18, 0x30, 0x38, 0x18, 0x14, 128f, 128f, 0f, 128f, 128f), layout);
    }
}
