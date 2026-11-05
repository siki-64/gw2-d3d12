using System;
using System.IO;
using Gw2.Formats;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Formats.Tests;

[TestClass]
public sealed class EffectContentLayoutTests
{
    [TestMethod]
    public void ReadsTheEffectDefinitionFieldsFromTheExecutable()
    {
        var exe = Gw2Installation.ExecutablePath();
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) Assert.Inconclusive("Gw2-64.exe is not installed.");

        var layout = EffectContentLayout.FromExecutable(exe);

        Assert.AreEqual(0xB7u, layout.EffectContentType);
        Assert.AreEqual(0x28, layout.KindOffset);
        Assert.AreEqual(0x30, layout.PayloadOffset);
        Assert.AreEqual(new EffectModelLayout(0x18, 0x20, 0x80, 0x28, 0x44, 0x48, 0x40, 0x68), layout.Model);
        Assert.AreEqual(new EffectAttachedModelLayout(0x18, 0x20), layout.BoneModel);
        Assert.AreEqual(new EffectAttachedModelLayout(0x08, 0x10), layout.WeaponTrail);
        Assert.AreEqual(new EffectDecalLayout(0x00, 0x08, 0x10, 0x18, 0x20, 0x38, 0x40), layout.Decal);
    }
}
