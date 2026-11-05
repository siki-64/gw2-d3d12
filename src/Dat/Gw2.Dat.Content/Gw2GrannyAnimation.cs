using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace Gw2.Dat.Content;

public enum Gw2GrannyCurveRole
{
    Orientation,
    Position,
    ScaleShear,
    Vector,
}

public enum Gw2GrannyCurveFormat
{
    Unknown,
    Identity,
    Constant,
    Keyframes,
    Compressed,
}

public sealed class Gw2GrannyCurve
{
    public Gw2GrannyCurveRole Role { get; init; }
    public Gw2GrannyCurveFormat Format { get; init; }
    public string FormatName { get; init; } = string.Empty;
    public int Degree { get; init; }
    public int Dimension { get; init; }
    public IReadOnlyList<float> Constant { get; init; } = [];
    public IReadOnlyList<float> Knots { get; init; } = [];
    public IReadOnlyList<float> Controls { get; init; } = [];

    public bool TryGetConstant(Span<float> destination)
    {
        if (Format != Gw2GrannyCurveFormat.Constant || Constant.Count != destination.Length) return false;
        for (var i = 0; i < Constant.Count; i++) destination[i] = Constant[i];
        return true;
    }

    public float[] Sample(float time)
    {
        var result = new float[Dimension];
        return TrySample(time, result) ? result : [];
    }

    public bool TrySample(float time, Span<float> destination)
    {
        if (Dimension <= 0 || destination.Length < Dimension) return false;

        if (Format == Gw2GrannyCurveFormat.Identity || Constant.Count == Dimension && Format == Gw2GrannyCurveFormat.Constant)
        {
            if (Constant.Count == Dimension)
            {
                for (var i = 0; i < Dimension; i++) destination[i] = Constant[i];
            }
            else
            {
                WriteIdentity(destination);
            }

            return true;
        }

        var knotCount = Knots.Count;
        if (knotCount == 1 && Controls.Count >= Dimension)
        {
            for (var d = 0; d < Dimension; d++) destination[d] = Controls[d];
            if (Role == Gw2GrannyCurveRole.Orientation && Dimension >= 4) Normalize(destination, 4);
            return true;
        }

        if (Degree <= 0 || knotCount <= Degree || Controls.Count < knotCount * Dimension)
        {
            WriteIdentity(destination);
            return true;
        }

        var minimum = Degree;
        var maximum = knotCount - Degree;
        if (maximum < minimum) maximum = minimum;

        var clamped = Math.Clamp(time, Knots[0], Knots[knotCount - 1]);
        if (knotCount < 2 * Degree)
        {
            var nearest = 0;
            while (nearest < knotCount - 1 && Knots[nearest + 1] <= clamped) nearest++;
            for (var d = 0; d < Dimension; d++) destination[d] = Controls[nearest * Dimension + d];
            if (Role == Gw2GrannyCurveRole.Orientation && Dimension >= 4) Normalize(destination, 4);
            return true;
        }

        var knot = minimum;
        while (knot < maximum && Knots[knot] <= clamped) knot++;

        for (var d = 0; d < Dimension; d++) destination[d] = 0f;
        switch (Degree)
        {
            case 1:
            {
                var c0 = Coefficient(Knots[knot - 1], Knots[knot], clamped);
                var c1 = 1f - c0;
                for (var d = 0; d < Dimension; d++)
                    destination[d] = c1 * Controls[(knot - 1) * Dimension + d] + c0 * Controls[knot * Dimension + d];
                break;
            }
            case 2:
            {
                QuadraticCoefficients(Knots[knot - 2], Knots[knot - 1], Knots[knot], Knots[knot + 1], clamped,
                    out var c2, out var c1, out var c0);
                for (var d = 0; d < Dimension; d++)
                    destination[d] = c2 * Controls[(knot - 2) * Dimension + d]
                        + c1 * Controls[(knot - 1) * Dimension + d]
                        + c0 * Controls[knot * Dimension + d];
                break;
            }
            case 3:
            {
                CubicCoefficients(Knots[knot - 3], Knots[knot - 2], Knots[knot - 1], Knots[knot], Knots[knot + 1], Knots[knot + 2], clamped,
                    out var c3, out var c2, out var c1, out var c0);
                for (var d = 0; d < Dimension; d++)
                    destination[d] = c3 * Controls[(knot - 3) * Dimension + d]
                        + c2 * Controls[(knot - 2) * Dimension + d]
                        + c1 * Controls[(knot - 1) * Dimension + d]
                        + c0 * Controls[knot * Dimension + d];
                break;
            }
            default:
            {
                for (var d = 0; d < Dimension; d++) destination[d] = Controls[knot * Dimension + d];
                break;
            }
        }

        if (Role == Gw2GrannyCurveRole.Orientation && Dimension >= 4) Normalize(destination, 4);
        return true;
    }

