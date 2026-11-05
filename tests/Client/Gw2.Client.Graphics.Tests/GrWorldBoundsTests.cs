using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Crystal;

namespace Gw2.Client.Graphics.Tests;

[TestClass]
public sealed class GrWorldBoundsTests
{
    [TestMethod]
    public void SortDepthIsTheViewDepthOfTheBoundingSphereFront()
    {
        var view = Matrix4x4.CreateLookAt(Vector3.Zero, new Vector3(0, 0, -1), Vector3.UnitY);
        var box = new BoundingBox(new Vector3(-3, -4, -12), new Vector3(3, 4, -12));
        Assert.AreEqual(12f - 5f, GrWorldBounds.SortDepth(box, view), 1e-4f);
    }

    [TestMethod]
    public void SortDepthIgnoresSidewaysOffset()
    {
        var view = Matrix4x4.CreateLookAt(Vector3.Zero, new Vector3(0, 0, -1), Vector3.UnitY);
        var ahead = new BoundingBox(new Vector3(0, 0, -10), new Vector3(0, 0, -10));
        var aside = new BoundingBox(new Vector3(50, 0, -10), new Vector3(50, 0, -10));
        Assert.AreEqual(GrWorldBounds.SortDepth(ahead, view), GrWorldBounds.SortDepth(aside, view), 1e-4f);
    }
}
