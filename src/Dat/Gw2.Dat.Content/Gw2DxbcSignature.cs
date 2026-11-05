using System.Buffers.Binary;
using System.Text;

namespace Gw2.Dat.Content;

public sealed record Gw2DxbcSignatureElement(
    string SemanticName,
    int SemanticIndex,
    int SystemValue,
    int ComponentType,
    int Register,
    byte Mask,
    byte ReadWriteMask);

public static class Gw2DxbcSignature
{
    private const int HeaderSize = 8;
    private const int ElementSize = 24;
    private const byte NamePadding = 0xAB;

    public static IReadOnlyList<Gw2DxbcSignatureElement>? TryParse(ReadOnlyMemory<byte> chunk)
    {
        var span = chunk.Span;
        if (span.Length < HeaderSize) return null;
        var count = BinaryPrimitives.ReadInt32LittleEndian(span);
        if (count < 0 || HeaderSize + (long)count * ElementSize > span.Length) return null;

        var elements = new Gw2DxbcSignatureElement[count];
        for (var index = 0; index < count; index++)
        {
            var element = span.Slice(HeaderSize + index * ElementSize, ElementSize);
            var nameOffset = BinaryPrimitives.ReadInt32LittleEndian(element);
            if (nameOffset < 0 || nameOffset >= span.Length) return null;
            var nameLength = span[nameOffset..].IndexOf((byte)0);
            if (nameLength < 0) return null;
            elements[index] = new(
                Encoding.ASCII.GetString(span.Slice(nameOffset, nameLength)),
                BinaryPrimitives.ReadInt32LittleEndian(element[4..]),
                BinaryPrimitives.ReadInt32LittleEndian(element[8..]),
                BinaryPrimitives.ReadInt32LittleEndian(element[12..]),
                BinaryPrimitives.ReadInt32LittleEndian(element[16..]),
                element[20],
                element[21]);
        }

        return elements;
    }

    public static byte[] Write(IReadOnlyList<Gw2DxbcSignatureElement> elements)
    {
        var names = new List<string>();
        var nameOffsets = new Dictionary<string, int>();
        var cursor = HeaderSize + elements.Count * ElementSize;
        foreach (var element in elements)
        {
            if (nameOffsets.ContainsKey(element.SemanticName)) continue;
            nameOffsets[element.SemanticName] = cursor;
            names.Add(element.SemanticName);
            cursor += element.SemanticName.Length + 1;
        }

        var buffer = new byte[(cursor + 3) & ~3];
        buffer.AsSpan(cursor).Fill(NamePadding);
        BinaryPrimitives.WriteInt32LittleEndian(buffer, elements.Count);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(4), HeaderSize);
        for (var index = 0; index < elements.Count; index++)
        {
            var element = elements[index];
            var at = buffer.AsSpan(HeaderSize + index * ElementSize, ElementSize);
            BinaryPrimitives.WriteInt32LittleEndian(at, nameOffsets[element.SemanticName]);
            BinaryPrimitives.WriteInt32LittleEndian(at[4..], element.SemanticIndex);
            BinaryPrimitives.WriteInt32LittleEndian(at[8..], element.SystemValue);
            BinaryPrimitives.WriteInt32LittleEndian(at[12..], element.ComponentType);
            BinaryPrimitives.WriteInt32LittleEndian(at[16..], element.Register);
            at[20] = element.Mask;
            at[21] = element.ReadWriteMask;
        }

        foreach (var name in names)
            Encoding.ASCII.GetBytes(name).CopyTo(buffer, nameOffsets[name]);
        return buffer;
    }
}
