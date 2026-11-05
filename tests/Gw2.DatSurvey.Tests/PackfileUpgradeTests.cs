using System.Runtime.InteropServices;
using System.Text;
using Gw2.Dat.Archive;
using Gw2.Formats;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.DatSurvey.Tests;

[TestClass]
public sealed unsafe class PackfileUpgradeTests
{
    private const int SampleStride = 13;
    private const int RequiredNarrowChunks = 20;
    private const int RequiredUpgradedChunks = 1500;

    [TestMethod]
    public void UpgradedChunksKeepEveryValueTheSchemaReaderDecodesAtTheFileVersion()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        using var archive = FileArchive.Open(dat, verifyChecksums: false);
        var schema = PackfileSchema.Default;
        var reader = new Gw2PackReader();
        int upgradedChunks = 0, narrowChunks = 0, sampled = 0;
        foreach (uint recordIndex in archive.FileIndex.Values.Distinct().Order())
        {
            if (sampled++ % SampleStride != 0) continue;
            byte[] prefix;
            try { prefix = archive.ReadDecodedPrefix(recordIndex); }
            catch (InvalidDataException) { continue; }
            if (prefix.Length < 12 || prefix[0] != 'P' || prefix[1] != 'F') continue;
            var decoded = new MemoryStream();
            archive.DecodeEntry(recordIndex, decoded);
            byte[] bytes = decoded.ToArray();
            Gw2PackFile expectedFile = Gw2PackFile.Parse(bytes);
            Packfile packfile;
            try { packfile = Packfile.Open(bytes)!; }
            catch (NotSupportedException) { continue; }
            using (packfile)
            {
                bool narrow = (packfile.Flags & PackfileHeader.FlagPointers64) == 0;
                foreach (Gw2PackChunk chunk in expectedFile.Chunks.Where(chunk => chunk.Bytes.Length > chunk.HeaderSize).GroupBy(chunk => chunk.Type).Select(group => group.Last()))
                {
                    uint chunkType = FourCc(chunk.Type);
                    if (schema.Latest(chunkType, chunk.Version) is not { } latest || latest.Version == chunk.Version) continue;
                    if (CrossesUpgradeRoutine(schema, chunkType, chunk.Version, latest.Version)) continue;
                    PackObject expected;
                    try { expected = reader.Read(expectedFile, chunk); }
                    catch (Exception exception) when (exception is NotSupportedException or Gw2PackReadLimitExceededException or InvalidDataException) { continue; }
                    PackfileChunk upgraded = packfile.Chunk(chunkType)!.Value;
                    Assert.AreEqual(latest.Version, upgraded.Version);
                    CompareStruct(expected, schema.Type(chunkType, chunk.Version)!, latest.Type, upgraded.Data, $"record {recordIndex} {chunk.Type} v{chunk.Version}");
                    upgradedChunks++;
                    if (narrow) narrowChunks++;
                }
            }
            if (upgradedChunks >= RequiredUpgradedChunks && narrowChunks >= RequiredNarrowChunks) break;
        }
        Assert.IsTrue(upgradedChunks >= RequiredUpgradedChunks, $"only {upgradedChunks} upgraded chunks");
        Assert.IsTrue(narrowChunks >= RequiredNarrowChunks, $"only {narrowChunks} upgraded chunks from packfiles with 4-byte pointers");
    }

    private static void CompareStruct(PackObject expected, PackType sourceType, PackType destinationType, byte* actual, string path)
    {
        int offset = 0;
        foreach (PackField destinationField in destinationType.Fields)
        {
            var match = ReferenceEquals(sourceType, destinationType) ? (destinationField, offset) : PackfileUpgrade.FindMatch(destinationField, sourceType);
            string fieldPath = $"{path}.{destinationField.Name}";
            if (match is { } found) CompareField(expected.Fields[RecordedName(sourceType, found.Field)], found.Field, destinationField, actual + offset, fieldPath);
            else
                for (int index = 0; index < destinationField.Size; index++)
                    Assert.AreEqual((byte)0, actual[offset + index], $"{fieldPath} is not cleared");
            offset += destinationField.Size;
        }
    }

    private static bool CrossesUpgradeRoutine(PackfileSchema schema, uint chunkType, ushort fileVersion, ushort latestVersion)
    {
        var versions = schema.Registration(chunkType, fileVersion, 0)!.Versions;
        for (int version = fileVersion + 1; version <= latestVersion; version++)
            if (versions.TryGetValue((ushort)version, out var type) && PackfileUpgradeRoutines.For(type) is not null) return true;
        return false;
    }

    private static string RecordedName(PackType type, PackField field)
    {
        int occurrence = 0;
        foreach (PackField candidate in type.Fields)
        {
            if (candidate.Name == field.Name) occurrence++;
            if (ReferenceEquals(candidate, field)) break;
        }
        return occurrence == 1 ? field.Name : $"{field.Name}#{occurrence}";
    }

    private static void CompareElement(object? expected, PackType sourceType, PackType destinationType, byte* actual, string path)
    {
        if (expected is PackObject obj) CompareStruct(obj, sourceType, destinationType, actual, path);
        else CompareField(expected, sourceType.Fields[0], destinationType.Fields[0], actual, path);
    }

    private static void CompareField(object? expected, PackField sourceField, PackField destinationField, byte* actual, string path)
    {
        switch (destinationField.Kind)
        {
            case PackFieldKind.FixedArray:
            {
                if (expected is ReadOnlyMemory<byte> fixedBlob)
                {
                    int length = Math.Min(fixedBlob.Length, destinationField.Count);
                    CollectionAssert.AreEqual(fixedBlob[..length].ToArray(), new ReadOnlySpan<byte>(actual, length).ToArray(), path);
                    break;
                }
                var elements = (object?[])expected!;
                int stride = destinationField.Reference!.Size;
                for (int index = 0; index < Math.Min(elements.Length, destinationField.Count); index++)
                    CompareElement(elements[index], sourceField.Reference!, destinationField.Reference, actual + index * stride, $"{path}[{index}]");
                break;
            }
            case PackFieldKind.Array:
            case PackFieldKind.PointerArray:
            {
                uint count = *(uint*)actual;
                byte* elements = *(byte**)(actual + 4);
                if (expected is ReadOnlyMemory<byte> blob)
                {
                    Assert.AreEqual((uint)blob.Length, count, path);
                    CollectionAssert.AreEqual(blob.ToArray(), new ReadOnlySpan<byte>(elements, (int)count).ToArray(), path);
                    break;
                }
                var expectedElements = (object?[])expected!;
                Assert.AreEqual((uint)expectedElements.Length, count, path);
                int stride = destinationField.Reference!.Size;
                for (int index = 0; index < expectedElements.Length; index++)
                {
                    byte* element = destinationField.Kind == PackFieldKind.PointerArray ? ((byte**)elements)[index] : elements + index * stride;
                    if (expectedElements[index] is null && destinationField.Kind == PackFieldKind.PointerArray) { Assert.IsTrue(element == null, $"{path}[{index}]"); continue; }
                    CompareElement(expectedElements[index], sourceField.Reference!, destinationField.Reference, element, $"{path}[{index}]");
                }
                break;
            }
            case PackFieldKind.Pointer:
            {
                byte* target = *(byte**)actual;
                if (expected is null) { Assert.IsTrue(target == null, path); break; }
                CompareElement(expected, sourceField.Reference!, destinationField.Reference!, target, path);
                break;
            }
            case PackFieldKind.Struct:
            case PackFieldKind.StructBase:
                CompareElement(expected, sourceField.Reference!, destinationField.Reference!, actual, path);
                break;
            case PackFieldKind.Variant:
            {
                var variant = (PackVariant)expected!;
                Assert.AreEqual((uint)variant.Case, *(uint*)actual, path);
                byte* target = *(byte**)(actual + 4);
                if (variant.Payload is null) { Assert.IsTrue(target == null, path); break; }
                CompareStruct(variant.Payload, sourceField.Variants[variant.Case], destinationField.Variants[variant.Case], target, path);
                break;
            }
            case PackFieldKind.WideString:
            case PackFieldKind.String:
            {
                byte* target = *(byte**)actual;
                if (expected is null) { Assert.IsTrue(target == null, path); break; }
                string text = destinationField.Kind == PackFieldKind.WideString
                    ? new string((char*)target)
                    : Encoding.UTF8.GetString(MemoryMarshal.CreateReadOnlySpanFromNullTerminated(target));
                Assert.AreEqual((string)expected, text, path);
                break;
            }
            case PackFieldKind.FileName:
            case PackFieldKind.FileReference:
            {
                byte* target = *(byte**)actual;
                if (expected is null) { Assert.IsTrue(target == null, path); break; }
                var reference = (PackFileReference)expected;
                Assert.AreEqual(reference.Low, ((ushort*)target)[0], path);
                Assert.AreEqual(reference.High, ((ushort*)target)[1], path);
                break;
            }
            default:
                CollectionAssert.AreEqual(ExpectedBytes(expected, destinationField.Size), new ReadOnlySpan<byte>(actual, destinationField.Size).ToArray(), path);
                break;
        }
    }

    private static byte[] ExpectedBytes(object? value, int size) => value switch
    {
        byte single => [single],
        ushort word => BitConverter.GetBytes(word),
        uint dword => BitConverter.GetBytes(dword),
        ulong qword => BitConverter.GetBytes(qword),
        float single => BitConverter.GetBytes(single),
        double wide => BitConverter.GetBytes(wide),
        float[] floats => floats.SelectMany(BitConverter.GetBytes).ToArray(),
        uint[] dwords => dwords.SelectMany(BitConverter.GetBytes).ToArray(),
        ushort[] words => words.SelectMany(BitConverter.GetBytes).ToArray(),
        ReadOnlyMemory<byte> memory => memory.ToArray(),
        _ => throw new AssertFailedException($"unexpected value {value?.GetType().Name} for a {size}-byte field"),
    };

    private static uint FourCc(string type) => (uint)(type[0] | type[1] << 8 | type[2] << 16 | type[3] << 24);
}
