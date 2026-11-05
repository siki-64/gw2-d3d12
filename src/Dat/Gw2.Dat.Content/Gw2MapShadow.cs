using Gw2.Formats;
using System.Buffers.Binary;
using System.Numerics;
using Gw2.Dat.Archive;

namespace Gw2.Dat.Content;

public enum Gw2MapShadowTileCompression : uint
{
    Constant = 0,
    Quantized = 1,
    QuantizedDelta = 2,
}

public sealed record Gw2MapShadowTile(Gw2MapShadowTileCompression Compression, byte[] Bytes);

public sealed record Gw2MapShadow(
    uint Width,
    uint Height,
    uint TileWidth,
    uint TileHeight,
    IReadOnlyList<Gw2MapShadowTile> Tiles,
    Matrix4x4 WorldToShadow,
    Vector2 S,
    Vector2 T,
    Vector2 U,
    Vector3 Eye,
    Vector3 Direction,
    Vector3 Up)
{
    public const int TileSize = 64;
    public const int TileTexels = TileSize * TileSize;
    private const int QuantizedHeaderSize = 8;
    private const int QuantizedPayloadSize = QuantizedHeaderSize + TileTexels * sizeof(ushort);
    private const float QuantizedScale = 65534f;
    private const ushort UnoccludedDelta = 0xFFFF;
    private const float UnoccludedDepth = 1f;

    public int TilesAcross => (int)((Width + TileWidth - 1) / TileWidth);
    public int TilesDown => (int)((Height + TileHeight - 1) / TileHeight);

    public static Gw2MapShadow? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.FindRoot("PackMapShadow") is not { } root) return null;
        var dims = PackValue.AsU32List(root.Fields.GetValueOrDefault("shadowDims"));
        var tileDims = PackValue.AsU32List(root.Fields.GetValueOrDefault("tileDims"));
        var tiles = PackValue.AsObjects(root.Fields.GetValueOrDefault("tiles"))
            .Select(tile => new Gw2MapShadowTile(
                (Gw2MapShadowTileCompression)PackValue.RequireU32(tile, "compressionMode"),
                PackValue.AsBytes(tile.Fields.GetValueOrDefault("bytes"))))
            .ToArray();
        var matrix = root.Fields.GetValueOrDefault("worldToShadow") is object?[] items ? items.Select(PackValue.AsF32).ToArray() : new float[16];
        return new(
            dims[0], dims[1], tileDims[0], tileDims[1], tiles,
            new Matrix4x4(
                matrix[0], matrix[1], matrix[2], matrix[3],
                matrix[4], matrix[5], matrix[6], matrix[7],
                matrix[8], matrix[9], matrix[10], matrix[11],
                matrix[12], matrix[13], matrix[14], matrix[15]),
            Vec2(root, "s"), Vec2(root, "t"), Vec2(root, "u"),
            Vec3(root, "shadowEye"), Vec3(root, "shadowDir"), Vec3(root, "shadowUp"));
    }

    public float[] TileDepths(int tileIndex)
    {
        var tile = Tiles[tileIndex];
        var depths = new float[TileTexels];
        if (tile.Compression == Gw2MapShadowTileCompression.Constant)
        {
            Array.Fill(depths, BinaryPrimitives.ReadSingleLittleEndian(tile.Bytes));
            return depths;
        }

        using var payload = new MemoryStream(QuantizedPayloadSize);
        ArchiveEntry.DecodeCompressed(tile.Bytes, QuantizedPayloadSize, [], payload);
        var bytes = payload.GetBuffer().AsSpan(0, QuantizedPayloadSize);
        var minimum = BinaryPrimitives.ReadSingleLittleEndian(bytes);
        var range = BinaryPrimitives.ReadSingleLittleEndian(bytes[4..]) - minimum;
        ushort running = 0;
        for (var texel = 0; texel < TileTexels; texel++)
        {
            var quantized = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(QuantizedHeaderSize + texel * sizeof(ushort))..]);
            if (tile.Compression == Gw2MapShadowTileCompression.Quantized)
            {
                depths[texel] = quantized / QuantizedScale * range + minimum;
                continue;
            }

            running = unchecked((ushort)(running + quantized));
            depths[texel] = running == UnoccludedDelta ? UnoccludedDepth : running / QuantizedScale * range + minimum;
        }

        return depths;
    }

    public Vector2[] TileMoments(int tileIndex, bool filtered)
    {
        var depths = TileDepths(tileIndex);
        var moments = new Vector2[TileTexels];
        for (var row = 0; row < TileSize; row++)
        for (var column = 0; column < TileSize; column++)
        {
            if (!filtered || Tiles[tileIndex].Compression == Gw2MapShadowTileCompression.Constant)
            {
                var depth = depths[row * TileSize + column];
                moments[row * TileSize + column] = new Vector2(depth, depth * depth);
                continue;
            }

            var mean = 0f;
            var meanOfSquares = 0f;
            for (var rowOffset = -1; rowOffset <= 1; rowOffset++)
            for (var columnOffset = -1; columnOffset <= 1; columnOffset++)
            {
                var depth = depths[Math.Clamp(row + rowOffset, 0, TileSize - 1) * TileSize + Math.Clamp(column + columnOffset, 0, TileSize - 1)];
                var weight = FilterWeight(rowOffset) * FilterWeight(columnOffset);
                mean += depth * weight;
                meanOfSquares += depth * depth * weight;
            }

            moments[row * TileSize + column] = new Vector2(mean * FilterNormalization, meanOfSquares * FilterNormalization);
        }

        return moments;
    }

    private const float FilterNormalization = 0.25f;

    private static float FilterWeight(int offset) => offset == 0 ? 1f : 0.5f;

    private static Vector2 Vec2(PackObject owner, string name) =>
        PackValue.AsFloats(owner.Fields.GetValueOrDefault(name)) is { Length: >= 2 } values ? new Vector2(values[0], values[1]) : Vector2.Zero;

    private static Vector3 Vec3(PackObject owner, string name) =>
        PackValue.AsFloats(owner.Fields.GetValueOrDefault(name)) is { Length: >= 3 } values ? new Vector3(values[0], values[1], values[2]) : Vector3.Zero;
}
