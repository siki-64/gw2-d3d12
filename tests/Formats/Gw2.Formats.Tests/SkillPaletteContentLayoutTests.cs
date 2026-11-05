using System;
using System.IO;
using Gw2.Formats;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Formats.Tests;

[TestClass]
public sealed class SkillPaletteContentLayoutTests
{
    [TestMethod]
    public void ReadsThePaletteGroupAndEntryFieldsFromTheExecutable()
    {
        var path = Gw2Installation.ExecutablePath();
        if (path is null || !File.Exists(path)) Assert.Inconclusive("Gw2-64.exe is not installed.");

        var layout = SkillPaletteContentLayout.FromExecutable(path);

        Assert.AreEqual(new SkillPaletteContentLayout(0x41, 0x40, 0x40, 0x44, 0x30, 0x38, 0x18, 0x04, 0x08, 0x10, 0x70, 0x50, 0x38, 0x1000, 0x182, 0x4D, 0x68, 0x70, 0x60, 0x58, 0x60, 0xC0, 0xB8, 0x50, 0xE8, 0x120, 0x128), layout);
    }
}
