using Gw2.Dat.Content;
using Gw2.Formats;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public sealed class CharacterConfigsTests
{
    [TestMethod]
    public void ReadsTheEnergyMaximumAndRegenerationFromTheConfigurationContent()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        if (Gw2Installation.ExecutablePath() is null) Assert.Inconclusive("Gw2-64.exe is not installed.");
        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var configs = new Gw2CharacterConfigs(reader.ContentIndex, CharacterConfigTable.Current);
        Assert.AreEqual(100f, configs.Number(CharacterConfigTable.EnergyMaximumConfig));
        Assert.AreEqual(10f, configs.Number(CharacterConfigTable.EnergyDefaultChangePerSecondConfig));
    }
}
