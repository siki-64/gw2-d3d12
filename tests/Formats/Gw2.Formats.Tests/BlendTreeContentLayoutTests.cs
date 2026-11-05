using System;
using System.IO;
using System.Linq;
using Gw2.Formats;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Formats.Tests;

[TestClass]
public sealed class BlendTreeContentLayoutTests
{
    private static readonly int[][] ExpectedChildOffsets =
    [
        [], [], [], [], [0x0, 0x8], [0x8, 0x10, 0x18, 0x20, 0x28, 0x30, 0x38, 0x40], [], [], [0x8, 0x10], [0x0, 0x8, 0x10],
        [0x8, 0x10], [0x8], [0x8, 0x10], [0x18, 0x20, 0x28], [0x8, 0x10], [0x28, 0x30], [0x8, 0x10], [0x8, 0x10], [0x0], [],
        [0x20], [], [0x8, 0x10, 0x18, 0x20], [0x8, 0x10, 0x18, 0x20], [0x8, 0x10], [0x8, 0x10], [0x8, 0x10, 0x18, 0x20],
    ];

    [TestMethod]
    public void ReadsTheBlendTreeAndOpFieldsFromTheExecutable()
    {
        var path = Gw2Installation.ExecutablePath();
        if (path is null || !File.Exists(path)) Assert.Inconclusive("Gw2-64.exe is not installed.");

        var layout = BlendTreeContentLayout.FromExecutable(path);

        Assert.AreEqual(0x65u, layout.TreeContentType);
        Assert.AreEqual((0x28, 0x30, 0x20, 0x10, 0x18, 0x40, 0x38),
            (layout.OpsOffset, layout.OpCountOffset, layout.OpSize, layout.OpTypeOffset, layout.OpDataOffset, layout.RootOffset, layout.BlendTimeMillisecondsOffset));
        Assert.AreEqual(ExpectedChildOffsets.Length, layout.OpTypeCount);
        for (int type = 0; type < ExpectedChildOffsets.Length; type++)
            CollectionAssert.AreEqual(ExpectedChildOffsets[type], layout.ChildOffsetsByOpType[type].ToArray(), $"op type {type}");
        CollectionAssert.AreEqual(new int?[] { 0, 0, 0, 0 }, layout.ClipTokenOffsetByOpType.Take(4).ToArray());
        Assert.IsTrue(layout.ClipTokenOffsetByOpType.Skip(4).All(offset => offset is null));
        Assert.AreEqual(new BlendTreeWeightedOptionsLayout(0x8, 0x10), layout.WeightedOptionsByOpType[BlendTreeContentLayout.WeightedRandomType]);
        Assert.AreEqual(new BlendTreeWeightedOptionsLayout(0x0, 0x8), layout.WeightedOptionsByOpType[BlendTreeContentLayout.WeightedRandomOptionsFirstType]);
        Assert.AreEqual((0x18, 0x0, 0x8, 0xC), (layout.OptionSize, layout.OptionChildOffset, layout.OptionWeightOffset, layout.OptionCooldownMillisecondsOffset));
        Assert.AreEqual(new BlendTreeComparisonLayout(0x0, 0x4, 0x8, 0xC, 0x14, 0x18, 0x1C), layout.ComparisonFields);
        Assert.AreEqual(new BlendTreeDrivenLayout(0x0, 0x10, 0x14, 0x18), layout.DrivenFields);
        Assert.AreEqual((0x0, 0x4), (layout.SelectorParameterOffset, layout.SelectorLengthThresholdOffset));
        Assert.AreEqual((0x0, 0x4), (layout.DirectionMeasuredVectorKindOffset, layout.DirectionReferenceVectorKindOffset));
    }
}
