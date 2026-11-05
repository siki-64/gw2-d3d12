using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2ModelMaterialDrawTests
{
    private const uint WayfarerFoothillsMapFileId = 187611;

    [TestMethod]
    public void MaterialFlagsMapToTheExeDrawFlags()
    {
        Assert.AreEqual(Gw2DrawFlags.SkipEffectCull, Gw2ModelMaterialDraw.DrawFlagsOf(0x1));
        Assert.AreEqual(Gw2DrawFlags.LessEqualWithoutDepthWrite, Gw2ModelMaterialDraw.DrawFlagsOf(0x2));
        Assert.AreEqual(0x8u | 0x80000u | 0x200000u, Gw2ModelMaterialDraw.DrawFlagsOf(0x10 | 0x80 | 0x2000));
        Assert.AreEqual(0u, Gw2ModelMaterialDraw.DrawFlagsOf(0x4 | 0x8 | 0x100 | 0x200 | 0x800 | 0x1000));
    }

    [TestMethod]
    public void ModelSortOrderZeroKeepsTheMaterialBucket()
    {
        Assert.AreEqual(0, Gw2ModelMaterialDraw.SortBucketOverrideOf(0));
        Assert.AreEqual(Gw2SortBuckets.Default, Gw2ModelMaterialDraw.SortBucketOverrideOf(1));
        Assert.AreEqual(13, Gw2ModelMaterialDraw.SortBucketOverrideOf(11));
        Assert.AreEqual(0, Gw2ModelMaterialDraw.SortBucketOverrideOf(17));
    }

    [TestMethod]
    public void MapPropMaterialsUseFlagsAndSortOrdersTheTableCovers()
    {
        var datPath = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(datPath) || !File.Exists(datPath)) Assert.Inconclusive("Gw2.dat is not installed.");
        using var reader = Gw2ContentReader.Open(datPath, verifyChecksums: false);
        var map = reader.ReadMap(WayfarerFoothillsMapFileId)!;
        var materials = map.Props.Placements.Select(placement => placement.ModelFileId).Distinct()
            .Select(reader.ReadModel).Where(model => model is not null)
            .SelectMany(model => model!.Materials).ToList();
        Assert.IsTrue(materials.All(material => material.SortOrder < 17));
        Assert.IsTrue(materials.Any(material => Gw2ModelMaterialDraw.SortBucketOverrideOf(material.SortOrder) != 0));

        var grFlags = materials
            .Select(material => material.TryGetFileId(out var fileId) ? reader.ReadMaterial(fileId) : null)
            .Where(amat => amat is not null).Select(amat => amat!.GrFlags).Distinct().ToList();
        CollectionAssert.Contains(grFlags, 0x404Cu);
        Assert.AreEqual(0x11C00u, Gw2DrawStageFlags.Of(0x404C, 0));
        foreach (var flags in grFlags)
            Assert.AreEqual((flags & 0x80) == 0, Gw2AmatDrawState.Of(0, 0, Gw2AmatEffectKeys.Default, stageFlags: Gw2DrawStageFlags.Of(flags, 0)).DepthWrite);
    }

    [TestMethod]
    public void GrmtFieldsAddTheGrMatDrawFlags()
    {
        Assert.AreEqual(0u, Gw2DrawFlags.OfGr(0x404C, 0, 0));
        Assert.AreEqual(Gw2DrawFlags.GreaterDepthTest, Gw2DrawFlags.OfGr(0x10000, 0, 0));
        Assert.AreEqual(0x1000000u | 0x4u, Gw2DrawFlags.OfGr(0, 2, 1));
        Assert.AreEqual(0x10000u | 0x40000u | 0x80000u, Gw2DrawFlags.OfGr(0x200 | 0x800 | 0x100, 0, 0));
    }

    [TestMethod]
    public void StageFlagsCombineGrmtAndMaterialFlagsLikeTheShader()
    {
        Assert.AreEqual(Gw2DrawStageFlags.NoDepthWrite, Gw2DrawStageFlags.Of(0x80, 0));
        Assert.AreEqual(0x80u | 0x1800u, Gw2DrawStageFlags.Of(0x1 | 0x8, 0));
        Assert.AreEqual(0x10u | 0x40u, Gw2DrawStageFlags.Of(0, Gw2DrawFlags.SkipEffectCull | Gw2DrawFlags.LessEqualWithoutDepthWrite));
    }
}
