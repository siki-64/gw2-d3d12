using System;
using System.IO;
using Gw2.Formats;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Formats.Tests;

[TestClass]
public sealed class SkinContentLayoutTests
{
    [TestMethod]
    public void ReadsTheSkinFieldsFromTheExecutable()
    {
        var exe = Gw2Installation.ExecutablePath();
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) Assert.Inconclusive("Gw2-64.exe is not installed.");

        Assert.AreEqual(new SkinContentLayout(0x42, 0x30, 0x80, 0x88, 0xB8, 0x48), SkinContentLayout.FromExecutable(exe));
    }
}
