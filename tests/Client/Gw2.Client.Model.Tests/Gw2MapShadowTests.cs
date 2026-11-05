using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using Gw2.Dat.Content;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2MapShadowTests
{
    private const uint MapFileId = 187611;
    private const uint ExternalShadowFileId = 187232;

    [TestMethod]
    public void FollowsTheExternalShadowFileAndDecodesEveryTile()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var external = Gw2PackDecoders.Decode<Gw2MapShadowExt>(reader.ReadDocument(MapFileId))!;
        Assert.AreEqual(ExternalShadowFileId, external.File.FileId);
        Assert.AreEqual((1920u, 3840u), (external.Width, external.Height));

        var shadow = reader.ReadMapShadow(MapFileId)!;
        Assert.AreEqual((1920u, 3840u), (shadow.Width, shadow.Height));
        Assert.AreEqual((64u, 64u), (shadow.TileWidth, shadow.TileHeight));
        Assert.AreEqual(shadow.TilesAcross * shadow.TilesDown, shadow.Tiles.Count);
        Assert.AreEqual(Gw2MapShadowTileCompression.Constant, shadow.Tiles[0].Compression);
        Assert.AreEqual(2.7475113E-05f, shadow.WorldToShadow.M11, 1e-12f);
        Assert.AreEqual(0.5735767f, shadow.Direction.X, 1e-6f);

        var quantized = Enumerable.Range(0, shadow.Tiles.Count).Where(index => shadow.Tiles[index].Compression != Gw2MapShadowTileCompression.Constant).ToArray();
        Assert.IsTrue(quantized.Length > 0);
        foreach (var index in quantized)
        {
            var depths = shadow.TileDepths(index);
            Assert.IsTrue(depths.All(depth => float.IsFinite(depth)));
            var moments = shadow.TileMoments(index, filtered: true);
            Assert.IsTrue(moments.All(moment => moment.Y + 1e-4f >= moment.X * moment.X));
        }
    }
}

[TestClass]
public sealed class Gw2MapShadowWindowTests
{
    private const uint MapFileId = 187611;
    private const int Resolution = 1024;
    private const float QuarterTexel = 0.25f;

    [TestMethod]
    public void ViewDistanceRoundsTheSwapDistanceToWholeChunksOfAtLeastThree()
    {
        Assert.AreEqual(24576f, Gw2MapTerrain.DefaultViewDistance(32));
        Assert.AreEqual(24576f, Gw2MapTerrain.DefaultViewDistance(64));
        Assert.AreEqual(61440f, Gw2MapTerrain.ViewDistanceOf(32, 61000f));
        Assert.AreEqual(9216f, Gw2MapTerrain.ViewDistanceOf(32, 1000f));
    }

    [TestMethod]
    public void ProjectsWorldPointsToTheSameTexelAsTheBakedMatrixAndStreamsOnlyNewPages()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var shadow = reader.ReadMapShadow(MapFileId)!;
        var window = new Gw2MapShadowWindow(shadow, Resolution, Gw2MapTerrain.DefaultViewDistance(Gw2MapTerrain.DefaultTilesPerChunk));
        var pages = new System.Collections.Generic.List<Gw2MapShadowPage>();

        var eye = WorldAtTexel(shadow, 900, 1900);
        var constants = window.Update(eye, FarCorners(eye, 2000f), pages);
        Assert.AreEqual(window.PagesPerSide * window.PagesPerSide, pages.Count);
        Assert.IsTrue(constants.Coverage > 0f && constants.Coverage <= 1f);
        Assert.AreEqual(new System.Numerics.Vector4(100f / (24576f * constants.Coverage), -99f, 0f, 0f), constants.WorldToShadowD);
        AssertSameTexel(shadow, constants, WorldAtTexel(shadow, 1000, 2000));

