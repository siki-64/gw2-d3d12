using Gw2.Formats;
using System.Buffers.Binary;
using System.Text;
using Gw2.Dat.Archive;

namespace Gw2.DatSurvey.Tests;

[TestClass]
public sealed class Gw2PackVariantTests
{
    private const string CatalogJson = """
        {"format":"gw2-native-pack-schemas-v1","executableSha256":"00",
         "registrations":[{"chunk":"vari","address":"1","versions":{"0":"A"}}],
         "types":{
          "A":{"name":"PackVariantRootV0","fields":[{"kind":28,"name":"action","count":3,"variants":["B","C","D"]},{"kind":10,"name":"after","count":0}]},
          "B":{"name":"PackCaseWordV0","fields":[{"kind":10,"name":"word","count":0}]},
          "C":{"name":"PackCaseTokenV0","fields":[{"kind":17,"name":"token","count":0}]},
          "D":{"name":"PackCaseScaleV0","fields":[{"kind":12,"name":"scale","count":0}]}}}
        """;

    [TestMethod]
    [DataRow(0, "B", "word")]
    [DataRow(1, "C", "token")]
    [DataRow(2, "D", "scale")]
    public void ReadsEachCaseWithItsPayload(int tag, string typeId, string payloadField)
    {
        var root = Read(Chunk(tag, payload: new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 }));
        var variant = (PackVariant)root.Fields["action"]!;
        Assert.AreEqual(tag, variant.Case);
        Assert.AreEqual(typeId, variant.TypeId);
        Assert.IsNotNull(variant.Payload);
        Assert.IsTrue(variant.Payload.Fields.ContainsKey(payloadField));
        Assert.AreEqual(0xABCDu, root.Fields["after"]);
    }

    [TestMethod]
    public void NullPointerGivesCaseWithoutPayload()
    {
        var variant = (PackVariant)Read(Chunk(1, payload: null)).Fields["action"]!;
        Assert.AreEqual(1, variant.Case);
        Assert.IsNull(variant.Payload);
    }

    [TestMethod]
    public void TagOutsideCasesThrows()
        => Assert.ThrowsExactly<InvalidDataException>(() => Read(Chunk(3, payload: null)));

    private static PackObject Read(Gw2PackChunk chunk)
        => new Gw2PackReader(Gw2PackSchemaCatalog.Load(new MemoryStream(Encoding.UTF8.GetBytes(CatalogJson)))).Read(chunk, 8);

    private static Gw2PackChunk Chunk(int tag, byte[]? payload)
    {
        const int header = 16, root = header, payloadAt = header + 16;
        var bytes = new byte[payloadAt + 8];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(root), (uint)tag);
        if (payload is not null)
        {
            BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(root + 4), payloadAt - (root + 4));
            payload.CopyTo(bytes, payloadAt);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(root + 12), 0xABCD);
        return new Gw2PackChunk("vari", 0, 0, header, bytes, 0);
    }
}
