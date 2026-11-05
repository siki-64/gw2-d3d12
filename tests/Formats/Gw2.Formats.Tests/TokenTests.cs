using Gw2.Formats;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Formats.Tests;

[TestClass]
public sealed class TokenTests
{
    [TestMethod]
    public void MatchesTheRecoveredConstantRegistry()
    {
        var checkedCount = 0;
        foreach (var (name, expected) in ConstantTokenRegistry.Tokens)
        {
            Assert.AreEqual(expected, Token.Encode(name), $"token for '{name}'");
            checkedCount++;
        }

        Assert.IsTrue(checkedCount > 150, $"only {checkedCount} tokens checked");
    }

    [TestMethod]
    public void HashesNamesWithDroppedLetters()
    {
        Assert.AreEqual(0x357970D9u, Token.Encode("grvegp"));
        Assert.AreEqual(0x33B2EF6Du, Token.Encode("conspja"));
        Assert.AreEqual(0x368078C3u, Token.Encode("grltzr"));
        Assert.AreEqual(0x300012DEu, Token.Encode("adj"));
    }

    [TestMethod]
    public void HashesTechniqueQualityNames()
    {
        Assert.AreEqual(0x30002A89u, Token.Encode("low"));
        Assert.AreEqual(0x348AF5E2u, Token.Encode("medium"));
        Assert.AreEqual(0x300159D6u, Token.Encode("high"));
    }

    [TestMethod]
    public void DecodesCanonicalNamesForLowercaseTokens()
    {
        Assert.IsTrue(Token.TryDecode(0x357970D9u, out var grassland));
        Assert.AreEqual("grvegp", grassland);

        Assert.IsTrue(Token.TryDecode(0x300012DEu, out var adjacent));
        Assert.AreEqual("adk", adjacent);
    }

    [TestMethod]
    public void DecodeRoundTripsEveryLowercaseRegistryToken()
    {
        var checkedCount = 0;
        foreach (var (name, token) in ConstantTokenRegistry.Tokens)
        {
            if (name.Any(c => c is < 'a' or > 'z')) continue;

            Assert.IsTrue(Token.TryDecode(token, out var decoded), name);
            Assert.AreEqual(token, Token.Encode(decoded), $"round-trip for '{name}'");
            checkedCount++;
        }

        Assert.IsTrue(checkedCount > 150, $"only {checkedCount} tokens checked");
    }

    [TestMethod]
    public void ArchiveIdsResolveTheLowestIdOfARecordAsTheBaseIdAndTheHighestAsTheFileId()
    {
        var ids = new FileArchiveIds(new Dictionary<uint, uint>
        {
            [0x0002DCDB] = 40,
            [0x003139A8] = 40,
            [0x00002000] = 41,
        });

        Assert.AreEqual(0x0002DCDBu, ids.BaseIdOf(0x003139A8));
        Assert.AreEqual(0x0002DCDBu, ids.BaseIdOf(0x0002DCDB));
        Assert.AreEqual(0x00002000u, ids.BaseIdOf(0x00002000));
        Assert.IsTrue(ids.TryGetFileId(0x0002DCDB, out uint fileId));
        Assert.AreEqual(0x003139A8u, fileId);
        Assert.IsFalse(ids.TryGetBaseId(0x99, out _));
    }
}
