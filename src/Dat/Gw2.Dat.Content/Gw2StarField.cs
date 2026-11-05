using Gw2.Formats;
using System.Buffers.Binary;
using Gw2.Dat.Archive;

namespace Gw2.Dat.Content;

public sealed record Gw2Star(float AngleZ, float AngleY, float U0, float U1, float V0, float V1);

public sealed record Gw2StarField(float Size, IReadOnlyList<Gw2Star> Stars, PackFileReference Texture)
{
    private const int StarSize = 24;

    public static Gw2StarField? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Pack.PointerSize != 4) return null;
        var chunk = document.Chunks.FirstOrDefault(parsed => parsed.Chunk.Type == "STAR")?.Chunk;
        if (chunk is null) return null;
        var bytes = chunk.Bytes.Span;
        var root = chunk.HeaderSize;
        var count = BinaryPrimitives.ReadInt32LittleEndian(bytes[(root + 4)..]);
        var starsOffset = Target(bytes, root + 8);
        var stars = new Gw2Star[count];
        for (var index = 0; index < count; index++)
        {
            var star = bytes.Slice(starsOffset + index * StarSize, StarSize);
            stars[index] = new(F32(star, 0), F32(star, 4), F32(star, 8), F32(star, 12), F32(star, 16), F32(star, 20));
        }
        var textureOffset = Target(bytes, root + 12);
        var texture = textureOffset < 0 ? default : PackFileReference.Parse(bytes[textureOffset..]);
        return new(F32(bytes, root), stars, texture);
    }

    private static float F32(ReadOnlySpan<byte> bytes, int offset) => Gw2Binary.ReadSingleLittleEndian(bytes[offset..]);

    private static int Target(ReadOnlySpan<byte> bytes, int pointerOffset)
    {
        var relative = BinaryPrimitives.ReadInt32LittleEndian(bytes[pointerOffset..]);
        return relative == 0 ? -1 : pointerOffset + relative;
    }
}
