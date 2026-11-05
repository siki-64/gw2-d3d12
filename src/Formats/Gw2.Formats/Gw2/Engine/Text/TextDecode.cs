using System.Buffers.Binary;
using System.Text;

namespace Gw2.Formats;

internal static class TextDecode
{
    internal const int EntryHeaderBytes = 6;
    internal const ulong NoKey = 0;
    private const byte Utf16Bits = 16;
    private const uint FirstLiteralCode = 0x20;
    private const string PackedCharacters = "0123456strnum()[]<>%#/:-'\" ,.!\n";

    internal static string? Decrypt(ReadOnlySpan<byte> entry, ulong key)
    {
        if (entry.Length < EntryHeaderBytes) return null;
        int entryBytes = BinaryPrimitives.ReadUInt16LittleEndian(entry);
        if (entryBytes < EntryHeaderBytes || entryBytes > entry.Length) return null;
        ushort baseCharacter = BinaryPrimitives.ReadUInt16LittleEndian(entry[2..]);
        byte bitsPerCharacter = entry[4];
        if (baseCharacter != 0 && key == NoKey) return null;

        byte[] payload = entry[EntryHeaderBytes..entryBytes].ToArray();
        if (key != NoKey) CptRc4.KeySchedule(key).Process(payload);

        if (baseCharacter == 0 && bitsPerCharacter == Utf16Bits && (payload.Length & 1) == 0)
        {
            string text = Encoding.Unicode.GetString(payload);
            int terminator = text.IndexOf('\0');
            return terminator < 0 ? text : text[..terminator];
        }
        return Unpack(payload, baseCharacter, bitsPerCharacter);
    }

    private static string Unpack(byte[] payload, ushort baseCharacter, byte bitsPerCharacter)
    {
        if (bitsPerCharacter == 0) return string.Empty;
        int characterCount = 8 * payload.Length / bitsPerCharacter + 1;
        var text = new StringBuilder(characterCount);
        uint mask = (1u << bitsPerCharacter) - 1;
        uint rack = 0;
        int rackBits = 0;
        int position = 0;
        for (int index = 0; index < characterCount; index++)
        {
            for (; rackBits <= 24; rackBits += 8)
                if (position < payload.Length) rack |= (uint)payload[position++] << rackBits;
            uint code = rack & mask;
            rackBits -= bitsPerCharacter;
            rack >>= bitsPerCharacter;
            if (code == 0) break;
            text.Append(code >= FirstLiteralCode ? (char)(code + baseCharacter - FirstLiteralCode) : PackedCharacters[(int)code - 1]);
        }
        return text.ToString();
    }
}
