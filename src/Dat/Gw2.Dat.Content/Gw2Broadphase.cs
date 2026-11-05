using System.Buffers.Binary;

namespace Gw2.Dat.Content;

public readonly record struct Gw2BroadphaseEdge(uint BroadId, bool IsMaxEdge, float Coordinate)
{
    public bool IsSentinel => BroadId == Gw2Broadphase.SentinelBroadId;
}

public readonly record struct Gw2BroadphaseBox(uint BroadId, float MinX, float MinY, float MaxX, float MaxY);

public sealed class Gw2Broadphase
{
    public const uint SentinelBroadId = 0x7FFFFFFF;
    private const uint MaxEdgeFlag = 0x80000000;
    private const int EdgeSize = 8;

    private Gw2Broadphase(IReadOnlyList<Gw2BroadphaseEdge> xEdges, IReadOnlyList<Gw2BroadphaseEdge> yEdges, IReadOnlyList<Gw2BroadphaseBox> boxes)
    {
        XEdges = xEdges;
        YEdges = yEdges;
        Boxes = boxes;
    }

    public IReadOnlyList<Gw2BroadphaseEdge> XEdges { get; }
    public IReadOnlyList<Gw2BroadphaseEdge> YEdges { get; }
    public IReadOnlyList<Gw2BroadphaseBox> Boxes { get; }

    public static int SerializedSize(uint boxCount) => 4 + 2 * (int)(boxCount * 2 + 2) * EdgeSize;

    public static Gw2Broadphase? TryParse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4) return null;
        var boxCount = BinaryPrimitives.ReadUInt32LittleEndian(data);
        if (boxCount > (data.Length - 4) / (4 * EdgeSize) || SerializedSize(boxCount) != data.Length) return null;

        var edgesPerAxis = (int)boxCount * 2 + 2;
        var xEdges = ReadAxis(data[4..], edgesPerAxis);
        var yEdges = ReadAxis(data[(4 + edgesPerAxis * EdgeSize)..], edgesPerAxis);
        return new Gw2Broadphase(xEdges, yEdges, CollectBoxes(xEdges, yEdges));
    }

    private static Gw2BroadphaseEdge[] ReadAxis(ReadOnlySpan<byte> data, int edgeCount)
    {
        var edges = new Gw2BroadphaseEdge[edgeCount];
        for (var index = 0; index < edgeCount; index++)
        {
            var record = data.Slice(index * EdgeSize, EdgeSize);
            var packedId = BinaryPrimitives.ReadUInt32LittleEndian(record);
            edges[index] = new Gw2BroadphaseEdge(packedId & ~MaxEdgeFlag, (packedId & MaxEdgeFlag) != 0, Gw2Binary.ReadSingleLittleEndian(record[4..]));
        }

        return edges;
    }

    private static IReadOnlyList<Gw2BroadphaseBox> CollectBoxes(IReadOnlyList<Gw2BroadphaseEdge> xEdges, IReadOnlyList<Gw2BroadphaseEdge> yEdges)
    {
        var extents = new SortedDictionary<uint, float[]>();
        CollectAxis(xEdges, extents, 0);
        CollectAxis(yEdges, extents, 1);
        return extents.Select(entry => new Gw2BroadphaseBox(entry.Key, entry.Value[0], entry.Value[1], entry.Value[2], entry.Value[3])).ToArray();
    }

    private static void CollectAxis(IReadOnlyList<Gw2BroadphaseEdge> edges, SortedDictionary<uint, float[]> extents, int axis)
    {
        foreach (var edge in edges)
        {
            if (edge.IsSentinel) continue;
            if (!extents.TryGetValue(edge.BroadId, out var extent)) extents[edge.BroadId] = extent = new float[4];
            extent[axis + (edge.IsMaxEdge ? 2 : 0)] = edge.Coordinate;
        }
    }
}
