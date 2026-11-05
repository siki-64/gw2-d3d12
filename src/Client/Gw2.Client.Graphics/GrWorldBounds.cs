using System.Collections.Generic;
using System.Numerics;
using Crystal;

namespace Gw2.Client.Graphics;

public static class GrWorldBounds
{
    public static BoundingBox Of(Vector3 localMinimum, Vector3 localMaximum, Matrix4x4 world)
    {
        var minimum = new Vector3(float.MaxValue);
        var maximum = new Vector3(float.MinValue);
        for (var corner = 0; corner < 8; corner++)
        {
            var local = new Vector3(
                (corner & 1) == 0 ? localMinimum.X : localMaximum.X,
                (corner & 2) == 0 ? localMinimum.Y : localMaximum.Y,
                (corner & 4) == 0 ? localMinimum.Z : localMaximum.Z);
            var transformed = Vector3.Transform(local, world);
            minimum = Vector3.Min(minimum, transformed);
            maximum = Vector3.Max(maximum, transformed);
        }
        return new BoundingBox(new Vector3(minimum.X, minimum.Y, minimum.Z), new Vector3(maximum.X, maximum.Y, maximum.Z));
    }

    public static BoundingBox Union(IEnumerable<BoundingBox> boxes)
    {
        var minimum = new Vector3(float.MaxValue);
        var maximum = new Vector3(float.MinValue);
        foreach (var box in boxes)
        {
            minimum = Vector3.Min(minimum, box.Min);
            maximum = Vector3.Max(maximum, box.Max);
        }
        return new BoundingBox(minimum, maximum);
    }

    public static float SortDepth(BoundingBox box, Matrix4x4 view)
    {
        var center = (box.Min + box.Max) * 0.5f;
        var radius = (box.Max - box.Min).Length() * 0.5f;
        var viewDepth = -(center.X * view.M13 + center.Y * view.M23 + center.Z * view.M33 + view.M43);
        return viewDepth - radius;
    }
}
