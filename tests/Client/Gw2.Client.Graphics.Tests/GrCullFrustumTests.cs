using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Crystal;

namespace Gw2.Client.Graphics.Tests;

[TestClass]
public sealed class GrCullFrustumTests
{
    private static readonly Matrix4x4 LookDownNegativeZ =
        Matrix4x4.CreateLookAt(Vector3.Zero, new Vector3(0, 0, -1), Vector3.UnitY);

    [TestMethod]
    public void CameraFrustumKeepsBoxesInFrontAndDropsBoxesBehindOrBeside()
    {
        var frustum = GrCullFrustum.Camera(LookDownNegativeZ * Matrix4x4.CreatePerspectiveFieldOfView(1f, 1f, 1f, 100f));
        Assert.IsTrue(frustum.Intersects(new BoundingBox(new Vector3(-1, -1, -11), new Vector3(1, 1, -9))));
        Assert.IsTrue(frustum.Intersects(new BoundingBox(new Vector3(-50, -1, -11), new Vector3(0, 1, -9))));
        Assert.IsFalse(frustum.Intersects(new BoundingBox(new Vector3(-1, -1, 9), new Vector3(1, 1, 11))));
        Assert.IsFalse(frustum.Intersects(new BoundingBox(new Vector3(40, -1, -11), new Vector3(50, 1, -9))));
        Assert.IsFalse(frustum.Intersects(new BoundingBox(new Vector3(-1, -1, -300), new Vector3(1, 1, -200))));
    }

    [TestMethod]
    public void ShadowCasterFrustumKeepsCastersBetweenTheLightAndTheCascade()
    {
        var lightViewProjection = LookDownNegativeZ * Matrix4x4.CreateOrthographic(20f, 20f, 1f, 100f);
        var behindLight = new BoundingBox(new Vector3(-1, -1, 9), new Vector3(1, 1, 11));
        Assert.IsFalse(GrCullFrustum.Camera(lightViewProjection).Intersects(behindLight));
        Assert.IsTrue(GrCullFrustum.ShadowCasters(lightViewProjection).Intersects(behindLight));
        Assert.IsFalse(GrCullFrustum.ShadowCasters(lightViewProjection).Intersects(new BoundingBox(new Vector3(30, -1, -11), new Vector3(40, 1, -9))));
    }
}
