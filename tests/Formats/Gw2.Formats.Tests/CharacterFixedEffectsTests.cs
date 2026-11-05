using System;
using System.IO;
using Gw2.Formats;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Formats.Tests;

[TestClass]
public sealed class CharacterFixedEffectsTests
{
    [TestMethod]
    public void ReadsTheFixedEffectEventGuidFromTheExecutable()
    {
        var path = Gw2Installation.ExecutablePath();
        if (path is null || !File.Exists(path)) Assert.Inconclusive("Gw2-64.exe is not installed.");

        Assert.AreEqual(new Guid("c57ad5fe-38f9-40b4-9294-661eefc20095"), CharacterFixedEffects.FromExecutable(path).FixedEffectEventEffect);
    }
}
