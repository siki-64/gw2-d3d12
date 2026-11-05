using System.Buffers.Binary;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Formats.Tests;

[TestClass]
public sealed class CptRc4Tests
{
    [TestMethod]
    public void KeyScheduleAndProcessAreStandardRc4()
    {
        var data = Encoding.ASCII.GetBytes("Plaintext");
        CptRc4.KeySchedule(Encoding.ASCII.GetBytes("Key")).Process(data);
        CollectionAssert.AreEqual(Convert.FromHexString("BBF316E8D940AF0AD3"), data);
    }

    [TestMethod]
    public void ProcessContinuesTheKeystreamAcrossCalls()
    {
        var whole = new byte[32];
        CptRc4.KeySchedule(0x0123456789ABCDEF).Process(whole);
        var split = new byte[32];
        var cipher = CptRc4.KeySchedule(0x0123456789ABCDEF);
        cipher.Process(split.AsSpan(0, 11));
        cipher.Process(split.AsSpan(11));
        CollectionAssert.AreEqual(whole, split);
    }

    [TestMethod]
    public void LongKeyBytesBeyondTwentyFoldIntoTheDerivedKey()
    {
        var shortKey = new byte[20];
        var longKey = new byte[21];
        longKey[20] = 0x01;
        CollectionAssert.AreNotEqual(CptRc4.DeriveKey(shortKey), CptRc4.DeriveKey(longKey));
    }

    [TestMethod]
    public void EightByteDerivationMatchesTheTextKeyDerivation()
    {
        const ulong key = 0x0123456789ABCDEF;
        var keyBytes = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(keyBytes, key);
        CollectionAssert.AreEqual(CptRc4.DeriveKey(key), CptRc4.DeriveKey(keyBytes));
    }
}
