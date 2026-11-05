using System.Buffers.Binary;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Formats.Tests;

[TestClass]
public sealed class TextDecodeTests
{
    private const ulong Key = 0x00001CD7945EC8A7;

    private static byte[] Entry(ushort baseCharacter, byte bitsPerCharacter, byte[] payload)
    {
        var entry = new byte[TextDecode.EntryHeaderBytes + payload.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(entry, (ushort)entry.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(entry.AsSpan(2), baseCharacter);
        entry[4] = bitsPerCharacter;
        payload.CopyTo(entry, TextDecode.EntryHeaderBytes);
        return entry;
    }

    private static byte[] Pack(byte bitsPerCharacter, params uint[] codes)
    {
        var bytes = new List<byte>();
        ulong rack = 0;
        int rackBits = 0;
        foreach (uint code in codes)
        {
            rack |= (ulong)code << rackBits;
            rackBits += bitsPerCharacter;
            for (; rackBits >= 8; rackBits -= 8, rack >>= 8) bytes.Add((byte)rack);
        }
        if (rackBits > 0) bytes.Add((byte)rack);
        return [.. bytes];
    }

    [TestMethod]
    public void PlainUtf16StopsAtTheTerminator() =>
        Assert.AreEqual("Mug", TextDecode.Decrypt(Entry(0, 16, Encoding.Unicode.GetBytes("Mug\0x")), TextDecode.NoKey));

    [TestMethod]
    public void PackedCodesIndexTheTableAndOffsetTheBaseCharacter()
    {
        var payload = Pack(6, 0x20 + 'h' - 'a', 0x20 + 'i' - 'a', 30, 0);
        CptRc4.KeySchedule(Key).Process(payload);
        Assert.AreEqual("hi!", TextDecode.Decrypt(Entry('a', 6, payload), Key));
    }

    [TestMethod]
    public void PackedTextWithoutAKeyIsAPlaceholder() =>
        Assert.IsNull(TextDecode.Decrypt(Entry('a', 6, Pack(6, 0x20)), TextDecode.NoKey));

    [TestMethod]
    public void EntryShorterThanItsHeaderIsAPlaceholder() =>
        Assert.IsNull(TextDecode.Decrypt(new byte[4], TextDecode.NoKey));

    [TestMethod]
    public void KeyedEntryDecryptsBeforeUnpacking()
    {
        var payload = Pack(6, 0x20 + 'o' - 'a', 0x20 + 'k' - 'a', 0);
        CptRc4.KeySchedule(Key).Process(payload);
        Assert.AreEqual("ok", TextDecode.Decrypt(Entry('a', 6, payload), Key));
    }
}
