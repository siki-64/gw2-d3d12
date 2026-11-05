using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2StarFieldTests
{
    private const uint StarFieldFileId = 186291;
    private const uint StarMapFileId = 187611;
    private const uint MapStarFieldFileId = 187544;

    [TestMethod]
    public void ReadsStarFieldWhenConfigured()
    {
        using var reader = OpenReader();
        var stars = Gw2StarField.TryParse(reader.ReadDocument(StarFieldFileId))!;

        Assert.AreEqual(0.25f, stars.Size);
        Assert.AreEqual(1908, stars.Stars.Count);
        Assert.IsTrue(stars.Stars.All(star => star.U0 <= star.U1 && star.V0 <= star.V1 && star.U1 <= 1 && star.V1 <= 1));
        Assert.IsTrue(stars.Texture.TryGetFileId(out var texture) && reader.Contains(texture));
    }

    [TestMethod]
    public void BuildsStarMeshOnTheUpperHalfOfTheSkySphereWhenConfigured()
    {
        using var reader = OpenReader();
        var stars = Gw2StarField.TryParse(reader.ReadDocument(StarFieldFileId))!;
        const float radius = 30720f;
        var mesh = Gw2StarFieldMesh.Build(stars, radius);

        Assert.AreEqual(stars.Stars.Count * 4, mesh.Vertices.Length);
        Assert.AreEqual(stars.Stars.Count * 6, mesh.Indices.Length);
        Assert.IsTrue(mesh.Vertices.All(vertex => MathF.Abs(vertex.Position.Length() - radius) < 1f));
        Assert.IsTrue(mesh.Vertices.Chunk(4).All(corners => corners.Average(vertex => vertex.Position.Z) < 1f));
        Assert.IsTrue(mesh.Vertices.Take(4).Select(vertex => vertex.Color & 0xFFFFFF).SequenceEqual(mesh.Vertices.Skip(4).Take(4).Select(vertex => vertex.Color & 0xFFFFFF)));
    }

    [TestMethod]
    public void ReadsStarFileOfMapEnvironmentWhenConfigured()
    {
        using var reader = OpenReader();
        var starFile = reader.ReadMap(StarMapFileId)!.Environment!.Sky!.StarFile;

        Assert.AreEqual(MapStarFieldFileId, starFile.FileId);
        Assert.IsNotNull(Gw2StarField.TryParse(reader.ReadDocument(MapStarFieldFileId)));
    }

    [TestMethod]
    [DataRow(3628525u, "Gw2CompositeBoneScale")]
    [DataRow(StarFieldFileId, "Gw2StarField")]
    [DataRow(Gw2FontManifest.ManifestFileId, "Gw2FontManifestFile")]
    public void PackDecodersDecodeChunksWithoutRegistrationWhenConfigured(uint fileId, string decoder)
    {
        using var reader = OpenReader();
        Assert.IsTrue(Gw2PackDecoders.Decode(reader.ReadDocument(fileId)).Any(decoded => decoded.Decoder == decoder));
    }

    private static Gw2ContentReader OpenReader()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        return Gw2ContentReader.Open(dat, verifyChecksums: false);
    }
}
