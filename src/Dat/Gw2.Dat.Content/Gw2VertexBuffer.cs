using Gw2.Formats;
using System.Buffers.Binary;
using System.Numerics;

namespace Gw2.Dat.Content;

public sealed record Gw2VertexAttribute(string Name, int Offset, int Size, string? Semantic = null);

public sealed class Gw2VertexBuffer
{
    public Gw2VertexBuffer(uint format, uint count, ReadOnlyMemory<byte> bytes)
    {
        Format = format; Count = count; Bytes = bytes;
        var attributes = new List<Gw2VertexAttribute>();
        int stride = 0;
        void Add(string name, int size, string? semantic = null) { attributes.Add(new(name, stride, size, semantic)); stride += size; }
        if ((format & Gw2VertexFvf.ReservedMask) != 0) throw new NotSupportedException($"Unknown vertex format bits {format:X8}");
        if ((format & Gw2VertexFvf.Position) != 0) Add("Position", Gw2VertexFvf.PositionSize, Gw2VertexFvf.SemanticFor(Gw2VertexFvf.Position));
        if ((format & Gw2VertexFvf.Weights) != 0) Add("Weights", Gw2VertexFvf.WeightsSize, Gw2VertexFvf.SemanticFor(Gw2VertexFvf.Weights));
        if ((format & Gw2VertexFvf.Group) != 0) Add("Group", Gw2VertexFvf.GroupSize, Gw2VertexFvf.SemanticFor(Gw2VertexFvf.Group));
        if ((format & Gw2VertexFvf.Normal) != 0) Add("Normal", Gw2VertexFvf.VectorSize, Gw2VertexFvf.SemanticFor(Gw2VertexFvf.Normal));
        if ((format & Gw2VertexFvf.Color) != 0) Add("Color", Gw2VertexFvf.ColorSize, Gw2VertexFvf.SemanticFor(Gw2VertexFvf.Color));
        if ((format & Gw2VertexFvf.Tangent) != 0) Add("Tangent", Gw2VertexFvf.VectorSize, Gw2VertexFvf.SemanticFor(Gw2VertexFvf.Tangent));
        if ((format & Gw2VertexFvf.Bitangent) != 0) Add("Bitangent", Gw2VertexFvf.VectorSize, Gw2VertexFvf.SemanticFor(Gw2VertexFvf.Bitangent));
        if ((format & Gw2VertexFvf.TangentFrame) != 0) Add("TangentFrame", Gw2VertexFvf.VectorSize, Gw2VertexFvf.SemanticFor(Gw2VertexFvf.TangentFrame));
        for (int i = 0; i < 8; i++) if ((format & (1u << (8 + i))) != 0) Add($"UV32_{i}", Gw2VertexFvf.TexCoord32Size, $"TEXCOORD{i}");
        for (int i = 0; i < 8; i++) if ((format & (1u << (16 + i))) != 0) Add($"UV16_{i}", Gw2VertexFvf.TexCoord16Size, $"TEXCOORD{i}");
        if ((format & Gw2VertexFvf.Opaque24) != 0) Add("Opaque24", Gw2VertexFvf.Opaque24Size);
        if ((format & Gw2VertexFvf.Opaque25) != 0) Add("Opaque25", Gw2VertexFvf.Opaque25Size);
        if ((format & Gw2VertexFvf.Opaque26) != 0) Add("Opaque26", Gw2VertexFvf.Opaque26Size);
        if ((format & Gw2VertexFvf.Opaque27) != 0) Add("Opaque27", Gw2VertexFvf.Opaque27Size);
        if ((format & Gw2VertexFvf.PositionCompressed) != 0) Add("PositionHalf", Gw2VertexFvf.PositionCompressedSize, Gw2VertexFvf.SemanticFor(Gw2VertexFvf.PositionCompressed));
        if ((format & Gw2VertexFvf.Opaque29) != 0) Add("Opaque29", Gw2VertexFvf.Opaque29Size);
        if ((ulong)count * (uint)stride != (ulong)bytes.Length || (count != 0 && stride == 0))
            throw new InvalidDataException("Vertex count, FVF stride and buffer size disagree");
        Stride = stride; Attributes = attributes.AsReadOnly();
    }
    public uint Format { get; }
    public uint Count { get; }
    public int Stride { get; }
    public ReadOnlyMemory<byte> Bytes { get; }
    public IReadOnlyList<Gw2VertexAttribute> Attributes { get; }
    public Vector3 ReadPosition(uint index)
    {
        if (index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
        var field = Attributes.FirstOrDefault(a => a.Name is "Position" or "PositionHalf")
            ?? throw new NotSupportedException("Vertex has no position attribute");
        var span = Bytes.Span.Slice(checked((int)index * Stride + field.Offset), field.Size);
        return field.Size == 12
            ? new(Gw2Binary.ReadSingleLittleEndian(span), Gw2Binary.ReadSingleLittleEndian(span[4..]), Gw2Binary.ReadSingleLittleEndian(span[8..]))
            : new(Gw2Binary.ReadHalfLittleEndian(span), Gw2Binary.ReadHalfLittleEndian(span[2..]), Gw2Binary.ReadHalfLittleEndian(span[4..]));
    }
    public static Gw2VertexBuffer FromGeometry(PackObject geometry)
    {
        var vertices = (PackObject)geometry.Fields["verts"]!;
        var mesh = (PackObject)vertices.Fields["mesh"]!;
        return new((uint)mesh.Fields["fvf"]!, (uint)vertices.Fields["vertexCount"]!,
            mesh.Fields["vertices"] is ReadOnlyMemory<byte> data ? data : ReadOnlyMemory<byte>.Empty);
    }
}