    private void WriteIdentity(Span<float> destination)
    {
        for (var d = 0; d < Dimension; d++) destination[d] = 0f;
        switch (Role)
        {
            case Gw2GrannyCurveRole.Orientation:
                destination[3] = 1f;
                break;
            case Gw2GrannyCurveRole.ScaleShear:
                if (Dimension >= 9)
                {
                    destination[0] = 1f;
                    destination[4] = 1f;
                    destination[8] = 1f;
                }

                break;
        }
    }

    private static float Coefficient(float lower, float upper, float time)
    {
        var length = upper - lower;
        return length != 0f ? (time - lower) / length : 0f;
    }

    private static void QuadraticCoefficients(float ti2, float ti1, float ti, float ti1Next, float time,
        out float c2, out float c1, out float c0)
    {
        var l0 = Coefficient(ti1, ti, time);
        var l11 = Coefficient(ti2, ti, time);
        var l12 = Coefficient(ti1, ti1Next, time);
        var m = l11 + l0 - l0 * l11;
        c0 = l0 * l12;
        c1 = m - c0;
        c2 = 1f - m;
    }

    private static void CubicCoefficients(float ti3, float ti2, float ti1, float ti, float ti1Next, float ti2Next, float time,
        out float c3, out float c2, out float c1, out float c0)
    {
        var l0 = Coefficient(ti1, ti, time);
        var l11 = Coefficient(ti2, ti, time);
        var l12 = Coefficient(ti1, ti1Next, time);
        var l21 = Coefficient(ti3, ti, time);
        var l22 = Coefficient(ti2, ti1Next, time);
        var l23 = Coefficient(ti1, ti2Next, time);

        var m0 = 1f - l0;
        var m11 = 1f - l11;
        var m12 = 1f - l12;
        var m21 = 1f - l21;
        var m22 = 1f - l22;
        var m23 = 1f - l23;

        var m0m11 = m0 * m11;
        var m0l11 = m0 * l11;
        var l0m12 = l0 * m12;
        var l0l12 = l0 * l12;

        c3 = m0m11 * m21;
        c2 = m0m11 * l21 + m0l11 * m22 + l0m12 * m22;
        c1 = m0l11 * l22 + l0m12 * l22 + l0l12 * m23;
        c0 = l0l12 * l23;
    }

    private static void Normalize(Span<float> value, int count)
    {
        var lengthSquared = 0f;
        for (var i = 0; i < count; i++) lengthSquared += value[i] * value[i];
        if (lengthSquared <= 1e-12f) return;
        var scale = 1f / MathF.Sqrt(lengthSquared);
        for (var i = 0; i < count; i++) value[i] *= scale;
    }
}

public sealed class Gw2GrannyTransformTrack
{
    public string Name { get; init; } = string.Empty;
    public uint Flags { get; init; }
    public Gw2GrannyCurve Orientation { get; init; } = new();
    public Gw2GrannyCurve Position { get; init; } = new();
    public Gw2GrannyCurve ScaleShear { get; init; } = new();
}

public sealed class Gw2GrannyVectorTrack
{
    public string Name { get; init; } = string.Empty;
    public uint TrackKey { get; init; }
    public int Dimension { get; init; }
    public Gw2GrannyCurve ValueCurve { get; init; } = new();
}

public sealed class Gw2GrannyTrackGroup
{
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<Gw2GrannyVectorTrack> VectorTracks { get; init; } = [];
    public IReadOnlyList<Gw2GrannyTransformTrack> TransformTracks { get; init; } = [];
}

