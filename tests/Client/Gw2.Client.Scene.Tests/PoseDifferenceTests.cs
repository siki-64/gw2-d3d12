using System;
using System.Numerics;
using Gw2.Client.Scene;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Scene.Tests;

[TestClass]
public sealed class PoseDifferenceTests
{
    private static readonly Vector3[] UnitProbe = [Vector3.Zero, Vector3.UnitX];

    [TestMethod]
    public void IdenticalPalettesHaveNoDifference()
    {
        Matrix4x4[] palette = [Matrix4x4.CreateTranslation(1, 2, 3), Matrix4x4.CreateRotationZ(0.5f)];

        var differences = Gw2PoseDifference.Compare(palette, palette, UnitProbe);

        Assert.AreEqual(2, differences.Length);
        Assert.AreEqual(0f, differences[1].Distance, 1e-5f);
        Assert.AreEqual(0f, differences[1].AngleRadians, 1e-3f);
    }

    [TestMethod]
    public void MeasuresTheLargestProbeOffsetAndTheRotationAngle()
    {
        Matrix4x4[] expected = [Matrix4x4.Identity];
        Matrix4x4[] actual = [Matrix4x4.CreateRotationZ(MathF.PI / 2f) * Matrix4x4.CreateTranslation(0, 0, 3)];

        var difference = Gw2PoseDifference.Compare(expected, actual, UnitProbe)[0];

        Assert.AreEqual(MathF.Sqrt(11f), difference.Distance, 1e-4f);
        Assert.AreEqual(MathF.PI / 2f, difference.AngleRadians, 1e-3f);
    }

    [TestMethod]
    public void BindPoseComparisonMeasuresEachBoneAgainstTheRoot()
    {
        var root = Matrix4x4.CreateTranslation(10, 0, 0);
        Matrix4x4[] palette = [root, root, Matrix4x4.CreateTranslation(0, 4, 0) * root];

        var differences = Gw2PoseDifference.CompareWithBindPose(palette, UnitProbe);

        Assert.AreEqual(0f, differences[1].Distance, 1e-5f);
        Assert.AreEqual(4f, differences[2].Distance, 1e-5f);
        Assert.AreEqual(2, Gw2PoseDifference.Largest(differences).Bone);
    }
}
