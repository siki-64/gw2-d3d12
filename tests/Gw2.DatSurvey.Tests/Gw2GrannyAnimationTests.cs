using System;
using System.Buffers.Binary;
using System.Text;
using Gw2.Dat.Content;
using Gw2.Dat.Archive;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.DatSurvey.Tests;

[TestClass]
public sealed class Gw2GrannyAnimationTests
{
    [TestMethod]
    public void ParsesThePackedAnimationTree()
    {
        var blob = BuildBlob();

        var animation = Gw2GrannyAnimation.TryParse(blob);

        Assert.IsNotNull(animation);
        Assert.AreEqual("anim", animation.Name);
        Assert.AreEqual(1f, animation.Duration);
        Assert.AreEqual(0.033f, animation.TimeStep);
        Assert.AreEqual(1, animation.TrackGroups.Count);

        var group = animation.TrackGroups[0];
        Assert.AreEqual("grp", group.Name);
        Assert.AreEqual(1, group.TransformTracks.Count);

        var track = group.TransformTracks[0];
        Assert.AreEqual("bone:root", track.Name);
        Assert.AreEqual(Gw2GrannyCurveFormat.Constant, track.Orientation.Format);
        Assert.AreEqual(4, track.Orientation.Dimension);
        Assert.AreEqual(Gw2GrannyCurveFormat.Identity, track.Position.Format);
        Assert.AreEqual(Gw2GrannyCurveFormat.Identity, track.ScaleShear.Format);
    }

    [TestMethod]
    public void ReadsConstantCurveValues()
    {
        var track = Gw2GrannyAnimation.TryParse(BuildBlob())!.TrackGroups[0].TransformTracks[0];

        Span<float> value = stackalloc float[4];
        Assert.IsTrue(track.Orientation.TryGetConstant(value));
        CollectionAssert.AreEqual(new[] { 1f, 2f, 3f, 4f }, value.ToArray());
    }

    [TestMethod]
    public void ClassifiesCompressedOrientationCurve()
    {
        var track = Gw2GrannyAnimation.TryParse(BuildBlob("CurveDataHeader_D4nK16uC15u"))!.TrackGroups[0].TransformTracks[0];

        Assert.AreEqual(Gw2GrannyCurveFormat.Compressed, track.Orientation.Format);
        Assert.AreEqual(4, track.Orientation.Dimension);
        Assert.AreEqual("CurveDataHeader_D4nK16uC15u", track.Orientation.FormatName);
    }

    [TestMethod]
    public void ClassifiesFloatKeyframeOrientationCurve()
    {
        var track = Gw2GrannyAnimation.TryParse(BuildBlob("CurveDataHeader_DaK32fC32f"))!.TrackGroups[0].TransformTracks[0];

        Assert.AreEqual(Gw2GrannyCurveFormat.Keyframes, track.Orientation.Format);
    }

    [TestMethod]
    public void ReadsFloatKeyframeKnotsAndControls()
    {
        var track = Gw2GrannyAnimation.TryParse(BuildBlob("CurveDataHeader_DaK32fC32f"))!.TrackGroups[0].TransformTracks[0];

        Assert.AreEqual(Gw2GrannyCurveFormat.Keyframes, track.Orientation.Format);
        Assert.AreEqual(3, track.Orientation.Degree);
        Assert.AreEqual(4, track.Orientation.Dimension);
        CollectionAssert.AreEqual(new[] { 0f, 1f }, track.Orientation.Knots.ToArray());
        Assert.AreEqual(8, track.Orientation.Controls.Count);
        Assert.AreEqual(1f, track.Orientation.Controls[0]);
    }

    [TestMethod]
    public void SamplesALinearCurve()
    {
        var curve = Gw2GrannyAnimation.TryParse(BuildBlob("CurveDataHeader_DaK32fC32f", degree: 1, controlCount: 2))!
            .TrackGroups[0].TransformTracks[0].Orientation;

        Assert.AreEqual(1, curve.Dimension);
        Assert.AreEqual(1f, curve.Sample(0f)[0], 1e-4f);
        Assert.AreEqual(1.5f, curve.Sample(0.5f)[0], 1e-4f);
        Assert.AreEqual(2f, curve.Sample(1f)[0], 1e-4f);
    }

