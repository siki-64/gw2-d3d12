using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Gw2.Client.Model;
using Gw2.Dat.Content;
using Gw2.Formats;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2MapPropTransformTests
{
    private const uint WayfarerFoothillsMapFileId = 187611;
    private const float JoinTolerance = 12f;
    private const int MinimumSegments = 20;
    private const float ElongatedRatio = 4f;

    [TestMethod]
    public void JoinsChainedSegmentPropsEndToEnd()
    {
        var datPath = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(datPath) || !File.Exists(datPath)) Assert.Inconclusive("Gw2.dat is not installed.");
        using var reader = Gw2ContentReader.Open(datPath, verifyChecksums: false);
        var map = reader.ReadMap(WayfarerFoothillsMapFileId)!;

        var joined = 0;
        var mirroredJoined = 0;
        var segments = 0;
        foreach (var group in map.Props.Placements.GroupBy(placement => placement.ModelFileId).Where(group => group.Count() >= MinimumSegments))
        {
            if (reader.ReadModel(group.Key) is not { } source) continue;
            var model = Gw2ModelBuilder.Build(group.Key, source, _ => null);
            var positions = model.Meshes.Where(mesh => mesh.IsRenderable && mesh.LodLevel == 0).SelectMany(mesh => mesh.Vertices).Select(vertex => vertex.Position).ToArray();
            if (positions.Length == 0) continue;
            var length = positions.Max(point => point.X) - positions.Min(point => point.X);
            var width = positions.Max(point => point.Y) - positions.Min(point => point.Y);
            if (length < width * ElongatedRatio) continue;
            var ends = new[] { positions.MinBy(point => point.X), positions.MaxBy(point => point.X) };
            var placements = group.ToList();
            segments += placements.Count;
            joined += JoinedSegments(placements, ends, placement => placement);
            mirroredJoined += JoinedSegments(placements, ends, placement => placement with { Rotation = -placement.Rotation });
        }

        Console.WriteLine($"segments {segments} joined {joined} mirrored {mirroredJoined}");
        Assert.IsTrue(segments >= MinimumSegments);
        Assert.IsTrue(joined > mirroredJoined * 2);
    }

    private static int JoinedSegments(System.Collections.Generic.List<Gw2MapPropPlacement> placements, Vector3[] ends, Func<Gw2MapPropPlacement, Gw2MapPropPlacement> variant)
    {
        var placedEnds = placements.Select(placement => ends.Select(end => Vector3.Transform(end, Gw2MapPropTransform.World(variant(placement)))).ToArray()).ToArray();
        var joined = 0;
        for (var segment = 0; segment < placedEnds.Length; segment++)
        {
            var touches = placedEnds[segment].Any(end => placedEnds.Where((_, other) => other != segment).SelectMany(otherEnds => otherEnds).Any(otherEnd => Vector3.Distance(end, otherEnd) < JoinTolerance));
            if (touches) joined++;
        }

        return joined;
    }
}
