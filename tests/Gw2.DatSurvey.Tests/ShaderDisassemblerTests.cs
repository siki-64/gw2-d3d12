using Gw2.Formats;
using Gw2.DatSurvey;
using Gw2.Dat.Content;
using Gw2.Dat.Archive;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.DatSurvey.Tests;

[TestClass]
public sealed class ShaderDisassemblerTests
{
    [TestMethod]
    public void DisassemblesCanonicalPropShaderWhenConfigured()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("D3DDisassemble needs Windows.");

        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var archive = FileArchive.Open(dat, verifyChecksums: false);
        var listings = ShaderDisassembler.Disassemble(new Gw2DatContentSource(archive), 0x1130E);

        Assert.IsTrue(listings.Count > 0, "no shaders disassembled");
        var pixel = listings.FirstOrDefault(listing => listing.Stage == "ps");
        Assert.IsNotNull(pixel, "no pixel shader disassembled");
        StringAssert.Contains(pixel!.Text, "ps_5_0");
        StringAssert.Contains(pixel.Text, "sample");
    }
}
