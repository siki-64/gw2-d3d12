using System;
using System.IO;
using Gw2.Formats;

namespace Gw2.Protocol.Login;

public sealed record InstallRecord(Guid InstallId, uint UseCount, uint ComputerNameHash)
{
    public const uint LocalArchiveFileId = 2;
    public const int Length = 24;
    public const uint ReplacedAfterUses = 3;
    private const uint FirstSeed = 561029770;
    private const uint SecondSeed = 3804322973;
    private const uint HashSeed = 844963502;
    private static readonly uint[] HashMix =
    {
        0x92b9a528, 0x25d4fc88, 0xedcbefb8, 0x51063a80, 0x91341c61, 0x0261229d, 0x726f48ed, 0xce1c088c,
        0x76253eb5, 0x31e3a0de, 0xa2aad215, 0xca7d6d27, 0xa5f98970, 0x0541c365, 0x3c14ff04, 0x5056af4f,
    };

    public static InstallRecord? Read(FileArchive localArchive)
    {
        if (!localArchive.FileIndex.ContainsKey(LocalArchiveFileId)) return null;
        using var bytes = new MemoryStream();
        localArchive.DecodeFileId(LocalArchiveFileId, bytes);
        return Parse(bytes.ToArray());
    }

    public static InstallRecord? Parse(ReadOnlySpan<byte> bytes) =>
        bytes.Length != Length ? null : new InstallRecord(new Guid(bytes[..16]), BitConverter.ToUInt32(bytes[16..]), BitConverter.ToUInt32(bytes[20..]));

    public byte[] ToBytes()
    {
        var bytes = new byte[Length];
        InstallId.TryWriteBytes(bytes);
        BitConverter.TryWriteBytes(bytes.AsSpan(16), UseCount);
        BitConverter.TryWriteBytes(bytes.AsSpan(20), ComputerNameHash);
        return bytes;
    }

    public bool BelongsTo(string computerName) => ComputerNameHash == HashComputerName(computerName) || UseCount >= ReplacedAfterUses;

    public static uint HashComputerName(string computerName)
    {
        uint mix = FirstSeed;
        uint shifted = SecondSeed;
        uint hash = HashSeed;
        foreach (char character in computerName)
        {
            uint upper = character is >= 'a' and <= 'z' ? (uint)character - 32 : character;
            uint value = (shifted << 3) ^ upper;
            shifted = value;
            mix += HashMix[value & 0xF];
            hash ^= mix + value;
        }
        return hash;
    }
}