public sealed class Gw2GrannyAnimation
{
    public string Name { get; init; } = string.Empty;
    public float Duration { get; init; }
    public float TimeStep { get; init; }
    public float Oversampling { get; init; }
    public IReadOnlyList<Gw2GrannyTrackGroup> TrackGroups { get; init; } = [];

    public static Gw2GrannyAnimation? TryParse(byte[] blob, int pointerSize = 4) => TryParse(blob.AsSpan(), pointerSize);

    public static Gw2GrannyAnimation? TryParse(ReadOnlySpan<byte> blob, int pointerSize = 4)
    {
        if (pointerSize != 4 && pointerSize != 8) return null;
        if (blob.Length < pointerSize + 20) return null;

        var animation = ReadPtr(blob, 0x00, pointerSize);
        var duration = ReadF32(blob, pointerSize);
        var timeStep = ReadF32(blob, pointerSize + 4);
        var oversampling = ReadF32(blob, pointerSize + 8);
        var groupCount = (int)ReadU32(blob, pointerSize + 12);
        var groupsOffset = ReadPtr(blob, pointerSize + 16, pointerSize);
        if (groupCount < 0 || groupsOffset <= 0 || (long)groupsOffset + (long)groupCount * pointerSize > blob.Length) return null;

        var groups = new List<Gw2GrannyTrackGroup>(groupCount);
        for (var g = 0; g < groupCount; g++)
        {
            var groupOffset = ReadPtr(blob, groupsOffset + g * pointerSize, pointerSize);
            var group = ReadGroup(blob, groupOffset, pointerSize);
            if (group is not null) groups.Add(group);
        }

        return new Gw2GrannyAnimation
        {
            Name = ReadString(blob, animation),
            Duration = duration,
            TimeStep = timeStep,
            Oversampling = oversampling,
            TrackGroups = groups,
        };
    }

    private static Gw2GrannyTrackGroup? ReadGroup(ReadOnlySpan<byte> blob, int offset, int pointerSize)
    {
        if (offset < 0 || offset + 2 * pointerSize + 12 > blob.Length) return null;

        var name = ReadString(blob, ReadPtr(blob, offset, pointerSize));
        var vectorTrackCount = (int)ReadU32(blob, offset + pointerSize);
        var vectorTracksOffset = ReadPtr(blob, offset + pointerSize + 4, pointerSize);
        var trackCount = (int)ReadU32(blob, offset + 2 * pointerSize + 4);
        var tracksOffset = ReadPtr(blob, offset + 2 * pointerSize + 8, pointerSize);
        var trackSize = TransformTrackSize(pointerSize);
        if (trackCount < 0 || (trackCount > 0 && tracksOffset <= 0) || (long)tracksOffset + (long)trackCount * trackSize > blob.Length) return null;
        var vectorTrackSize = VectorTrackSize(pointerSize);
        var vectorTracks = new List<Gw2GrannyVectorTrack>();
        if (vectorTrackCount > 0 && vectorTracksOffset > 0 && (long)vectorTracksOffset + (long)vectorTrackCount * vectorTrackSize <= blob.Length)
            for (var v = 0; v < vectorTrackCount; v++)
                vectorTracks.Add(ReadVectorTrack(blob, vectorTracksOffset + v * vectorTrackSize, pointerSize));

        var tracks = new List<Gw2GrannyTransformTrack>(trackCount);
        for (var t = 0; t < trackCount; t++)
        {
            var track = ReadTrack(blob, tracksOffset + t * trackSize, pointerSize);
            if (track is not null) tracks.Add(track);
        }

        return new Gw2GrannyTrackGroup { Name = name, VectorTracks = vectorTracks, TransformTracks = tracks };
    }

    private static Gw2GrannyVectorTrack ReadVectorTrack(ReadOnlySpan<byte> blob, int offset, int pointerSize)
    {
        var dimension = (int)ReadU32(blob, offset + pointerSize + 4);
        return new Gw2GrannyVectorTrack
        {
            Name = ReadString(blob, ReadPtr(blob, offset, pointerSize)),
            TrackKey = ReadU32(blob, offset + pointerSize),
            Dimension = dimension,
            ValueCurve = ReadCurve(blob, offset + pointerSize + 8, pointerSize, Gw2GrannyCurveRole.Vector, dimension),
        };
    }

