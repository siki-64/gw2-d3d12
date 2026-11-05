using Gw2.Formats;
using System;
using System.IO;
using System.Numerics;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

using Gw2Model = Gw2.Dat.Content.Gw2Model;

[TestClass]
public sealed class Gw2ModelDataTests
{
    private const uint BoneOffsetModelFileId = 2198805;
    private const uint EffectLightModelFileId = 1765278;
    private const uint EffectWindModelFileId = 88293;
    private const uint ObstacleModelFileId = 2213253;
    private const uint TextureTransformModelFileId = 1494329;
    private const uint ClothModelFileId = 2306945;
    private const uint LightningModelFileId = 2083982;

    [TestMethod]
    public void ReadsBoneOffsetsLightsWindAndObstaclesWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);

        var boneOffsets = Gw2Model.Parse(reader.ReadDocument(BoneOffsetModelFileId)).BoneOffsets;
        Assert.AreEqual(241, boneOffsets.Count);
        Assert.AreEqual(671218ul, boneOffsets[0].Bone);
        Assert.AreEqual(new Vector4(0, 0, 0, 1), boneOffsets[0].Rotation);
        Assert.AreEqual(3, boneOffsets[0].BoneInverseOffset.Count);
        Assert.AreEqual(26.750519f, boneOffsets[0].BoneInverseOffset[1].W, 1e-5f);

        var lights = Gw2Model.Parse(reader.ReadDocument(EffectLightModelFileId)).EffectLights;
        Assert.AreEqual(15, lights.Count);
        Assert.AreEqual(100f, lights[1].FarDistance);
        Assert.AreEqual(0.5f, lights[1].Intensity);
        Assert.AreEqual(3, lights[1].Color.Length);

        var winds = Gw2Model.Parse(reader.ReadDocument(EffectWindModelFileId)).EffectWinds;
        Assert.AreEqual(3, winds.Count);
        Assert.AreEqual(200f, winds[0].NearDistance);
        Assert.AreEqual(250f, winds[0].FarDistance);
        Assert.AreEqual(5f, winds[0].Intensity);

        var obstacles = Gw2Model.Parse(reader.ReadDocument(ObstacleModelFileId)).Obstacles;
        Assert.AreEqual(88, obstacles.Count);
        Assert.AreEqual((byte)3, obstacles[0].Type);
        Assert.AreEqual(32.8f, obstacles[0].Height, 1e-5f);
        Assert.AreEqual(16.7f, obstacles[0].Radius, 1e-5f);
        Assert.AreEqual(1.1111113f, obstacles[0].TransformRowX.X, 1e-6f);
    }

    [TestMethod]
    public void ReadsMaterialConstantLinksAndTextureTransformsWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);

        var materials = Gw2Model.Parse(reader.ReadDocument(TextureTransformModelFileId)).Materials;
        Assert.AreEqual(190350863ul, materials[0].Token);
        Assert.AreEqual((byte)3, materials[0].TexCoordCount);
        Assert.AreEqual(896001641u, materials[0].ConstantLinks[0].ConstantToken);
        Assert.AreEqual(55882301008ul, materials[0].Textures[0].BlitId);

        var transform = materials[1].TextureTransforms[0];
        Assert.AreEqual((byte)5, transform.Columns);
        Assert.AreEqual((byte)5, transform.Rows);
        Assert.AreEqual((ushort)25, transform.Count);
        Assert.AreEqual(5f, transform.Fps);
    }

    [TestMethod]
    public void ReadsClothMeshesWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);

        var cloths = Gw2Model.Parse(reader.ReadDocument(ClothModelFileId)).Cloths;
        Assert.AreEqual(14, cloths.Count);
        foreach (var cloth in cloths)
        {
            Assert.IsTrue(cloth.Indices.Count > 0);
            Assert.AreEqual(0, cloth.Indices.Count % 3);
            Assert.IsTrue(cloth.MeshVertices.Length > 0);
            Assert.IsTrue(cloth.Lod0Constraints.Count > 0);
        }
    }

    [TestMethod]
    public void ReadsLightningSystemsBoltsAndNodesWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);

        var lightning = Gw2Model.Parse(reader.ReadDocument(LightningModelFileId)).Lightning!;
        Assert.AreEqual(25, lightning.Bolts.Count);
        Assert.IsTrue(lightning.Systems.Count > 0);
        foreach (var system in lightning.Systems)
            foreach (var boltIndex in system.BoltIndices)
                Assert.IsTrue(boltIndex < lightning.Bolts.Count);
        foreach (var node in lightning.Nodes)
            foreach (var childIndex in node.ChildIndices)
                Assert.IsTrue(childIndex < lightning.Nodes.Count);
    }
}
