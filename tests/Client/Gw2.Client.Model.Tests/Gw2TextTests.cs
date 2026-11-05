using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using Gw2.Dat.Content;
using Gw2.Dat.Archive;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2TextTests
{
    private const uint SleepingJotunTextId = 0x7C37;
    private const ulong SleepingJotunKey = 0x000001BF00DDBF2A;
    private const uint PungentGladiolusTextId = 0x5092D;
    private const ulong PungentGladiolusKey = 0x00001CD7945EC8A7;

    private static Gw2ContentReader OpenOrSkip()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        return Gw2ContentReader.Open(dat, verifyChecksums: false);
    }

    [TestMethod]
    public void ManifestListsOneThousandTwentyFourStringsPerFile()
    {
        using var reader = OpenOrSkip();

        Assert.AreEqual(1024u, reader.TextManifest.StringsPerFile);
        Assert.AreEqual(0, reader.TextManifest.Languages[(int)Gw2Language.Korean].FileIds.Count);
    }

    [TestMethod]
    public void CapturedKeysDecryptNames()
    {
        using var reader = OpenOrSkip();

        Assert.AreEqual((Gw2TextStatus.Decrypted, "Sleeping Jotun"), reader.ReadText(SleepingJotunTextId, Gw2Language.English, SleepingJotunKey));
        Assert.AreEqual((Gw2TextStatus.Decrypted, "Gladiolo acre"), reader.ReadText(PungentGladiolusTextId, Gw2Language.Spanish, PungentGladiolusKey));
        Assert.AreEqual(Gw2TextStatus.Encrypted, reader.ReadText(SleepingJotunTextId, Gw2Language.English).Status);
    }
}
