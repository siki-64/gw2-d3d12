using System.Buffers.Binary;
using System.Text;

namespace Gw2.DatSurvey.Tests;

internal static class SyntheticArchive
{
    public static byte[] Attex(string format, ushort width, ushort height, uint flags, byte[] payload)
    {
        var bytes = new byte[20 + payload.Length];
        "ATEX"u8.CopyTo(bytes);
        Encoding.ASCII.GetBytes(format).CopyTo(bytes, 4);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8), width);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(10), height);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), (uint)(payload.Length + 8));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), flags);
        payload.CopyTo(bytes, 20);
        return bytes;
    }

    public static byte[] MipChain(string format, params (ushort Width, ushort Height, uint Flags, byte[] Payload)[] mips)
    {
        var output = new MemoryStream();
        Span<byte> header = stackalloc byte[12];
        "ATEX"u8.CopyTo(header);
        Encoding.ASCII.GetBytes(format).CopyTo(header[4..]);
        BinaryPrimitives.WriteUInt16LittleEndian(header[8..], mips[0].Width);
        BinaryPrimitives.WriteUInt16LittleEndian(header[10..], mips[0].Height);
        output.Write(header);
        foreach (var mip in mips)
            output.Write(Attex(format, mip.Width, mip.Height, mip.Flags, mip.Payload).AsSpan(12));
        return output.ToArray();
    }

    public static byte[] DdsUncompressed(ushort width, ushort height)
    {
        var data = new byte[128 + width * height * 4];
        "DDS "u8.CopyTo(data);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 124);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), 0x1 | 0x2 | 0x4 | 0x1000);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), height);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16), width);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(20), (uint)(width * height * 4));
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(76), 32);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(80), 0x41);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(88), 32);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(108), 0x1000);
        for (var i = 128; i < data.Length; i++) data[i] = (byte)i;
        return data;
    }

    public static byte[] Dat(params (uint ContentFileId, byte[] Record)[] assets)
    {
        const int headerSize = 40;
        const int mftOffset = 64;
        const int mftHeaderSize = 24;
        const int mftEntrySize = 24;

        var recordCount = (uint)(2 + assets.Length);
        var mftSize = (uint)(mftHeaderSize + recordCount * mftEntrySize);
        var atocOffset = mftOffset + mftSize;
        var atocSize = (uint)(assets.Length * 8);
        var dataOffset = atocOffset + atocSize;

        var data = new byte[dataOffset + assets.Sum(asset => asset.Record.Length)];

        data[0] = 1;
        "AN\x1A"u8.CopyTo(data.AsSpan(1));
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), headerSize);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), 0x1000);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(24), mftOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(32), mftSize);

        "Mft\x1A"u8.CopyTo(data.AsSpan(mftOffset));
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(mftOffset + 12), recordCount + 1);

        WriteMftEntry(data, mftOffset + mftHeaderSize + mftEntrySize, (ulong)atocOffset, atocSize);

        var recordOffset = (long)dataOffset;
        for (var i = 0; i < assets.Length; i++)
        {
            var recordIndex = 2 + i;
            WriteMftEntry(data, mftOffset + mftHeaderSize + recordIndex * mftEntrySize, (ulong)recordOffset, (uint)assets[i].Record.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan((int)(atocOffset + i * 8)), assets[i].ContentFileId);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan((int)(atocOffset + i * 8 + 4)), (uint)(recordIndex + 1));
            assets[i].Record.CopyTo(data, recordOffset);
            recordOffset += assets[i].Record.Length;
        }

        return data;
    }

    private static void WriteMftEntry(byte[] data, int offset, ulong recordOffset, uint size)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(offset), recordOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset + 8), size);
    }
}
