using System.Numerics;

namespace Gw2.Dat.Content;

public readonly record struct Gw2MapTerrainBox(int X, int Y, int Size);

public static class Gw2MapTerrainLod
{
    public const int SmallestErrorBox = 2;
    public const float SiblingErrorTolerance = 6f;
    public const float SplitError = 40f;
    private const float DistanceScale = 0.0002f;
    private const float DistanceQuarticScale = 1e-13f;
    private const float BoxSizePerDistance = 0.6666667f;
    private const float ErrorScale = 4f;

    public static int BoxCount(int smallestSize, int tilesPerChunk)
    {
        var count = 0;
        for (var size = tilesPerChunk; size >= smallestSize; size >>= 1)
            count += (tilesPerChunk / size) * (tilesPerChunk / size);
        return count;
    }

    public static Gw2MapTerrainBox Box(int index, int tilesPerChunk)
    {
        var x = 0;
        var y = 0;
        var size = tilesPerChunk;
        var path = new Stack<int>();
        for (var current = index; current != 0; current = (current - 1) >> 2)
            path.Push((current - 1) & 3);
        while (path.Count > 0)
        {
            var child = path.Pop();
            size >>= 1;
            x += (child & 1) * size;
            y += (child >> 1) * size;
        }

        return new Gw2MapTerrainBox(x, y, size);
    }

    public static byte[] BoxErrors(Func<int, int, float> height, int tilesPerChunk, float tile)
    {
        ArgumentNullException.ThrowIfNull(height);
        var errors = new byte[BoxCount(SmallestErrorBox, tilesPerChunk)];
        for (var index = 0; index < errors.Length; index++)
            errors[index] = BoxError(height, Box(index, tilesPerChunk), tile);
        return errors;
    }

    private static byte BoxError(Func<int, int, float> height, Gw2MapTerrainBox box, float tile)
    {
        var extent = box.Size * tile;
        var topLeft = new Vector3(0f, 0f, height(box.X, box.Y));
        var topRight = new Vector3(extent, 0f, height(box.X + box.Size, box.Y));
        var bottomLeft = new Vector3(0f, -extent, height(box.X, box.Y + box.Size));
        var bottomRight = new Vector3(extent, -extent, height(box.X + box.Size, box.Y + box.Size));
        var upperPlane = PlaneThrough(topLeft, topRight, bottomLeft);
        var lowerPlane = PlaneThrough(topRight, bottomRight, bottomLeft);
        var squaredSum = 0f;
        for (var row = 0; row <= box.Size; row++)
        for (var column = 0; column <= box.Size; column++)
        {
            var plane = column + row > box.Size ? lowerPlane : upperPlane;
            var point = new Vector3(column * tile, -row * tile, height(box.X + column, box.Y + row));
            var distance = Vector3.Dot(plane.Normal, point) + plane.D;
            squaredSum += distance * distance;
        }

        var samples = (box.Size + 1) * (box.Size + 1);
        var error = MathF.Round(MathF.Sqrt(squaredSum / samples) * ErrorScale);
        return (byte)Math.Min(error, byte.MaxValue);
    }

    private static Plane PlaneThrough(Vector3 first, Vector3 second, Vector3 third)
    {
        var normal = Vector3.Cross(second - first, third - first);
        if (normal.LengthSquared() < 1e-12f) return default;
        normal = Vector3.Normalize(normal);
        return new Plane(normal, -Vector3.Dot(normal, first));
    }

    public static float CornerDistance(Vector3 eye, Vector3 corner)
    {
        var squared = Vector3.DistanceSquared(eye, corner);
        return (MathF.Sqrt(squared) + squared * squared * DistanceQuarticScale) * DistanceScale;
    }

    public static List<Gw2MapTerrainBox> SelectLeaves(byte[] errors, int tilesPerChunk, Vector3 eye, Vector3 topLeft, Vector3 topRight, Vector3 bottomLeft, Vector3 bottomRight)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var topLeftDistance = CornerDistance(eye, topLeft);
        var topRightDistance = CornerDistance(eye, topRight);
        var bottomLeftDistance = CornerDistance(eye, bottomLeft);
        var bottomRightDistance = CornerDistance(eye, bottomRight);
        var leaves = new List<Gw2MapTerrainBox>();
        var pending = new Stack<int>();
        pending.Push(0);
        while (pending.Count > 0)
        {
            var index = pending.Pop();
            var box = Box(index, tilesPerChunk);
            var u = (float)box.X / tilesPerChunk;
            var v = (float)box.Y / tilesPerChunk;
            var top = topLeftDistance + (topRightDistance - topLeftDistance) * u;
            var bottom = bottomLeftDistance + (bottomRightDistance - bottomLeftDistance) * u;
            var wanted = (int)MathF.Round((top + (bottom - top) * v) * BoxSizePerDistance);
            var target = 1 << BitOperations.Log2((uint)(2 * wanted + 1));
            if (ShouldSplit(errors, index, box.Size, target))
            {
                if (box.Size > SmallestErrorBox)
                {
                    for (var child = 4; child >= 1; child--)
                        pending.Push(4 * index + child);
                }
                else
                {
                    var half = box.Size / 2;
                    for (var child = 0; child < 4; child++)
                        leaves.Add(new Gw2MapTerrainBox(box.X + (child & 1) * half, box.Y + (child >> 1) * half, half));
                }
            }
            else
            {
                leaves.Add(box);
            }
        }

        return leaves;
    }

    private static bool ShouldSplit(byte[] errors, int index, int size, int target)
    {
        var effectiveSize = size;
        var effectiveIndex = index;
        if (target < size)
        {
            effectiveSize = target;
            for (var descend = size; descend != target; descend >>= 1)
                effectiveIndex = 4 * effectiveIndex + 1;
            while (effectiveSize < size)
            {
                var parentError = errors[(effectiveIndex - 1) >> 2];
                var siblingsFlat = true;
                for (var sibling = effectiveIndex; sibling < effectiveIndex + 4; sibling++)
                {
                    var siblingError = effectiveSize <= 1 ? 0 : errors[sibling];
                    if (parentError - siblingError > SiblingErrorTolerance)
                    {
                        siblingsFlat = false;
                        break;
                    }
                }

                if (!siblingsFlat) break;
                effectiveSize *= 2;
                effectiveIndex = (effectiveIndex - 1) >> 2;
            }
        }

        return effectiveSize < size || errors[index] > SplitError;
    }
}
