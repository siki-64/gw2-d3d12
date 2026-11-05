using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2SkyCardLensFlareTests
{
    private const uint SkyCardMapFileId = 1839188;
    private static readonly Vector3 Right = new(0f, -1f, 0f);
    private static readonly Vector3 Up = new(0f, 0f, 1f);
    private static readonly Vector3 Forward = new(1f, 0f, 0f);

    private static Gw2MapEnvLensFlare Flare(params Gw2MapEnvLensFlareAtom[] atoms) =>
        new(atoms, [], default, new Vector2(0.1f, 0.3f), Vector2.Zero, 1f, 0, []);

    private static Gw2MapEnvLensFlareAtom Atom(Vector2 offset, Vector2 scale, uint rows = 1, uint columns = 1, uint start = 0, float fps = 0f) =>
        new(rows, columns, start, fps, [0x10, 0x20, 0x30, 0x80], offset, scale, 0f, 0f, 0);

    [TestMethod]
    public void AtomOnTheFlareLineSitsWhereItsOffsetPlacesIt()
    {
        var flare = Flare(Atom(new Vector2(0f, 0f), new Vector2(2f, 3f)), Atom(new Vector2(1f, 0f), Vector2.One));

        var vertices = Gw2SkyCardLensFlare.Build(flare, new Vector3(25000f, 0f, 0f), 1f, 0f, Vector3.Zero, Right, Up, Forward);

        var firstCenter = (vertices[0].Position + vertices[3].Position) / 2f;
        Assert.AreEqual(40f, firstCenter.X, 1e-3f);
        Assert.AreEqual(4f, Vector3.Distance(vertices[0].Position, vertices[2].Position), 1e-3f);
        Assert.AreEqual(6f, Vector3.Distance(vertices[0].Position, vertices[1].Position), 1e-3f);
        Assert.AreEqual(new Vector4(0x30, 0x20, 0x10, 0x80) / 255f, vertices[0].Color);
    }

    [TestMethod]
    public void FlareBehindTheCameraOrHiddenProducesNoGeometry()
    {
        var flare = Flare(Atom(Vector2.Zero, Vector2.One));

        var behind = Gw2SkyCardLensFlare.Build(flare, new Vector3(-25000f, 0f, 0f), 1f, 0f, Vector3.Zero, Right, Up, Forward);
        var hidden = Gw2SkyCardLensFlare.Build(flare, new Vector3(25000f, 0f, 0f), 0f, 0f, Vector3.Zero, Right, Up, Forward);

        Assert.IsTrue(behind.Concat(hidden).All(vertex => vertex == default));
    }

    [TestMethod]
    public void FrameAdvancesTheAtlasCellAndBlendsIntoTheNext()
    {
        var flare = Flare(Atom(Vector2.Zero, Vector2.One, rows: 2, columns: 2, start: 1, fps: 2f));

        var vertices = Gw2SkyCardLensFlare.Build(flare, new Vector3(25000f, 0f, 0f), 1f, 0.75f, Vector3.Zero, Right, Up, Forward);

        Assert.AreEqual(new Vector2(0f, 0.5f), vertices[0].Uv);
        Assert.AreEqual(new Vector2(0.5f, 0.5f), vertices[0].NextUv);
        Assert.AreEqual(0.5f, vertices[0].Blend, 1e-6f);
    }

    [TestMethod]
    public void VisibilityScalesOcclusionByDistanceFromTwilight()
    {
        Assert.AreEqual(0.5f * 0.6f, Gw2SkyCardLensFlare.Visibility(50, 100, 0.8f, 1f), 1e-6f);
        Assert.AreEqual(0f, Gw2SkyCardLensFlare.Visibility(0, 100, 1f, 1f));
        Assert.AreEqual(0f, Gw2SkyCardLensFlare.Visibility(100, 0, 1f, 1f));
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 2, 1, 3, 4, 5, 6, 6, 5, 7 }, Gw2SkyCardLensFlare.Indices(2));
    }

    [TestMethod]
    public void DayFlareOfTheMapCarriesItsAtomsAndFadeBand()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var flares = reader.ReadMap(SkyCardMapFileId)!.Environment!.SkyCards
            .Select(card => card.Day?.LensFlare)
            .OfType<Gw2MapEnvLensFlare>()
            .Where(flare => flare.OpacityCoeff > 0f)
            .ToArray();
        Assert.IsTrue(flares.Length > 0);
        var flare = flares[0];
        Assert.AreEqual(4, flare.Atoms.Count);
        Assert.AreEqual(0.1304f, flare.FadeBand.X, 1e-3f);

        var vertices = Gw2SkyCardLensFlare.Build(flare, new Vector3(25000f, 0f, 0f), 1f, 0f, Vector3.Zero, Right, Up, Forward);
        Assert.AreEqual(16, vertices.Length);
        Assert.IsTrue(vertices.All(vertex => float.IsFinite(vertex.Position.X) && vertex.Color.W > 0f));
    }
}
