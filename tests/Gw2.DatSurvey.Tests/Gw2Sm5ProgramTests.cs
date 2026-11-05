using Gw2.Formats;
using System.Text.RegularExpressions;
using Gw2.Dat.Content;
using Gw2.Dat.Archive;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.DatSurvey.Tests;

[TestClass]
public sealed partial class Gw2Sm5ProgramTests
{
    private const uint WaterMaterialFileId = 187551;
    private const uint CanonicalPropMaterialFileId = 0x1130E;
    private const uint HumanFemaleBodyMaterialFileId = 0x8585;

    [TestMethod]
    public void OpCodeNamesFollowTheD3D11TokenTable()
    {
        Assert.AreEqual("mov", Gw2Sm5OpCodes.NameOf(54));
        Assert.AreEqual("dcl_globalFlags", Gw2Sm5OpCodes.NameOf(106));
        Assert.AreEqual("rcp", Gw2Sm5OpCodes.NameOf(129));
        Assert.AreEqual("swapc", Gw2Sm5OpCodes.NameOf(142));
        Assert.AreEqual("sync", Gw2Sm5OpCodes.NameOf(Gw2Sm5OpCodes.Sync));
        Assert.AreEqual("debug_break", Gw2Sm5OpCodes.NameOf(208));
    }

    [TestMethod]
    [DataRow(WaterMaterialFileId)]
    [DataRow(CanonicalPropMaterialFileId)]
    [DataRow(HumanFemaleBodyMaterialFileId)]
    public void ListingMatchesD3DDisassembleInstructionForInstruction(uint materialFileId)
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("D3DDisassemble needs Windows.");
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        using var archive = FileArchive.Open(dat, verifyChecksums: false);
        var source = new Gw2DatContentSource(archive);
        var reference = ShaderDisassembler.Disassemble(source, materialFileId);
        var material = Gw2PackDecoders.Decode<Gw2AmatMaterial>(source.DecodeAsset(materialFileId))!;

        Assert.IsTrue(reference.Count > 0);
        foreach (var listing in reference)
        {
            var binary = material.Shaders[listing.ShaderIndex].Binaries.First(candidate => candidate.Backend == Gw2AmatShaderBackend.Dx11 && candidate.Data.Length > 0);
            var container = Gw2BgfxShaderBinary.TryParse(binary.Data)!.Container!;
            Assert.IsTrue(container.TryGetChunk("SHEX", out var shex));
            var program = Gw2Sm5Program.TryParse(shex.Data);
            Assert.IsNotNull(program, $"shader {listing.ShaderIndex}");

            var expected = InstructionLines(listing.Text);
            var actual = InstructionLines(Gw2Sm5Listing.Format(program));
            Assert.AreEqual(expected.Count, actual.Count, $"shader {listing.ShaderIndex} instruction count");
            for (var line = 0; line < expected.Count; line++)
                Assert.AreEqual(expected[line], actual[line], $"shader {listing.ShaderIndex} line {line}");
        }
    }

    private static List<string> InstructionLines(string text) =>
        text.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith("//", StringComparison.Ordinal) && !line.StartsWith("dcl_", StringComparison.Ordinal) && !ShaderModelLine().IsMatch(line))
            .Select(line => ImmediateValues().Replace(line, match => "l(" + new string(',', match.Value.Count(character => character == ',')) + ")"))
            .ToList();

    [GeneratedRegex(@"^(vs|ps|gs|hs|ds|cs)_\d_\d$")]
    private static partial Regex ShaderModelLine();

    [GeneratedRegex(@"l\([^)]*\)")]
    private static partial Regex ImmediateValues();
}
