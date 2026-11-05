using System;
using System.IO;
using Gw2.Formats;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Formats.Tests;

[TestClass]
public sealed class MapLayoutContentLayoutTests
{
    [TestMethod]
    public void ReadsTheMapLayoutFieldsFromTheExecutable()
    {
        var path = Gw2Installation.ExecutablePath();
        if (path is null || !File.Exists(path)) Assert.Inconclusive("Gw2-64.exe is not installed.");

        var layout = MapLayoutContentLayout.FromExecutable(path);

        Assert.AreEqual(new MapLayoutContentLayout(0x30, 0x28, 0x30, 0x30, 0x28, 0x48, 0xB0, 0xC0, 0x38, 0x3C, 0x40, 0xC0,
            new MapLayoutFloorPagesOffsets(0xB8, 0x98, 0xA0, 0xA8, 0xB0, 0x88, 0x90, 0x70, 0x68), 0x188), layout);
    }
}