    private static int VectorTrackSize(int pointerSize) => 3 * pointerSize + 8;

    private static Gw2GrannyTransformTrack? ReadTrack(ReadOnlySpan<byte> blob, int offset, int pointerSize)
    {
        if (offset < 0 || offset + TransformTrackSize(pointerSize) > blob.Length) return null;

        var curveSize = 2 * pointerSize;
        return new Gw2GrannyTransformTrack
        {
            Name = ReadString(blob, ReadPtr(blob, offset, pointerSize)),
            Flags = ReadU32(blob, offset + pointerSize),
            Orientation = ReadCurve(blob, offset + pointerSize + 4, pointerSize, Gw2GrannyCurveRole.Orientation),
            Position = ReadCurve(blob, offset + pointerSize + 4 + curveSize, pointerSize, Gw2GrannyCurveRole.Position),
            ScaleShear = ReadCurve(blob, offset + pointerSize + 4 + 2 * curveSize, pointerSize, Gw2GrannyCurveRole.ScaleShear),
        };
    }

    private static int TransformTrackSize(int pointerSize) => 7 * pointerSize + 4;

    private static Gw2GrannyCurve ReadCurve(ReadOnlySpan<byte> blob, int offset, int pointerSize, Gw2GrannyCurveRole role, int vectorDimension = 0)
    {
        var typeOffset = ReadPtr(blob, offset, pointerSize);
        var objectOffset = ReadPtr(blob, offset + pointerSize, pointerSize);
        var formatName = ReadTypeName(blob, typeOffset);
        var format = ClassifyFormat(formatName);
        var degree = InBounds(blob, objectOffset + 1) ? blob[objectOffset + 1] : 0;

        var dimension = format == Gw2GrannyCurveFormat.Identity
            ? RoleDimension(role, vectorDimension)
            : DimensionOf(formatName, role, vectorDimension);

        IReadOnlyList<float> constant = Array.Empty<float>();
        float[] knots = [];
        float[] controls = [];

        switch (formatName)
        {
            case "CurveDataHeader_DaIdentity":
            {
                var dim = ReadU16(blob, objectOffset + 2);
                if (dim > 0) dimension = dim;
                break;
            }
            case "CurveDataHeader_DaKeyframes32f" when InBounds(blob, objectOffset + 8):
            {
                var dim = ReadU16(blob, objectOffset + 2);
                var controlCount = (int)ReadU32(blob, objectOffset + 4);
                var controlsOffset = ReadPtr(blob, objectOffset + 8, pointerSize);
                if (dim > 0 && controlCount > 0 && InBounds(blob, controlsOffset + controlCount * 4))
                {
                    dimension = dim;
                    controls = ReadFloats(blob, controlsOffset, controlCount);
                    knots = UniformKnots(controlCount / dim);
                    degree = 1;
                }

                break;
            }
            case "CurveDataHeader_D3Constant32f" when InBounds(blob, objectOffset + 16):
                dimension = 3;
                constant = ReadFloats(blob, objectOffset + 4, 3);
                break;
            case "CurveDataHeader_D4Constant32f" when InBounds(blob, objectOffset + 20):
                dimension = 4;
                constant = ReadFloats(blob, objectOffset + 4, 4);
                break;
            case "CurveDataHeader_DaConstant32f" when InBounds(blob, objectOffset + 12):
            {
                var count = (int)ReadU32(blob, objectOffset + 4);
                var controlsOffset = ReadPtr(blob, objectOffset + 8, pointerSize);
                if (count > 0 && InBounds(blob, controlsOffset + count * 4))
                {
                    dimension = count;
                    constant = ReadFloats(blob, controlsOffset, count);
                }

                break;
            }

            case "CurveDataHeader_DaK32fC32f" when InBounds(blob, objectOffset + 8 + 2 * pointerSize):
            {
                var knotCount = (int)ReadU32(blob, objectOffset + 4);
                var knotsOffset = ReadPtr(blob, objectOffset + 8, pointerSize);
                var controlCount = (int)ReadU32(blob, objectOffset + 8 + pointerSize);
                var controlsOffset = ReadPtr(blob, objectOffset + 12 + pointerSize, pointerSize);
                if (knotCount > 0 && InBounds(blob, knotsOffset + knotCount * 4))
                    knots = ReadFloats(blob, knotsOffset, knotCount);
                if (controlCount > 0 && InBounds(blob, controlsOffset + controlCount * 4))
                    controls = ReadFloats(blob, controlsOffset, controlCount);
                if (knotCount > 0 && controlCount % knotCount == 0) dimension = controlCount / knotCount;
                if (knotCount == 1 && controlCount > 0) constant = controls;
                if (knotCount == 0 && controlCount > 0 && dimension > 0)
                {
                    knots = UniformKnots(controlCount / dimension);
                    degree = 1;
                }

                break;
            }

            case "CurveDataHeader_DaK16uC16u" when CanRead(blob, objectOffset, 0x14):
                ReadScaledCurve(blob, objectOffset, pointerSize, false, out dimension, out knots, out controls);
                break;
            case "CurveDataHeader_DaK8uC8u" when CanRead(blob, objectOffset, 0x14):
                ReadScaledCurve(blob, objectOffset, pointerSize, true, out dimension, out knots, out controls);
                break;
            case "CurveDataHeader_D3K16uC16u" when CanRead(blob, objectOffset, 0x24):
                ReadThreeScaledCurve(blob, objectOffset, pointerSize, false, out knots, out controls);
                dimension = 3;
                break;
            case "CurveDataHeader_D3K8uC8u" when CanRead(blob, objectOffset, 0x24):
                ReadThreeScaledCurve(blob, objectOffset, pointerSize, true, out knots, out controls);
                dimension = 3;
                break;
            case "CurveDataHeader_D4nK16uC15u" when CanRead(blob, objectOffset, 0x10):
                ReadNormalizedQuaternionCurve(blob, objectOffset, pointerSize, false, out knots, out controls);
                dimension = 4;
                break;
            case "CurveDataHeader_D4nK8uC7u" when CanRead(blob, objectOffset, 0x10):
                ReadNormalizedQuaternionCurve(blob, objectOffset, pointerSize, true, out knots, out controls);
                dimension = 4;
                break;
            case "CurveDataHeader_D3I1K16uC16u" when CanRead(blob, objectOffset, 0x24):
                ReadLinearScaledCurve(blob, objectOffset, pointerSize, false, out knots, out controls);
                dimension = 3;
                break;
            case "CurveDataHeader_D3I1K8uC8u" when CanRead(blob, objectOffset, 0x24):
                ReadLinearScaledCurve(blob, objectOffset, pointerSize, true, out knots, out controls);
                dimension = 3;
                break;
        }

        return new Gw2GrannyCurve
        {
            Role = role,
            Format = format,
            FormatName = formatName,
            Degree = degree,
            Dimension = dimension,
            Constant = constant,
            Knots = knots,
            Controls = controls,
        };
    }

