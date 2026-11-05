using Gw2.Client.Scene;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Scene.Tests;

[TestClass]
public sealed class CameraProjectionTests
{
    [TestMethod]
    public void NearPlaneScalesWithViewDistanceAndInverselyWithFieldOfView()
    {
        Assert.AreEqual(14.648f, Gw2CameraProjection.NearPlaneOf(24576f, Gw2CameraProjection.ReferenceFieldOfView), 0.001f);
        Assert.AreEqual(21.972f, Gw2CameraProjection.NearPlaneOf(36864f, Gw2CameraProjection.ReferenceFieldOfView), 0.001f);
        Assert.AreEqual(29.297f / 2f, Gw2CameraProjection.NearPlaneOf(36864f, Gw2CameraProjection.ReferenceFieldOfView * 1.5f), 0.001f);
    }

    [TestMethod]
    public void NearPlaneIsClampedToTwentyFive()
    {
        Assert.AreEqual(25f, Gw2CameraProjection.NearPlaneOf(61440f, Gw2CameraProjection.ReferenceFieldOfView));
        Assert.AreEqual(0f, Gw2CameraProjection.NearPlaneOf(-1f, Gw2CameraProjection.ReferenceFieldOfView));
    }
}
