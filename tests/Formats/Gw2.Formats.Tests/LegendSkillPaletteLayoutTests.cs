using System;
using System.IO;
using System.Linq;
using Gw2.Formats;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Formats.Tests;

[TestClass]
public sealed class LegendSkillPaletteLayoutTests
{
    [TestMethod]
    public void ReadsTheLegendPaletteConfigKeysFromTheExecutable()
    {
        var path = Gw2Installation.ExecutablePath();
        if (path is null || !File.Exists(path)) Assert.Inconclusive("Gw2-64.exe is not installed.");

        var layout = LegendSkillPaletteLayout.FromExecutable(path);

        Assert.AreEqual(15u, layout.FirstLegendProfessionState);
        Assert.AreEqual(0x96u, layout.ConfigContentType);
        Assert.AreEqual(0x30, layout.ConfigValueOffset);
        CollectionAssert.AreEqual(new[]
        {
            new Guid("226f1dfd-2abf-44ac-9c98-7f4a72cda0d7"),
            new Guid("a5d62d94-755c-4a06-8629-33937b467c28"),
            new Guid("a33bf00f-8759-491e-b4c4-2792c10453b9"),
            new Guid("d58542fe-ea95-4ce6-8dbf-0bc718470496"),
            new Guid("f0c66363-4a98-4975-919c-6d8a7f819561"),
            new Guid("df562aac-536f-45e2-b418-42a10aec3a71"),
            new Guid("fa681aeb-caf2-4a61-959f-4fbe1c3913c8"),
            new Guid("0de936ed-9589-496c-8177-ffcea2e40403"),
        }, layout.PaletteConfigKeys.ToArray());
    }
}
