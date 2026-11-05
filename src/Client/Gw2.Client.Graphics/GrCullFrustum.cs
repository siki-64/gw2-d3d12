using System.Numerics;
using Crystal;

namespace Gw2.Client.Graphics;

public sealed class GrCullFrustum
{
    private readonly Plane[] _outwardPlanes;

    private GrCullFrustum(Plane[] outwardPlanes) => _outwardPlanes = outwardPlanes;

    public static GrCullFrustum Camera(Matrix4x4 viewProjection)
    {
        var frustum = new BoundingFrustum(viewProjection);
        return new GrCullFrustum([frustum.Near, frustum.Far, frustum.Left, frustum.Right, frustum.Top, frustum.Bottom]);
    }

    public static GrCullFrustum ShadowCasters(Matrix4x4 lightViewProjection)
    {
        var frustum = new BoundingFrustum(lightViewProjection);
        return new GrCullFrustum([frustum.Far, frustum.Left, frustum.Right, frustum.Top, frustum.Bottom]);
    }

    public System.ReadOnlySpan<Plane> OutwardPlanes => _outwardPlanes;

    public bool Intersects(BoundingBox box)
    {
        foreach (var plane in _outwardPlanes)
        {
            var nearestCorner = new Vector3(
                plane.Normal.X >= 0f ? box.Min.X : box.Max.X,
                plane.Normal.Y >= 0f ? box.Min.Y : box.Max.Y,
                plane.Normal.Z >= 0f ? box.Min.Z : box.Max.Z);
            if (Vector3.Dot(plane.Normal, nearestCorner) + plane.D > 0f) return false;
        }
        return true;
    }

}
