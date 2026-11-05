using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2ParticleFieldMeshTests
{
    private const uint ShipwreckStrandEnvironment = 3719751;

    [TestMethod]
    public void MinimalStandardRandomMatchesLehmerSequence()
    {
        var random = new Gw2MinimalStandardRandom(1);

        Assert.AreEqual(48271u, random.NextUInt());
        Assert.AreEqual(182605794u, random.NextUInt());
        Assert.AreEqual((uint)(123459876UL * 48271UL % 0x7FFFFFFFUL), new Gw2MinimalStandardRandom(0).NextUInt());
    }

    [TestMethod]
    public void EveryFieldOnMap1595BuildsOneQuadPerParticle()
    {
        var environment = OpenReader().ReadMap(ShipwreckStrandEnvironment)!.Environment!;
        var fields = environment.ParticleFields.Concat(environment.Zones.SelectMany(zone => zone.Data?.ParticleFields ?? [])).ToList();

        Assert.IsTrue(fields.Count > 0);
        foreach (var field in fields)
        {
            var mesh = Gw2ParticleFieldMesh.Build(field);

            Assert.AreEqual((int)field.ParticleCount, mesh.ParticleCount, field.Name);
            Assert.IsTrue(mesh.Vertices.All(vertex => vertex.Position.X is >= 0f and <= 1f && vertex.Position.Z is >= 0f and <= 1f && vertex.Position.Y is >= 0f and < 1.5f), field.Name);
            CollectionAssert.AreEqual(Gw2ParticleFieldMesh.Build(field).Vertices, mesh.Vertices, field.Name);
        }
    }

    [TestMethod]
    public void BlendFlagsDefaultByTypeWhenNoneIsSet()
    {
        var field = ShipwreckStrandFields().First();

        Assert.AreEqual(Gw2ParticleFieldFrame.AdditiveFlag, Gw2ParticleFieldFrame.BlendFlags(field with { Type = 2, Flags = 0 }));
        Assert.AreEqual(Gw2ParticleFieldFrame.AdditiveFlag, Gw2ParticleFieldFrame.BlendFlags(field with { Type = 3, Flags = Gw2ParticleFieldFrame.NightFlag }));
        Assert.AreEqual(Gw2ParticleFieldFrame.AlphaBlendFlag, Gw2ParticleFieldFrame.BlendFlags(field with { Type = 0, Flags = 0 }));
        Assert.AreEqual(Gw2ParticleFieldFrame.SubtractiveFlag, Gw2ParticleFieldFrame.BlendFlags(field with { Type = 2, Flags = Gw2ParticleFieldFrame.SubtractiveFlag }));
    }

    [TestMethod]
    public void WeightFollowsDayNightEyeHeightAndWater()
    {
        var field = ShipwreckStrandFields().First(candidate => (candidate.Flags & (Gw2ParticleFieldFrame.SlabFlag | Gw2ParticleFieldFrame.UnderwaterFlag)) == 0 && (candidate.Flags & Gw2ParticleFieldFrame.NightFlag) != 0 && (candidate.Flags & Gw2ParticleFieldFrame.DayFlag) == 0);
        var highAbove = new Vector3(0f, 0f, -500f);

        Assert.AreEqual(1f, Gw2ParticleFieldFrame.Weight(field, highAbove, float.PositiveInfinity, 0f, 1f));
        Assert.AreEqual(0.75f, Gw2ParticleFieldFrame.Weight(field, highAbove, float.PositiveInfinity, 0.25f, 1f), 1e-6f);
        Assert.AreEqual(0.5f, Gw2ParticleFieldFrame.Weight(field, highAbove, float.PositiveInfinity, 0f, 0.5f));
        Assert.AreEqual(0.25f, Gw2ParticleFieldFrame.Weight(field, new Vector3(0f, 0f, -5f), float.PositiveInfinity, 0f, 1f), 1e-6f);
        Assert.AreEqual(0f, Gw2ParticleFieldFrame.Weight(field, highAbove, -600f, 0f, 1f));
    }

    [TestMethod]
    public void ConstantsPlaceTheFieldBoxAheadOfTheEye()
    {
        var field = ShipwreckStrandFields().First();
        var eye = new Vector3(100f, 200f, -300f);
        var constants = Gw2ParticleFieldFrame.Constants(field, eye, Vector3.UnitX, 0.5f, 2f);
        var extent = (float)field.Extent;

        Assert.AreEqual((eye.X - extent / 2f + extent / 2f + 65.053825f) / extent, constants.A.X, 1e-5f);
        Assert.AreEqual((eye.Y - extent / 2f) / extent, constants.A.Y, 1e-5f);
        Assert.AreEqual(2f, constants.A.W);
        Assert.AreEqual(new Vector4(extent, 0.5f, field.Deviation / extent, -field.Altitude), constants.B);
        Assert.AreEqual(new Vector4(field.Period.X, field.Period.Y, field.Lifetime.X, field.Lifetime.Y), constants.F);
    }

    [TestMethod]
    public void AtMostSixCutoutsAreSelected()
    {
        var environment = OpenReader().ReadMap(ShipwreckStrandEnvironment)!.Environment!;
        var cutouts = environment.ParticleFieldCutouts;
        var selected = Gw2ParticleFieldFrame.SelectCutouts(cutouts, Vector3.Zero, Vector3.UnitX);

        Assert.AreEqual(Math.Min(cutouts.Count, Gw2ParticleFieldFrame.MaximumCutouts), selected.Count);
        Assert.IsTrue(selected.All(cutouts.Contains));
    }

    private static Gw2MapEnvParticleField[] ShipwreckStrandFields()
    {
        var environment = OpenReader().ReadMap(ShipwreckStrandEnvironment)!.Environment!;
        return environment.ParticleFields.Concat(environment.Zones.SelectMany(zone => zone.Data?.ParticleFields ?? [])).ToArray();
    }


    private static Gw2ContentReader OpenReader()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        return Gw2ContentReader.Open(dat, verifyChecksums: false);
    }
}
