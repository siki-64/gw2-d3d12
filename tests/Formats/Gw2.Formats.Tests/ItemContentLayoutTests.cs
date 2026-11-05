using System;
using System.IO;
using Gw2.Formats;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Formats.Tests;

[TestClass]
public sealed class ItemContentLayoutTests
{
    [TestMethod]
    public void ReadsTheItemTypeAndWeaponTypeFieldsFromTheExecutable()
    {
        var path = Gw2Installation.ExecutablePath();
        if (path is null || !File.Exists(path)) Assert.Inconclusive("Gw2-64.exe is not installed.");

        Assert.AreEqual(new ItemContentLayout(0x23, 0x2C, 24, 0x30, 0x0C, 0x80, 0), ItemContentLayout.FromExecutable(path));
    }
}