    private static void ReadScaledCurve(ReadOnlySpan<byte> blob, int offset, int pointerSize, bool eightBit,
        out int dimension, out float[] knots, out float[] controls)
    {
        dimension = 0;
        knots = [];
        controls = [];

        var oneOverKnotScale = OneOverKnotScale(ReadU16(blob, offset + 0x02));
        var scaleOffsetCount = (int)ReadU32(blob, offset + 0x04);
        var scaleOffsetOffset = ReadPtr(blob, offset + 0x08, pointerSize);
        var knotControlCount = (int)ReadU32(blob, offset + 0x08 + pointerSize);
        var knotControlOffset = ReadPtr(blob, offset + 0x0C + pointerSize, pointerSize);

        if (scaleOffsetCount <= 0 || (scaleOffsetCount & 1) != 0) return;
        dimension = scaleOffsetCount / 2;
        var knotCount = knotControlCount / (dimension + 1);
        if (knotCount <= 0) return;

        var scaleOffsets = ReadFloats(blob, scaleOffsetOffset, scaleOffsetCount);
        if (scaleOffsets.Length < scaleOffsetCount) return;
        var packed = ReadPacked(blob, knotControlOffset, knotControlCount, eightBit);
        if (packed.Length < knotCount * (dimension + 1)) return;

        knots = ReadQuantizedKnots(packed, knotCount, oneOverKnotScale);
        controls = new float[knotCount * dimension];
        for (var k = 0; k < knotCount; k++)
        {
            for (var d = 0; d < dimension; d++)
            {
                controls[k * dimension + d] = scaleOffsets[dimension + d]
                    + scaleOffsets[d] * packed[knotCount + k * dimension + d];
            }
        }
    }

