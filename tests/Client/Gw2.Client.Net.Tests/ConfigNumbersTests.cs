using Gw2.Dat.Content;
using Gw2.Formats;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public sealed class ConfigNumbersTests
{
    [TestMethod]
    public void ReadsTheHolosmithHeatThresholds()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var numbers = new Gw2ConfigNumbers(reader.ContentIndex);
        Assert.AreEqual(85f, numbers.NumberOf(Gw2ConfigNumbers.HolosmithOverheatPercentKey));
        Assert.AreEqual(100f, numbers.NumberOf(Gw2ConfigNumbers.HolosmithHeatPenaltyKey));
        Assert.AreEqual(50f, numbers.NumberOf(Gw2ConfigNumbers.HolosmithHeatBonusKey));
    }
}