        pages.Clear();
        var movedEye = WorldAtTexel(shadow, 900, 2860);
        var moved = window.Update(movedEye, FarCorners(movedEye, 2000f), pages);
        Assert.IsTrue(pages.Count > 0 && pages.Count < window.PagesPerSide * window.PagesPerSide);
        var point = WorldAtTexel(shadow, 1000, 2900);
        AssertSameTexel(shadow, moved, point);
        var physicalColumn = (int)(Fraction(Vector4.Dot(new Vector4(point, 1f), moved.WorldToShadowA)) * window.PagesPerSide);
        var physicalRow = (int)(Fraction(Vector4.Dot(new Vector4(point, 1f), moved.WorldToShadowB)) * window.PagesPerSide);
        var reloaded = pages.Single(page => page.PhysicalColumn == physicalColumn && page.PhysicalRow == physicalRow);
        Assert.AreEqual((1000 / Gw2MapShadowWindow.PageSize, 2900 / Gw2MapShadowWindow.PageSize), (reloaded.Column, reloaded.Row));
    }

    [TestMethod]
    public void CombinedWindowDropsTheRingOffsetThatTheCombinePassSamplesWith()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var shadow = reader.ReadMapShadow(MapFileId)!;
        var depthRange = Gw2MapTerrain.DefaultViewDistance(Gw2MapTerrain.DefaultTilesPerChunk);
        var ringed = new Gw2MapShadowWindow(shadow, Resolution, depthRange);
        var combined = new Gw2MapShadowWindow(shadow, Resolution, depthRange, combinesShadowSamples: true);
        var pages = new System.Collections.Generic.List<Gw2MapShadowPage>();
        foreach (var eye in new[] { WorldAtTexel(shadow, 900, 1900), WorldAtTexel(shadow, 900, 2860) })
        {
            var ringedConstants = ringed.Update(eye, FarCorners(eye, 2000f), pages);
            var combinedConstants = combined.Update(eye, FarCorners(eye, 2000f), pages);
            Assert.AreEqual(ringed.RingOffset, combined.RingOffset);
            Assert.AreEqual(ringedConstants.WorldToShadowA.W, combinedConstants.WorldToShadowA.W + combined.RingOffset.X, 1e-5f);
            Assert.AreEqual(ringedConstants.WorldToShadowB.W, combinedConstants.WorldToShadowB.W + combined.RingOffset.Y, 1e-5f);
        }
        Assert.AreNotEqual(System.Numerics.Vector2.Zero, combined.RingOffset);
    }

    [TestMethod]
    public void CombineTapsTakeTheNearerDepthAndWeighTheCentreTwice()
    {
        var taps = Gw2FarShadowCombine.Taps(1024);
        Assert.AreEqual(new System.Numerics.Vector2(-1f / 1024f, 0f), taps[0].Offset);
        Assert.AreEqual(Gw2FarShadowCombine.WeightSum, taps.Sum(tap => tap.Weight));
        var moments = Gw2FarShadowCombine.Moments([1f, 1f, 0.2f, 1f, 1f], [0.5f, 0.5f, 0.5f, 0.5f, 0.5f]);
        Assert.AreEqual((4f * 0.5f * 0.5f + 0.2f) / 3f, moments.X, 1e-6f);
        Assert.AreEqual((4f * 0.5f * 0.25f + 0.04f) / 3f, moments.Y, 1e-6f);
    }

    private static void AssertSameTexel(Gw2MapShadow shadow, Gw2MapShadowConstants constants, Vector3 point)
    {
        var baked = Vector4.Transform(new Vector4(point, 1f), shadow.WorldToShadow);
        var bakedX = (baked.X * 0.5f + 0.5f) * shadow.Width;
        var bakedY = (0.5f - baked.Y * 0.5f) * shadow.Height;
        var position = new Vector4(point, 1f);
        var physicalX = Fraction(Vector4.Dot(position, constants.WorldToShadowA)) * Resolution;
        var physicalY = Fraction(Vector4.Dot(position, constants.WorldToShadowB)) * Resolution;
        Assert.AreEqual(bakedX % Gw2MapShadowWindow.PageSize, physicalX % Gw2MapShadowWindow.PageSize, QuarterTexel);
        Assert.AreEqual(bakedY % Gw2MapShadowWindow.PageSize, physicalY % Gw2MapShadowWindow.PageSize, QuarterTexel);
    }

    private static float Fraction(float value) => value - MathF.Floor(value);

    private static Vector3 WorldAtTexel(Gw2MapShadow shadow, float x, float y)
    {
        Matrix4x4.Invert(shadow.WorldToShadow, out var shadowToWorld);
        var clip = new Vector4(x / shadow.Width * 2f - 1f, 1f - y / shadow.Height * 2f, 0.5f, 1f);
        var world = Vector4.Transform(clip, shadowToWorld);
        return new Vector3(world.X, world.Y, world.Z) / world.W;
    }

    private static Vector3[] FarCorners(Vector3 eye, float reach) =>
    [
        eye + new Vector3(-reach, -reach, reach),
        eye + new Vector3(reach, -reach, reach),
        eye + new Vector3(-reach, reach, reach),
        eye + new Vector3(reach, reach, reach),
    ];
}