    [TestMethod]
    public void ReadsCompressedThreeComponentCurve()
    {
        var blob = BuildCurveBlob("CurveDataHeader_D3K16uC16u", 1, b =>
        {
            WriteU16(b, 0x202, 0x3F80);
            WriteF32(b, 0x204, 1f);
            WriteF32(b, 0x208, 1f);
            WriteF32(b, 0x20C, 1f);
            WriteF32(b, 0x210, 0f);
            WriteF32(b, 0x214, 0f);
            WriteF32(b, 0x218, 0f);
            WriteU32(b, 0x21C, 8);
            WriteU32(b, 0x220, 0x240);
            WriteU16(b, 0x240, 0);
            WriteU16(b, 0x242, 1000);
            WriteU16(b, 0x244, 0);
            WriteU16(b, 0x246, 0);
            WriteU16(b, 0x248, 0);
            WriteU16(b, 0x24A, 100);
            WriteU16(b, 0x24C, 200);
            WriteU16(b, 0x24E, 300);
        });

        var curve = Gw2GrannyAnimation.TryParse(blob)!.TrackGroups[0].TransformTracks[0].Orientation;

        Assert.AreEqual(Gw2GrannyCurveFormat.Compressed, curve.Format);
        Assert.AreEqual(3, curve.Dimension);
        CollectionAssert.AreEqual(new[] { 0f, 1000f }, curve.Knots.ToArray());
        CollectionAssert.AreEqual(new[] { 0f, 0f, 0f, 100f, 200f, 300f }, curve.Controls.ToArray());

        var sample = curve.Sample(500f);
        Assert.AreEqual(50f, sample[0], 1e-3f);
        Assert.AreEqual(100f, sample[1], 1e-3f);
        Assert.AreEqual(150f, sample[2], 1e-3f);
    }

    [TestMethod]
    public void ReadsDynamicDimensionCompressedCurve()
    {
        var blob = BuildCurveBlob("CurveDataHeader_DaK16uC16u", 1, b =>
        {
            WriteU16(b, 0x202, 0x3F80);
            WriteU32(b, 0x204, 6);
            WriteU32(b, 0x208, 0x240);
            WriteU32(b, 0x20C, 8);
            WriteU32(b, 0x210, 0x260);
            WriteF32(b, 0x240, 1f);
            WriteF32(b, 0x244, 1f);
            WriteF32(b, 0x248, 1f);
            WriteF32(b, 0x24C, 0f);
            WriteF32(b, 0x250, 0f);
            WriteF32(b, 0x254, 0f);
            WriteU16(b, 0x260, 0);
            WriteU16(b, 0x262, 1000);
            WriteU16(b, 0x264, 1);
            WriteU16(b, 0x266, 2);
            WriteU16(b, 0x268, 3);
            WriteU16(b, 0x26A, 11);
            WriteU16(b, 0x26C, 22);
            WriteU16(b, 0x26E, 33);
        });

        var curve = Gw2GrannyAnimation.TryParse(blob)!.TrackGroups[0].TransformTracks[0].Orientation;

        Assert.AreEqual(Gw2GrannyCurveFormat.Compressed, curve.Format);
        Assert.AreEqual(3, curve.Dimension);
        Assert.AreEqual(6, curve.Controls.Count);

        var sample = curve.Sample(500f);
        Assert.AreEqual(6f, sample[0], 1e-3f);
        Assert.AreEqual(12f, sample[1], 1e-3f);
        Assert.AreEqual(18f, sample[2], 1e-3f);
    }

    [TestMethod]
    public void ReadsNormalizedQuaternionCurve()
    {
        var blob = BuildCurveBlob("CurveDataHeader_D4nK16uC15u", 1, b =>
        {
            WriteU16(b, 0x202, 0x0444);
            WriteF32(b, 0x204, 1f);
            WriteU32(b, 0x208, 4);
            WriteU32(b, 0x20C, 0x240);
            WriteU16(b, 0x240, 0);
            WriteU16(b, 0x242, 29956);
            WriteU16(b, 0x244, (ushort)(29956 | 0x8000));
            WriteU16(b, 0x246, (ushort)(29956 | 0x8000));
        });

        var curve = Gw2GrannyAnimation.TryParse(blob)!.TrackGroups[0].TransformTracks[0].Orientation;

        Assert.AreEqual(Gw2GrannyCurveFormat.Compressed, curve.Format);
        Assert.AreEqual(4, curve.Dimension);

        var sample = curve.Sample(0f);
        Assert.AreEqual(0.5f, sample[0], 2e-3f);
        Assert.AreEqual(0.5f, sample[1], 2e-3f);
        Assert.AreEqual(0.5f, sample[2], 2e-3f);
        Assert.AreEqual(0.5f, sample[3], 2e-3f);
    }

