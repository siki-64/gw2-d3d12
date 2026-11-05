using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using Gw2.Formats;

namespace Gw2.Protocol.Login;

public sealed record RememberedLogin(string Email, byte[] ProtectedPassword, byte PasswordVersion)
{
    public const uint LocalSettingsFileId = 16;
    public const ushort SettingsVersion = 8;
    private const uint SettingsChunk = 0x636C7875;
    private const int PackHeaderSizeOffset = 6;
    private const int ChunkHeaderLength = 12;
    private const int ChunkSizeOffset = 4;
    private const int ChunkVersionOffset = 8;
    private const int ChunkDataOffsetOffset = 10;
    private const int PasswordVersionOffset = 10;
    private const int PasswordLengthOffset = 11;
    private const int PasswordPointerOffset = 15;
    private const int EmailPointerOffset = 35;
    private const int SettingsLength = 0x4B;

    public static RememberedLogin? Read(FileArchive localArchive)
    {
        if (!localArchive.FileIndex.ContainsKey(LocalSettingsFileId)) return null;
        using var decoded = new MemoryStream();
        localArchive.DecodeFileId(LocalSettingsFileId, decoded);
        return Parse(decoded.ToArray());
    }

    public static RememberedLogin? Parse(ReadOnlySpan<byte> pack)
    {
        if (pack.Length < PackHeaderSizeOffset + 2) return null;
        int chunk = BinaryPrimitives.ReadUInt16LittleEndian(pack[PackHeaderSizeOffset..]);
        while (chunk + ChunkHeaderLength <= pack.Length)
        {
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(pack[chunk..]);
            int size = BinaryPrimitives.ReadInt32LittleEndian(pack[(chunk + ChunkSizeOffset)..]);
            if (type == SettingsChunk)
            {
                if (BinaryPrimitives.ReadUInt16LittleEndian(pack[(chunk + ChunkVersionOffset)..]) != SettingsVersion) return null;
                int settings = chunk + BinaryPrimitives.ReadUInt16LittleEndian(pack[(chunk + ChunkDataOffsetOffset)..]);
                return settings + SettingsLength <= pack.Length ? ReadSettings(pack, settings) : null;
            }
            chunk += 8 + size;
        }
        return null;
    }

    private static RememberedLogin? ReadSettings(ReadOnlySpan<byte> pack, int settings)
    {
        int passwordLength = BinaryPrimitives.ReadInt32LittleEndian(pack[(settings + PasswordLengthOffset)..]);
        int? password = RelativeTarget(pack, settings + PasswordPointerOffset);
        int? email = RelativeTarget(pack, settings + EmailPointerOffset);
        if (password is not { } passwordAt || email is not { } emailAt || passwordLength <= 0 || passwordAt + passwordLength > pack.Length) return null;
        int emailEnd = emailAt;
        while (emailEnd + 1 < pack.Length && (pack[emailEnd] | pack[emailEnd + 1]) != 0) emailEnd += 2;
        return new RememberedLogin(
            Encoding.Unicode.GetString(pack[emailAt..emailEnd]),
            pack.Slice(passwordAt, passwordLength).ToArray(),
            pack[settings + PasswordVersionOffset]);
    }

    private static int? RelativeTarget(ReadOnlySpan<byte> pack, int pointerField)
    {
        long offset = BinaryPrimitives.ReadInt64LittleEndian(pack[pointerField..]);
        long target = pointerField + offset;
        return offset == 0 || target < 0 || target >= pack.Length ? null : (int)target;
    }
}