    private static void ReadThreeScaledCurve(ReadOnlySpan<byte> blob, int offset, int pointerSize, bool eightBit,
        out float[] knots, out float[] controls)
    {
        knots = [];
        controls = [];

        var oneOverKnotScale = OneOverKnotScale(ReadU16(blob, offset + 0x02));
        var scales = ReadFloats(blob, offset + 0x04, 3);
        var offsets = ReadFloats(blob, offset + 0x10, 3);
        if (scales.Length < 3 || offsets.Length < 3) return;

        var knotControlCount = (int)ReadU32(blob, offset + 0x1C);
        var knotControlOffset = ReadPtr(blob, offset + 0x20, pointerSize);
        var knotCount = knotControlCount >> 2;
        if (knotCount <= 0) return;

        var packed = ReadPacked(blob, knotControlOffset, knotControlCount, eightBit);
        if (packed.Length < knotControlCount) return;

        knots = ReadQuantizedKnots(packed, knotCount, oneOverKnotScale);
        controls = new float[knotCount * 3];
        for (var k = 0; k < knotCount; k++)
        {
            for (var d = 0; d < 3; d++)
                controls[k * 3 + d] = offsets[d] + scales[d] * packed[knotCount + k * 3 + d];
        }
    }

    private static void ReadNormalizedQuaternionCurve(ReadOnlySpan<byte> blob, int offset, int pointerSize, bool eightBit,
        out float[] knots, out float[] controls)
    {
        knots = [];
        controls = [];

        var tableEntries = ReadU16(blob, offset + 0x02);
        var oneOverKnotScale = ReadF32(blob, offset + 0x04);
        var knotControlCount = (int)ReadU32(blob, offset + 0x08);
        var knotControlOffset = ReadPtr(blob, offset + 0x0C, pointerSize);
        var knotCount = knotControlCount >> 2;
        if (knotCount <= 0) return;

        var packed = ReadPacked(blob, knotControlOffset, knotControlCount, eightBit);
        if (packed.Length < knotControlCount) return;

        var componentBits = eightBit ? 7 : 15;
        var signMask = 1 << componentBits;
        var indexShift1 = componentBits - 1;
        var indexShift2 = componentBits;
        var componentMask = signMask - 1;
        Span<float> scales = stackalloc float[4];
        Span<float> offsets = stackalloc float[4];
        for (var d = 0; d < 4; d++)
        {
            var entry = (tableEntries >> (d * 4)) & 0xF;
            scales[d] = QuaternionScaleOffsetTable[entry * 2] * (1f / componentMask);
            offsets[d] = QuaternionScaleOffsetTable[entry * 2 + 1];
        }

        knots = ReadQuantizedKnots(packed, knotCount, oneOverKnotScale);
        controls = new float[knotCount * 4];
        for (var k = 0; k < knotCount; k++)
        {
            var controlBase = knotCount + k * 3;
            var data0 = (int)packed[controlBase];
            var data1 = (int)packed[controlBase + 1];
            var data2 = (int)packed[controlBase + 2];

            var missingIsNegative = (data0 & signMask) != 0;
            var missingIndex = ((data1 >> indexShift1) & 0x2) | (data2 >> indexShift2);

            var destination = missingIndex;
            var summedSquares = 0f;
            for (var source = 0; source < 3; source++)
            {
                destination = (destination + 1) & 0x3;
                var result = offsets[destination]
                    + scales[destination] * ((int)packed[controlBase + source] & componentMask);
                summedSquares += result * result;
                controls[k * 4 + destination] = result;
            }

            var missing = MathF.Sqrt(MathF.Max(0f, 1f - summedSquares));
            if (missingIsNegative) missing = -missing;
            controls[k * 4 + missingIndex] = missing;
        }
    }

