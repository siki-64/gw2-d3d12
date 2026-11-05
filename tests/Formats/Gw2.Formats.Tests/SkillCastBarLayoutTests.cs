using System.IO;
using Gw2.Formats;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Formats.Tests;

[TestClass]
public sealed class SkillCastBarLayoutTests
{
    [TestMethod]
    public void ReadsTheAbilityTokensAndTheHiddenCastBarFlagFromTheExecutable()
    {
        var path = Gw2Installation.ExecutablePath();
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) Assert.Inconclusive("Gw2-64.exe is not installed.");
        var layout = SkillCastBarLayout.FromExecutable(path);
        Assert.AreEqual(0x00, layout.AbilitySequenceTokenOffset);
        Assert.AreEqual(0x18, layout.AbilityVariantTokenOffset);
        Assert.AreEqual(0x70, layout.AbilityFlagsOffset);
        Assert.AreEqual(0x10000000u, layout.HidesCastBarAbilityFlag);
        Assert.AreEqual(0x80, layout.AbilityLoopStepCountsOffset);
    }
}