    [TestMethod]
    public void RejectsTruncatedBytes()
    {
        Assert.IsNull(Gw2GrannyAnimation.TryParse(new byte[16]));
    }

    [TestMethod]
    public void ParsesThePackedAnimationTreeAtPointerSize8()
    {
        var animation = Gw2GrannyAnimation.TryParse(BuildBlob64(), 8);

        Assert.IsNotNull(animation);
        Assert.AreEqual("anim", animation.Name);
        Assert.AreEqual(1f, animation.Duration, 1e-4f);
        Assert.AreEqual(0.033f, animation.TimeStep, 1e-4f);
        Assert.AreEqual(1, animation.TrackGroups.Count);

        var group = animation.TrackGroups[0];
        Assert.AreEqual("grp", group.Name);
        Assert.AreEqual(1, group.TransformTracks.Count);

        var track = group.TransformTracks[0];
        Assert.AreEqual("bone:root", track.Name);
        Assert.AreEqual(Gw2GrannyCurveFormat.Constant, track.Orientation.Format);
        Assert.AreEqual(4, track.Orientation.Dimension);
        Assert.AreEqual(Gw2GrannyCurveFormat.Identity, track.Position.Format);
        Assert.AreEqual(Gw2GrannyCurveFormat.Identity, track.ScaleShear.Format);

        Span<float> value = stackalloc float[4];
        Assert.IsTrue(track.Orientation.TryGetConstant(value));
        CollectionAssert.AreEqual(new[] { 1f, 2f, 3f, 4f }, value.ToArray());
    }

    private static byte[] BuildBlob64()
    {
        var blob = new byte[0x400];

        WritePtr64(blob, 0x00, 0x200);
        WriteF32(blob, 0x08, 1f);
        WriteF32(blob, 0x0C, 0.033f);
        WriteF32(blob, 0x10, 1f);
        WriteU32(blob, 0x14, 1);
        WritePtr64(blob, 0x18, 0x30);

        WritePtr64(blob, 0x30, 0x40);

        WritePtr64(blob, 0x40, 0x210);
        WriteU32(blob, 0x40 + 0x08, 0);
        WritePtr64(blob, 0x40 + 0x0C, 0);
        WriteU32(blob, 0x40 + 0x14, 1);
        WritePtr64(blob, 0x40 + 0x18, 0x70);

        WritePtr64(blob, 0x70, 0x220);
        WriteU32(blob, 0x70 + 0x08, 0);
        WritePtr64(blob, 0x70 + 0x0C, 0x2C0);
        WritePtr64(blob, 0x70 + 0x14, 0x320);
        WritePtr64(blob, 0x70 + 0x1C, 0x2E0);
        WritePtr64(blob, 0x70 + 0x24, 0x340);
        WritePtr64(blob, 0x70 + 0x2C, 0x300);
        WritePtr64(blob, 0x70 + 0x34, 0x360);

        WriteU32(blob, 0x2C0, 1);
        WriteU32(blob, 0x2C4, 0x240);
        WriteU32(blob, 0x2E0, 1);
        WriteU32(blob, 0x2E4, 0x280);
        WriteU32(blob, 0x300, 1);
        WriteU32(blob, 0x304, 0x2A0);

        blob[0x320] = 0;
        blob[0x321] = 0;
        WriteF32(blob, 0x324, 1f);
        WriteF32(blob, 0x328, 2f);
        WriteF32(blob, 0x32C, 3f);
        WriteF32(blob, 0x330, 4f);
        blob[0x340] = 0;
        blob[0x341] = 0;
        blob[0x360] = 0;
        blob[0x361] = 0;

        WriteString(blob, 0x200, "anim");
        WriteString(blob, 0x210, "grp");
        WriteString(blob, 0x220, "bone:root");
        WriteString(blob, 0x240, "CurveDataHeader_D4Constant32f");
        WriteString(blob, 0x280, "CurveDataHeader_DaIdentity");
        WriteString(blob, 0x2A0, "CurveDataHeader_DaIdentity");

        return blob;
    }

