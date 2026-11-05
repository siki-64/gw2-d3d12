using System.Buffers.Binary;
using System.Text;

namespace Gw2.Dat.Content;

public enum Gw2BgfxShaderStage
{
    Vertex,
    Fragment,
}

public sealed record Gw2BgfxShaderBinary(
    Gw2BgfxShaderStage Stage,
    int FormatVersion,
    uint InputHash,
    uint OutputHash,
    IReadOnlyList<Gw2ShaderReflectionEntry> Uniforms,
    ReadOnlyMemory<byte> Code,
    IReadOnlyList<ushort> VertexAttributes,
    int ConstantBufferSize)
{
    private const int FirstVersionWithOutputHash = 6;
    private const int FirstVersionWithTextureInfo = 8;
    private const int FirstVersionWithTextureFormat = 10;

    public Gw2Dxbc? Container => Gw2Dxbc.TryParse(Code);

    public uint CodeHash()
    {
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;
        var hash = offsetBasis;
        foreach (var value in Code.Span) hash = unchecked((hash ^ value) * prime);
        return hash;
    }

    public byte[]? ChecksummedCode()
    {
        if (Container is not { } container || container.Chunks.Count == 0) return null;
        return Gw2Dxbc.Rebuild(container.Chunks.OrderBy(chunk => chunk.Offset).Select(chunk => (chunk.FourCc, chunk.Data)).ToList());
    }

    public static Gw2BgfxShaderBinary? TryParse(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        if (span.Length < 8) return null;

        Gw2BgfxShaderStage stage;
        if (span[..3].SequenceEqual("VSH"u8)) stage = Gw2BgfxShaderStage.Vertex;
        else if (span[..3].SequenceEqual("FSH"u8)) stage = Gw2BgfxShaderStage.Fragment;
        else return null;

        int version = span[3];
        var cursor = 4;
        var inputHash = BinaryPrimitives.ReadUInt32LittleEndian(span[cursor..]);
        cursor += 4;
        uint outputHash = 0;
        if (version >= FirstVersionWithOutputHash)
        {
            if (!Fits(span, cursor, 4)) return null;
            outputHash = BinaryPrimitives.ReadUInt32LittleEndian(span[cursor..]);
            cursor += 4;
        }

        if (!Fits(span, cursor, 2)) return null;
        int uniformCount = BinaryPrimitives.ReadUInt16LittleEndian(span[cursor..]);
        cursor += 2;

        var uniformTail = 6
                          + (version >= FirstVersionWithTextureInfo ? 2 : 0)
                          + (version >= FirstVersionWithTextureFormat ? 2 : 0);
        var uniforms = new List<Gw2ShaderReflectionEntry>(uniformCount);
        for (var index = 0; index < uniformCount; index++)
        {
            if (!Fits(span, cursor, 1)) return null;
            int nameLength = span[cursor++];
            if (!Fits(span, cursor, nameLength + uniformTail)) return null;
            var name = Encoding.ASCII.GetString(span.Slice(cursor, nameLength));
            cursor += nameLength;
            int type = span[cursor];
            int register = BinaryPrimitives.ReadUInt16LittleEndian(span[(cursor + 2)..]);
            int registerCount = BinaryPrimitives.ReadUInt16LittleEndian(span[(cursor + 4)..]);
            cursor += uniformTail;
            uniforms.Add(new(name, type, register, registerCount, Gw2ShaderReflection.KindOf(type)));
        }

        if (!Fits(span, cursor, 4)) return null;
        var codeSize = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(span[cursor..]));
        cursor += 4;
        if (!Fits(span, cursor, codeSize + 1)) return null;
        var code = data.Slice(cursor, codeSize);
        cursor += codeSize + 1;

        if (!Fits(span, cursor, 1)) return null;
        int attributeCount = span[cursor++];
        if (!Fits(span, cursor, attributeCount * 2 + 2)) return null;
        var attributes = new ushort[attributeCount];
        for (var index = 0; index < attributeCount; index++)
        {
            attributes[index] = BinaryPrimitives.ReadUInt16LittleEndian(span[cursor..]);
            cursor += 2;
        }

        int constantBufferSize = BinaryPrimitives.ReadUInt16LittleEndian(span[cursor..]);
        cursor += 2;
        if (cursor != span.Length) return null;

        return new(stage, version, inputHash, outputHash, uniforms, code, attributes, constantBufferSize);
    }

    private static bool Fits(ReadOnlySpan<byte> span, int cursor, int length) => length >= 0 && cursor + length <= span.Length;
}
