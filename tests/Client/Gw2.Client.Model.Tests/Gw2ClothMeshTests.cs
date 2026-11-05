using System.IO;
using System.Linq;
using System.Numerics;
using Gw2.Client.Model;
using Gw2.Dat.Content;
using Gw2.Formats;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2ClothMeshTests
{
    private const uint HumanFemaleHairStyleFourModelId = 35481;
    private const int HairMeshCount = 2;
    private const int HairClothCount = 2;
    private const int FirstClothVertexCount = 247;
    private const int FirstClothIndexCount = 1044;
    private const uint HairMaterialIndex = 2;

    [TestMethod]
    public void DrawsHairClothAsSkinnedMeshes()
    {
        var datPath = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(datPath) || !File.Exists(datPath)) Assert.Inconclusive("Gw2.dat is not installed.");
        using var reader = Gw2ContentReader.Open(datPath, verifyChecksums: false);
        var model = Gw2ModelBuilder.Build(HumanFemaleHairStyleFourModelId, reader.ReadModel(HumanFemaleHairStyleFourModelId)!, _ => null);

        Assert.AreEqual(HairMeshCount + HairClothCount, model.Meshes.Count);
        var cloth = model.Meshes[HairMeshCount];
        Assert.AreEqual(FirstClothVertexCount, cloth.Vertices.Length);
        Assert.AreEqual(FirstClothIndexCount, cloth.Indices.Length);
        Assert.AreEqual(HairMaterialIndex, cloth.MaterialIndex);
        Assert.AreEqual("hair", cloth.MaterialName);
        Assert.IsTrue(cloth.Indices.All(index => index < cloth.Vertices.Length));
        Assert.IsTrue(cloth.Vertices.All(vertex => vertex.HasSkin && vertex.HasNormal && vertex.HasUv0));
        Assert.IsTrue(cloth.Vertices.All(vertex => Enumerable.Range(0, 4).Sum(slot => (int)((vertex.BoneWeights >> (8 * slot)) & 0xFF)) == 255));
        Assert.IsTrue(cloth.Vertices.All(vertex => Enumerable.Range(0, 4).All(slot => vertex.BoneIndex(slot) < cloth.BoneBindings.Count)));

        var centre = cloth.Vertices.Aggregate(Vector3.Zero, (sum, vertex) => sum + vertex.Position) / cloth.Vertices.Length;
        var outward = cloth.Vertices.Count(vertex => Vector3.Dot(vertex.Normal, vertex.Position - centre) > 0f);
        System.Console.WriteLine($"outward {outward} of {cloth.Vertices.Length}");
        Assert.IsTrue(outward > cloth.Vertices.Length / 2);
    }
}