    private static byte[] BuildCurveBlob(string orientationType, byte degree, Action<byte[]> writeObject)
    {
        var blob = new byte[0x300];

        WriteU32(blob, 0x00, 0x140);
        WriteF32(blob, 0x04, 1f);
        WriteF32(blob, 0x08, 0.033f);
        WriteF32(blob, 0x0C, 1f);
        WriteU32(blob, 0x10, 1);
        WriteU32(blob, 0x14, 0x28);

        WriteU32(blob, 0x28, 0x30);

        WriteU32(blob, 0x30, 0x148);
        WriteU32(blob, 0x30 + 0x0C, 1);
        WriteU32(blob, 0x30 + 0x10, 0xB4);

        WriteU32(blob, 0xB4, 0x150);
        WriteU32(blob, 0xB4 + 0x04, 0);
        WriteU32(blob, 0xB4 + 0x08, 0xE0);
        WriteU32(blob, 0xB4 + 0x0C, 0x200);
        WriteU32(blob, 0xB4 + 0x10, 0x100);
        WriteU32(blob, 0xB4 + 0x18, 0x100);

        WriteU32(blob, 0xE0, 1);
        WriteU32(blob, 0xE0 + 0x04, 0x160);
        WriteU32(blob, 0x100, 1);
        WriteU32(blob, 0x100 + 0x04, 0x180);

        blob[0x200] = 0;
        blob[0x201] = degree;
        writeObject(blob);

        WriteString(blob, 0x140, "anim");
        WriteString(blob, 0x148, "grp");
        WriteString(blob, 0x150, "bone:root");
        WriteString(blob, 0x160, orientationType);
        WriteString(blob, 0x180, "CurveDataHeader_DaIdentity");

        return blob;
    }

    private static byte[] BuildBlob(string orientationType = "CurveDataHeader_D4Constant32f", int degree = 3, int controlCount = 8)
    {
        var blob = new byte[0x300];

        WriteU32(blob, 0x00, 0x140);
        WriteF32(blob, 0x04, 1f);
        WriteF32(blob, 0x08, 0.033f);
        WriteF32(blob, 0x0C, 1f);
        WriteU32(blob, 0x10, 1);
        WriteU32(blob, 0x14, 0x28);

        WriteU32(blob, 0x28, 0x30);

        WriteU32(blob, 0x30, 0x148);
        WriteU32(blob, 0x30 + 0x0C, 1);
        WriteU32(blob, 0x30 + 0x10, 0xB4);

        WriteU32(blob, 0xB4, 0x150);
        WriteU32(blob, 0xB4 + 0x04, 0);
        WriteU32(blob, 0xB4 + 0x08, 0xE0);
        WriteU32(blob, 0xB4 + 0x0C, 0x200);
        WriteU32(blob, 0xB4 + 0x10, 0x100);
        WriteU32(blob, 0xB4 + 0x18, 0x100);

        WriteU32(blob, 0xE0, 1);
        WriteU32(blob, 0xE0 + 0x04, 0x160);
        WriteU32(blob, 0x100, 1);
        WriteU32(blob, 0x100 + 0x04, 0x180);

        if (orientationType == "CurveDataHeader_DaK32fC32f")
        {
            blob[0x200] = 0;
            blob[0x201] = (byte)degree;
            WriteU32(blob, 0x204, 2);
            WriteU32(blob, 0x208, 0x240);
            WriteU32(blob, 0x20C, (uint)controlCount);
            WriteU32(blob, 0x210, 0x250);
            WriteF32(blob, 0x240, 0f);
            WriteF32(blob, 0x244, 1f);
            for (var i = 0; i < controlCount; i++) WriteF32(blob, 0x250 + i * 4, i + 1);
        }
        else
        {
            WriteU32(blob, 0x200, 5);
            WriteF32(blob, 0x204, 1f);
            WriteF32(blob, 0x208, 2f);
            WriteF32(blob, 0x20C, 3f);
            WriteF32(blob, 0x210, 4f);
        }

        WriteString(blob, 0x140, "anim");
        WriteString(blob, 0x148, "grp");
        WriteString(blob, 0x150, "bone:root");
        WriteString(blob, 0x160, orientationType);
        WriteString(blob, 0x180, "CurveDataHeader_DaIdentity");

        return blob;
    }

    private static void WriteString(byte[] blob, int offset, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        bytes.CopyTo(blob, offset);
    }

    private static void WriteU16(byte[] blob, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(offset, 2), value);

    private static void WriteU32(byte[] blob, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(offset, 4), value);

    private static void WritePtr64(byte[] blob, int offset, long value) =>
        BinaryPrimitives.WriteInt64LittleEndian(blob.AsSpan(offset, 8), value);

    private static void WriteF32(byte[] blob, int offset, float value) =>
        BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(offset, 4), BitConverter.SingleToInt32Bits(value));
}
