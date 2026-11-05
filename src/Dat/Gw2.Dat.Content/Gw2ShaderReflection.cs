using System.Buffers.Binary;
using System.Text;

namespace Gw2.Dat.Content;

public enum Gw2ShaderReflectionKind
{
    Constant,
    Sampler,
    Other,
}

public sealed record Gw2ShaderReflectionEntry(string Name, int Type, int Register, int Count, Gw2ShaderReflectionKind Kind);

public sealed record Gw2ShaderReflection(string Stage, IReadOnlyList<Gw2ShaderReflectionEntry> Entries)
{
    private const int HeaderSize = 14;
    private const int EntryTail = 10;
    private const int SamplerType = 0x30;

    public IEnumerable<Gw2ShaderReflectionEntry> Samplers =>
        Entries.Where(entry => entry.Kind == Gw2ShaderReflectionKind.Sampler);

    public bool TryGetSampler(int textureSlot, out Gw2ShaderReflectionEntry entry)
    {
        foreach (var candidate in Samplers)
            if (candidate.Register == textureSlot)
            {
                entry = candidate;
                return true;
            }

        entry = null!;
        return false;
    }

    public static Gw2ShaderReflectionKind KindOf(int type) => type == SamplerType
        ? Gw2ShaderReflectionKind.Sampler
        : type is 0x02 or 0x04 or 0x12 or 0x14 or 0x24
            ? Gw2ShaderReflectionKind.Constant
            : Gw2ShaderReflectionKind.Other;

    public static Gw2ShaderReflection? TryParse(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        if (span.Length < HeaderSize + 4) return null;

        var stage = Encoding.ASCII.GetString(span[..3]);
        if (stage is not ("VSH" or "FSH")) return null;

        int count = BinaryPrimitives.ReadUInt16LittleEndian(span[12..]);
        if (count <= 0 || count > 4096) return null;

        var entries = new List<Gw2ShaderReflectionEntry>(count);
        var cursor = HeaderSize;
        for (var i = 0; i < count; i++)
        {
            if (cursor >= span.Length) return null;
            int length = span[cursor++];
            if (length is < 1 or > 255 || cursor + length + EntryTail > span.Length) return null;

            var nameSpan = span.Slice(cursor, length);
            foreach (var c in nameSpan)
                if (c < 32 || c >= 127) return null;
            var name = Encoding.ASCII.GetString(nameSpan);
            cursor += length;

            int type = span[cursor];
            int register = BinaryPrimitives.ReadUInt16LittleEndian(span[(cursor + 2)..]);
            int elementCount = BinaryPrimitives.ReadUInt16LittleEndian(span[(cursor + 4)..]);
            cursor += EntryTail;

            entries.Add(new(name, type, register, elementCount, KindOf(type)));
        }

        return new(stage, entries);
    }
}