    private static void ReadLinearScaledCurve(ReadOnlySpan<byte> blob, int offset, int pointerSize, bool eightBit,
        out float[] knots, out float[] controls)
    {
        knots = [];
        controls = [];

        var oneOverKnotScale = OneOverKnotScale(ReadU16(blob, offset + 0x02));
        var scales = ReadFloats(blob, offset + 0x04, 3);
        var offsets = ReadFloats(blob, offset + 0x10, 3);
        if (scales.Length < 3 || offsets.Length < 3) return;

        var knotControlCount = (int)ReadU32(blob, offset + 0x1C);
        var knotControlOffset = ReadPtr(blob, offset + 0x20, pointerSize);
        var knotCount = knotControlCount / 2;
        if (knotCount <= 0) return;

        var packed = ReadPacked(blob, knotControlOffset, knotControlCount, eightBit);
        if (packed.Length < knotControlCount) return;

        knots = ReadQuantizedKnots(packed, knotCount, oneOverKnotScale);
        controls = new float[knotCount * 3];
        for (var k = 0; k < knotCount; k++)
        {
            var parameter = packed[knotCount + k];
            for (var d = 0; d < 3; d++) controls[k * 3 + d] = offsets[d] + scales[d] * parameter;
        }
    }

    private static float[] ReadQuantizedKnots(float[] packed, int knotCount, float oneOverKnotScale)
    {
        var knotScale = oneOverKnotScale > 0f && float.IsFinite(oneOverKnotScale) ? 1f / oneOverKnotScale : 0f;
        var knots = new float[knotCount];
        for (var i = 0; i < knotCount; i++)
        {
            var knot = knotScale * packed[i];
            knots[i] = float.IsFinite(knot) ? knot : i;
        }

        return knots;
    }

    private static float[] UniformKnots(int knotCount)
    {
        if (knotCount <= 0) return [];
        var knots = new float[knotCount];
        for (var i = 0; i < knotCount; i++) knots[i] = i;
        return knots;
    }

    private static float OneOverKnotScale(ushort truncated) =>
        BitConverter.Int32BitsToSingle(unchecked((int)((uint)truncated << 16)));

    private static float[] ReadPacked(ReadOnlySpan<byte> blob, int offset, int count, bool eightBit)
    {
        if (count <= 0) return [];
        var elementSize = eightBit ? 1 : 2;
        if (!CanRead(blob, offset, (long)count * elementSize)) return [];
        var values = new float[count];
        if (eightBit)
        {
            for (var i = 0; i < count; i++) values[i] = blob[offset + i];
        }
        else
        {
            for (var i = 0; i < count; i++) values[i] = ReadU16(blob, offset + i * 2);
        }

        return values;
    }

    private static readonly float[] QuaternionScaleOffsetTable =
    [
        0.707106781f * 2.0f, -0.707106781f,
        0.707106781f * 1.0f, -0.707106781f * 0.5f,
        0.707106781f * 0.5f, -0.707106781f * 0.75f,
        0.707106781f * 0.5f, -0.707106781f * 0.25f,
        0.707106781f * 0.5f, 0.707106781f * 0.25f,
        0.707106781f * 0.25f, -0.707106781f * 0.25f,
        0.707106781f * 0.25f, -0.707106781f * 0.125f,
        0.707106781f * 0.25f, 0.0f,
        -0.707106781f * 2.0f, 0.707106781f,
        -0.707106781f * 1.0f, 0.707106781f * 0.5f,
        -0.707106781f * 0.5f, 0.707106781f * 0.75f,
        -0.707106781f * 0.5f, 0.707106781f * 0.25f,
        -0.707106781f * 0.5f, -0.707106781f * 0.25f,
        -0.707106781f * 0.25f, 0.707106781f * 0.25f,
        -0.707106781f * 0.25f, 0.707106781f * 0.125f,
        -0.707106781f * 0.25f, 0.0f,
    ];

