using System;
using System.IO;
using Gw2.Formats;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Formats.Tests;

[TestClass]
public sealed class PointOfInterestContentLayoutTests
{
    [TestMethod]
    public void ReadsThePointOfInterestFieldsFromTheExecutable()
    {
        var path = Gw2Installation.ExecutablePath();
        if (path is null || !File.Exists(path)) Assert.Inconclusive("Gw2-64.exe is not installed.");

        Assert.AreEqual(new PointOfInterestContentLayout(0x70, 0x78, 0x30, 0x4C, 0x44), PointOfInterestContentLayout.FromExecutable(path));
    }
}
