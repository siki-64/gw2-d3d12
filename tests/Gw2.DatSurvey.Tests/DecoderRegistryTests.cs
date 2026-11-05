using System.Reflection;
using System.Text;
using Gw2.Dat.Content;
using Gw2.Dat.Archive;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.DatSurvey.Tests;

[TestClass]
public sealed class DecoderRegistryTests
{
    private static readonly Assembly[] DatAssemblies = [typeof(Gw2AssetDocument).Assembly, typeof(Gw2Model).Assembly];

    [TestMethod]
    public void EveryPackDecoderEntryPointHasARoute()
    {
        var entryPoints = DatAssemblies.SelectMany(assembly => assembly.GetExportedTypes())
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(method => method.Name is "Parse" or "TryParse"
                && method.GetParameters() is [{ ParameterType: var parameter }] && parameter == typeof(Gw2AssetDocument))
            .Select(method => method.ReturnType.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var routed = Gw2PackDecoders.All.Select(decoder => decoder.Name).Distinct().Order(StringComparer.Ordinal).ToArray();
        CollectionAssert.AreEquivalent(entryPoints, routed);
    }

    [TestMethod]
    public void TypedLookupSkipsDocumentsWithoutAMatchingRoute()
    {
        var document = new Gw2AssetDocument(null!, []);
        Assert.IsNull(Gw2PackDecoders.Decode<Gw2Model>(document));
    }

    [TestMethod]
    public void TypedLookupRejectsTypesWithoutARoute() =>
        Assert.ThrowsExactly<InvalidOperationException>(() => Gw2PackDecoders.Decode<string>(new Gw2AssetDocument(null!, [])));

    [TestMethod]
    public void PackRoutesMatchRootTypePrefixOrChunkType()
    {
        var document = Gw2AssetDocument.Parse(Pack("test", new byte[8]), Catalog());
        Assert.AreEqual("PackTestV0", document.Chunks[0].Root!.TypeName);
        Assert.IsTrue(new Gw2PackDecoder("ByRoot", "PackTest", null, _ => null).Accepts(document));
        Assert.IsTrue(new Gw2PackDecoder("ByChunk", null, "test", _ => null).Accepts(document));
        Assert.IsFalse(new Gw2PackDecoder("Other", "PackTestX", "othr", _ => null).Accepts(document));
        Assert.AreEqual(0, Gw2PackDecoders.For(document).Count);
    }

    [TestMethod]
    public void DecodeObjectsFollowsTheFileKind()
    {
        Assert.AreEqual(0, Gw2ContentReader.DecodeObjects(Gw2DatFileKind.Pack, Pack("test", new byte[8]), Catalog()).Count);
        Assert.AreEqual(0, Gw2ContentReader.DecodeObjects(Gw2DatFileKind.Text, "abcd"u8.ToArray()).Count);
        var strings = Gw2ContentReader.DecodeObjects(Gw2DatFileKind.Strings, [.. "strs"u8, 0, 0]);
        Assert.AreEqual(nameof(Gw2TextEntry), strings.Single().Decoder);
    }

    private const string CatalogJson = """
        {"format":"gw2-native-pack-schemas-v1","executableSha256":"00",
         "registrations":[{"chunk":"test","address":"1","versions":{"0":"A"}}],
         "types":{"A":{"name":"PackTestV0","fields":[{"kind":6,"name":"value","count":0}]}}}
        """;

    private static Gw2PackSchemaCatalog Catalog() => Gw2PackSchemaCatalog.Load(new MemoryStream(Encoding.UTF8.GetBytes(CatalogJson)));

    private static byte[] Pack(string chunkType, byte[] payload)
    {
        var bytes = new byte[12 + 16 + payload.Length];
        "PF"u8.CopyTo(bytes);
        bytes[2] = 4;
        bytes[6] = 12;
        "abcd"u8.CopyTo(bytes.AsSpan(8));
        Encoding.ASCII.GetBytes(chunkType).CopyTo(bytes, 12);
        BitConverter.GetBytes((uint)(8 + payload.Length)).CopyTo(bytes, 16);
        bytes[22] = 16;
        payload.CopyTo(bytes, 28);
        return bytes;
    }
}
