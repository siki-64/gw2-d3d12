using System;
using System.Numerics;
using Gw2.Dat.Content;
using Gw2.Dat.Archive;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.DatSurvey.Tests;

[TestClass]
public sealed class PackedTangentFrameTests
{
    private static readonly byte[] Frame =
    {
        0xC3, 0x64, 0x18, 0x00,
        0xD9, 0xD1, 0xA5, 0x00,
        0x46, 0xDD, 0x40, 0x00,
    };

    [TestMethod]
    public void DecodesAnOrthonormalFrame()
    {
        var (normal, tangent, bitangent) = Gw2PackedTangentFrame.Decode(Frame);

        AssertVector(new Vector3(-0.818f, -0.217f, 0.533f), normal);
        AssertVector(new Vector3(0.296f, 0.643f, 0.706f), tangent);
        AssertVector(new Vector3(-0.501f, 0.737f, -0.453f), bitangent);

        Assert.AreEqual(0f, Vector3.Dot(tangent, normal), 0.01f);
        Assert.AreEqual(0f, Vector3.Dot(bitangent, normal), 0.01f);
        Assert.AreEqual(0f, Vector3.Dot(bitangent, tangent), 0.01f);
    }

    [TestMethod]
    public void IgnoresTheHighByte()
    {
        Assert.AreEqual(
            Gw2PackedTangentFrame.DecodeVector(0x001864C3),
            Gw2PackedTangentFrame.DecodeVector(0xFF1864C3));
    }

    private static void AssertVector(Vector3 expected, Vector3 actual)
    {
        Assert.AreEqual(expected.X, actual.X, 0.01f);
        Assert.AreEqual(expected.Y, actual.Y, 0.01f);
        Assert.AreEqual(expected.Z, actual.Z, 0.01f);
        Assert.AreEqual(1f, actual.Length(), 0.001f);
    }
}
