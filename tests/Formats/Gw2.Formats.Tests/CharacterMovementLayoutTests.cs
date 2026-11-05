using System;
using System.IO;
using Gw2.Formats;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Formats.Tests;

[TestClass]
public sealed class CharacterMovementLayoutTests
{
    [TestMethod]
    public void ReadsTheDefaultRunSpeedFromTheExecutable()
    {
        var path = Gw2Installation.ExecutablePath();
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) Assert.Inconclusive("Gw2-64.exe is not installed.");
        Assert.AreEqual(294f, CharacterMovementLayout.FromExecutable(path).DefaultRunSpeedInGameUnits);
    }
}
