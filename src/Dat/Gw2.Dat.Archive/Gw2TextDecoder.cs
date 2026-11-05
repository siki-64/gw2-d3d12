using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Gw2.Formats;

namespace Gw2.Dat.Archive;

public enum Gw2TextStatus
{
    Missing,
    Plain,
    Decrypted,
    Encrypted,
    MissingKey,
}

public readonly record struct Gw2TextEntry(ReadOnlyMemory<byte> Entry)
{
    private const byte Utf16Bits = 16;

    public ushort BaseCharacter => BinaryPrimitives.ReadUInt16LittleEndian(Entry.Span[2..]);
    public byte BitsPerCharacter => Entry.Span[4];
    public ReadOnlyMemory<byte> Payload => Entry[TextDecode.EntryHeaderBytes..];
    public bool IsUtf16 => BaseCharacter == 0 && BitsPerCharacter == Utf16Bits;
    public bool IsEncrypted => !Payload.IsEmpty && BaseCharacter != 0;

    public static IReadOnlyList<Gw2TextEntry> ReadFile(ReadOnlySpan<byte> file)
    {
        var entries = new List<Gw2TextEntry>();
        var position = file.Length >= 4 && BinaryPrimitives.ReadUInt32LittleEndian(file) == Gw2TextDecoder.StringFileMagic ? 4 : 0;
        var bytes = file.ToArray();
        while (position + TextDecode.EntryHeaderBytes <= file.Length)
        {
            var size = BinaryPrimitives.ReadUInt16LittleEndian(file[position..]);
            var bits = file[position + 4];
            if (size < TextDecode.EntryHeaderBytes || position + size > file.Length || bits > Utf16Bits)
                break;
            entries.Add(new Gw2TextEntry(bytes.AsMemory(position, size)));
            position += size;
        }
        return entries;
    }
}

public static class Gw2TextDecoder
{
    public const uint StringFileMagic = 0x73727473;

    public static (Gw2TextStatus Status, string Text) Decode(Gw2TextEntry entry, ulong? key)
    {
        if (entry.Payload.IsEmpty && entry.BaseCharacter == 0 && entry.BitsPerCharacter == 0)
            return (Gw2TextStatus.Missing, string.Empty);
        if (TextDecode.Decrypt(entry.Entry.Span, key ?? TextDecode.NoKey) is not { } text)
            return (Gw2TextStatus.Encrypted, string.Empty);
        return (key is null ? Gw2TextStatus.Plain : Gw2TextStatus.Decrypted, text);
    }
}