    private static bool CanRead(ReadOnlySpan<byte> blob, int offset, long size) =>
        size >= 0 && offset >= 0 && offset <= blob.Length - size;

    private static bool InBounds(ReadOnlySpan<byte> blob, int offset) =>
        offset >= 0 && offset < blob.Length;

    private static int ReadPtr(ReadOnlySpan<byte> blob, int offset, int pointerSize)
    {
        if (pointerSize == 8)
            return CanRead(blob, offset, 8) ? (int)BinaryPrimitives.ReadUInt64LittleEndian(blob.Slice(offset, 8)) : 0;
        return (int)ReadU32(blob, offset);
    }

    private static float[] ReadFloats(ReadOnlySpan<byte> blob, int offset, int count)
    {
        if (count <= 0 || !CanRead(blob, offset, (long)count * 4)) return [];
        var values = new float[count];
        for (var i = 0; i < count; i++) values[i] = ReadF32(blob, offset + i * 4);
        return values;
    }

    private static ushort ReadU16(ReadOnlySpan<byte> blob, int offset) =>
        CanRead(blob, offset, 2) ? BinaryPrimitives.ReadUInt16LittleEndian(blob.Slice(offset, 2)) : (ushort)0;

    private static string ReadTypeName(ReadOnlySpan<byte> blob, int typeOffset)
    {
        if (typeOffset < 0 || !CanRead(blob, typeOffset, 8)) return string.Empty;
        if (ReadU32(blob, typeOffset) == 0) return string.Empty;
        return ReadString(blob, (int)ReadU32(blob, typeOffset + 0x04));
    }

    private static Gw2GrannyCurveFormat ClassifyFormat(string name)
    {
        if (name.Length == 0) return Gw2GrannyCurveFormat.Unknown;
        if (name.Contains("Identity", StringComparison.Ordinal)) return Gw2GrannyCurveFormat.Identity;
        if (name.Contains("Constant", StringComparison.Ordinal)) return Gw2GrannyCurveFormat.Constant;
        if (name.Contains("Keyframes", StringComparison.Ordinal) || name.Contains("K32fC32f", StringComparison.Ordinal)) return Gw2GrannyCurveFormat.Keyframes;
        if (name.Contains("K16u", StringComparison.Ordinal) || name.Contains("K8u", StringComparison.Ordinal)) return Gw2GrannyCurveFormat.Compressed;
        return Gw2GrannyCurveFormat.Unknown;
    }

    private static int DimensionOf(string name, Gw2GrannyCurveRole role, int vectorDimension)
    {
        var marker = name.LastIndexOf('D');
        if (marker >= 0 && marker + 1 < name.Length)
        {
            var next = name[marker + 1];
            if (next is >= '0' and <= '9')
            {
                var value = 0;
                var index = marker + 1;
                while (index < name.Length && name[index] is >= '0' and <= '9')
                {
                    value = value * 10 + (name[index] - '0');
                    index++;
                }

                return value;
            }
        }

        return RoleDimension(role, vectorDimension);
    }

    private static int RoleDimension(Gw2GrannyCurveRole role, int vectorDimension = 0) => role switch
    {
        Gw2GrannyCurveRole.Vector => vectorDimension,
        Gw2GrannyCurveRole.Orientation => 4,
        Gw2GrannyCurveRole.Position => 3,
        _ => 9,
    };

    private static uint ReadU32(ReadOnlySpan<byte> blob, int offset) =>
        CanRead(blob, offset, 4) ? BinaryPrimitives.ReadUInt32LittleEndian(blob.Slice(offset, 4)) : 0u;

    private static float ReadF32(ReadOnlySpan<byte> blob, int offset) =>
        CanRead(blob, offset, 4) ? BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(blob.Slice(offset, 4))) : 0f;

    private static string ReadString(ReadOnlySpan<byte> blob, int offset)
    {
        if (offset <= 0 || offset >= blob.Length) return string.Empty;
        var end = offset;
        while (end < blob.Length && blob[end] != 0) end++;
        return Encoding.ASCII.GetString(blob[offset..end]);
    }
}
