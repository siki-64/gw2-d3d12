using System;
using System.IO;
using Gw2.Formats;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Formats.Tests;

[TestClass]
public sealed class MapContentLayoutTests
{
    [TestMethod]
    public void ReadsTheMapAndDayNightCycleFieldsFromTheExecutable()
    {
        var path = Gw2Installation.ExecutablePath();
        if (path is null || !File.Exists(path)) Assert.Inconclusive("Gw2-64.exe is not installed.");

        var layout = MapContentLayout.FromExecutable(path);

        Assert.AreEqual(new MapContentLayout(0x68, 0x98, 0x04, 0x220, 0xA8, new Guid("69374416-bfb3-4477-a0d2-72319fe69d4b"), 0x2C, 0x30, 0x40, 0x44, 0x78), layout);
    }

    [TestMethod]
    public void ReadsTheSpeciesNameOffsetFromTheExecutable()
    {
        var path = Gw2Installation.ExecutablePath();
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) Assert.Inconclusive("Gw2-64.exe is not installed.");
        Assert.AreEqual(new SpeciesContentLayout(0x194, 0xF0), SpeciesContentLayout.FromExecutable(path));
    }
}
