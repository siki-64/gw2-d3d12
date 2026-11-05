using Gw2.Formats;
using System.Buffers.Binary;
using System.Text;
using Gw2.Dat.Archive;

namespace Gw2.DatSurvey.Tests;

[TestClass]
public sealed class Gw2PackFileReferenceTests
{
    private const string CatalogJson = """
        {"format":"gw2-native-pack-schemas-v1","executableSha256":"00",
         "registrations":[{"chunk":"fref","address":"1","versions":{"0":"A"}}],
         "types":{"A":{"name":"PackReferenceRootV0","fields":[{"kind":27,"name":"file","count":0},{"kind":10,"name":"after","count":0}]}}}
        """;

    private const int Header = 16;
    private static readonly byte[] EncodedFileId = [0x00, 0x01, 0x00, 0x01, 0x00, 0x00];

    [TestMethod]
    public void RelocatedSlotIsReadAsPointerToReference()
    {
        var data = new byte[12 + 8];
        BinaryPrimitives.WriteInt64LittleEndian(data, 12);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), 0xABCD);
        EncodedFileId.CopyTo(data, 12);
        var root = Read(Chunk(data, relocatedSlots: [0]));
        Assert.AreEqual(1u, ((PackFileReference)root.Fields["file"]!).FileId);
        Assert.AreEqual(0xABCDu, root.Fields["after"]);
    }

    [TestMethod]
    public void UnrelocatedSlotIsReadInline()
    {
        var data = new byte[12];
        EncodedFileId.CopyTo(data, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(6), 0xABCD);
        var root = Read(Chunk(data, relocatedSlots: []));
        Assert.AreEqual(1u, ((PackFileReference)root.Fields["file"]!).FileId);
        Assert.AreEqual(0xABCDu, root.Fields["after"]);
    }

    private static PackObject Read(Gw2PackChunk chunk)
        => new Gw2PackReader(Gw2PackSchemaCatalog.Load(new MemoryStream(Encoding.UTF8.GetBytes(CatalogJson)))).Read(chunk, 8);

    private static Gw2PackChunk Chunk(byte[] data, int[] relocatedSlots)
    {
        var bytes = new byte[Header + data.Length + 4 + 4 * relocatedSlots.Length];
        data.CopyTo(bytes, Header);
        var table = Header + data.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(table), (uint)relocatedSlots.Length);
        for (int i = 0; i < relocatedSlots.Length; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(table + 4 + 4 * i), (uint)relocatedSlots[i]);
        return new Gw2PackChunk("fref", 0, 0, Header, bytes, table);
    }
}
