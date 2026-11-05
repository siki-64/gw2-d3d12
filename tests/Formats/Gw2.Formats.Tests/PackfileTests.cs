using Gw2.Formats;
using System.Buffers.Binary;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Formats.Tests;

[TestClass]
public sealed unsafe class PackfileTests
{
    [TestMethod]
    public void RejectsFilesWithoutThePackfileSignature()
    {
        Assert.IsNull(Packfile.Open(new byte[] { (byte)'P', (byte)'X', 4, 0, 0, 0, 12, 0, 1, 2, 3, 4 }));
        Assert.IsNull(Packfile.Open(new byte[] { (byte)'P', (byte)'F', 4, 0, 0, 0, 12, 0 }));
    }

    [TestMethod]
    public void ConvertsRelativePointersToAddressesInsideTheChunk()
    {
        var file = new byte[12 + 16 + 16 + 8];
        "PF"u8.CopyTo(file);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(2), PackfileHeader.FlagPointers64);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(6), 12);
        "test"u8.CopyTo(file.AsSpan(8));
        "main"u8.CopyTo(file.AsSpan(12));
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(16), 16 + 16 + 8 - 8);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(20), 3);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(22), 16);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(24), 16);
        BinaryPrimitives.WriteInt64LittleEndian(file.AsSpan(28), 8);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(44), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(48), 0);

        using var packfile = Packfile.Open(file)!;
        PackfileChunk main = packfile.FileChunk(FourCc("main"))!.Value;

        Assert.AreEqual((ushort)3, main.Version);
        Assert.AreEqual(24u, main.DataSize);
        Assert.AreEqual((long)(main.Data + 8), *(long*)main.Data);
    }

    private static uint FourCc(string chunkType) => BinaryPrimitives.ReadUInt32LittleEndian(Encoding.ASCII.GetBytes(chunkType.PadRight(4, '\0')));
}
