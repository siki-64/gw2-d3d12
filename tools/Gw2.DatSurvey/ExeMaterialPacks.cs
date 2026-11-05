using System.Buffers.Binary;
using System.Text;
using Gw2.Dat.Content;
using Gw2.Dat.Archive;

namespace Gw2.DatSurvey;

public sealed record ExeEmbeddedPack(int FileOffset, string Type, Gw2AssetDocument Document);

public static class ExeMaterialPacks
{
    private const int PackHeaderSize = 12;
    private const int ChunkHeaderSize = 8;
    private const ushort PackHeaderSizeField = 0x0C;

    public static IReadOnlyList<ExeEmbeddedPack> ReadAll(ReadOnlyMemory<byte> exe)
    {
        var bytes = exe.Span;
        var packs = new List<ExeEmbeddedPack>();
        for (var offset = 0; offset + PackHeaderSize + ChunkHeaderSize <= bytes.Length; offset++)
        {
            if (bytes[offset] != 'P' || bytes[offset + 1] != 'F' || BinaryPrimitives.ReadUInt16LittleEndian(bytes[(offset + 6)..]) != PackHeaderSizeField) continue;
            var type = bytes.Slice(offset + 8, 4);
            if (!IsTag(type)) continue;
            var end = offset + PackHeaderSize;
            while (end + ChunkHeaderSize <= bytes.Length && IsTag(bytes.Slice(end, 4)))
            {
                var next = end + ChunkHeaderSize + (long)BinaryPrimitives.ReadUInt32LittleEndian(bytes[(end + 4)..]);
                if (next > bytes.Length) break;
                end = (int)next;
            }

            if (end == offset + PackHeaderSize) continue;
            packs.Add(new ExeEmbeddedPack(offset, Encoding.ASCII.GetString(type), Gw2AssetDocument.Parse(exe[offset..end], Gw2PackSchemaCatalog.Default)));
            offset = end - 1;
        }

        return packs;
    }

    public static IReadOnlyList<ExeEmbeddedPack> ReadMaterials(ReadOnlyMemory<byte> exe) =>
        ReadAll(exe).Where(pack => pack.Type == "AMAT").ToList();

    private static bool IsTag(ReadOnlySpan<byte> tag)
    {
        foreach (var character in tag)
            if (character is not (>= (byte)'A' and <= (byte)'Z' or >= (byte)'0' and <= (byte)'9')) return false;
        return true;
    }
}
