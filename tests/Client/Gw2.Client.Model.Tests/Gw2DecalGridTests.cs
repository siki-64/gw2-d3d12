using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Gw2.Client.Model;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2DecalGridTests
{
    [TestMethod]
    public void DrapesTheProjectionOverTheGroundWithinItsDepth()
    {
        var corners = new List<Gw2ParticleCorner>();
        var placement = Matrix4x4.CreateRotationZ(MathF.PI / 2f) * Matrix4x4.CreateTranslation(100f, 200f, 50f);
        Gw2DecalGrid.Build(placement, new Vector2(240f, 75f), 260f, (near, far) => SurfaceWithin(near.X > 100f ? 60f : 500f, near, far), new Vector3(100f, 200f, 100000f), Vector4.One, corners);

        Assert.AreEqual(Gw2DecalGrid.CellsPerSide * Gw2DecalGrid.CellsPerSide * Gw2ParticleQuads.CornersPerQuad, corners.Count);
        var minimum = corners.Aggregate(new Vector3(float.MaxValue), (current, corner) => Vector3.Min(current, corner.Position));
        var maximum = corners.Aggregate(new Vector3(float.MinValue), (current, corner) => Vector3.Max(current, corner.Position));
        Assert.AreEqual(75f, maximum.X - minimum.X, 0.01f);
        Assert.AreEqual(240f, maximum.Y - minimum.Y, 0.01f);
        Assert.AreEqual(52f, minimum.Z, 0.01f);
        Assert.AreEqual(62f, maximum.Z, 0.01f);
        Assert.AreEqual(Vector2.Zero, corners[0].TexCoord);
    }

    private static Vector3? SurfaceWithin(float surface, Vector3 near, Vector3 far) => surface >= near.Z && surface <= far.Z ? near with { Z = surface } : null;
}
