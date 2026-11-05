using System;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using static Gw2.Client.Scene.Havok.HavokVector;

namespace Gw2.Client.Scene.Havok;

[StructLayout(LayoutKind.Sequential)]
public struct HavokTrianglePlanes
{
    public Vector128<float> InverseEdgeAndNormalLengths;
    public float NormalLength;
}

[StructLayout(LayoutKind.Sequential)]
public struct HavokContact
{
    public Vector128<float> Position;
    public Vector128<float> Normal;
}

[StructLayout(LayoutKind.Sequential)]
public struct HavokSegmentClosestPoints
{
    public Vector128<float> PointOnFirst;
    public Vector128<float> Difference;
    public Vector128<float> DistanceSquared;
    public Vector128<float> FirstFraction;
    public Vector128<float> SecondFraction;
}

[StructLayout(LayoutKind.Sequential)]
public struct HavokTransform
{
    public Vector128<float> Column0;
    public Vector128<float> Column1;
    public Vector128<float> Column2;
    public Vector128<float> Translation;

    public static HavokTransform Identity => new()
    {
        Column0 = Vector128.Create(1f, 0f, 0f, 0f),
        Column1 = Vector128.Create(0f, 1f, 0f, 0f),
        Column2 = Vector128.Create(0f, 0f, 1f, 0f),
        Translation = Vector128<float>.Zero,
    };

    public readonly Vector128<float> Apply(Vector128<float> point) =>
        Sse.Add(Sse.Add(Sse.Add(Sse.Multiply(Splat(point, 1), Column1), Sse.Multiply(Splat(point, 0), Column0)), Sse.Multiply(Splat(point, 2), Column2)), Translation);
}

public static class HavokCapsuleTriangle
{
    public const int SegmentFirstAtEnd = 1;
    public const int SegmentFirstAtStart = 2;
    public const int SegmentSecondAtEnd = 4;
    public const int SegmentSecondAtStart = 8;

    private const float ParallelTolerance = Epsilon * 8f;
    private static readonly float InitialDistance = BitConverter.Int32BitsToSingle(0x5F7FFFF0);
    private static readonly float LowestDistance = BitConverter.Int32BitsToSingle(unchecked((int)0xFF7FFFEE));
    private static readonly float EdgeFractionMinimum = BitConverter.Int32BitsToSingle(unchecked((int)0xB8D1B717));
    private static readonly float EdgeFractionMaximum = BitConverter.Int32BitsToSingle(0x3F800347);
    private static readonly float DegenerateLengthSquared = BitConverter.Int32BitsToSingle(0x28800000);
    private static readonly Vector128<float> SignBits = Vector128.Create(-0f);
    private static readonly byte[] EdgeStartVertex = [1, 2, 0];
    private static readonly byte[] EdgeEndVertex = [2, 0, 1];

    public static HavokTrianglePlanes PrepareTriangle(Vector128<float> vertex0, Vector128<float> vertex1, Vector128<float> vertex2)
    {
        var fromTwoToZero = Sse.Subtract(vertex0, vertex2);
        var fromOneToTwo = Sse.Subtract(vertex2, vertex1);
        var fromZeroToOne = Sse.Subtract(vertex1, vertex0);
        var normal = YZX(Sse.Subtract(Sse.Multiply(YZX(fromTwoToZero), fromOneToTwo), Sse.Multiply(YZX(fromOneToTwo), fromTwoToZero)));
        var lengthsSquared = Vector128.Create(
            Dot3Scalar(fromOneToTwo, fromOneToTwo),
            Dot3Scalar(fromTwoToZero, fromTwoToZero),
            Dot3Scalar(fromZeroToOne, fromZeroToOne),
            Dot3Scalar(normal, normal));
        var inverse = InverseSquareRoot(lengthsSquared);
        return new HavokTrianglePlanes { InverseEdgeAndNormalLengths = inverse, NormalLength = inverse.GetElement(3) * lengthsSquared.GetElement(3) };
    }

    public static int SegmentClosest(Vector128<float> firstStart, Vector128<float> firstDirection, Vector128<float> secondStart, Vector128<float> secondDirection, out HavokSegmentClosestPoints closest)
    {
        var between = Sse.Subtract(secondStart, firstStart);
        var directionsDot = Dot3(secondDirection, firstDirection);
        var firstAlongBetween = Dot3(firstDirection, between);
        var directionsDotSquared = Sse.Multiply(directionsDot, directionsDot);
        var secondAlongBetween = Dot3(secondDirection, between);
        var firstLengthSquared = Dot3(firstDirection, firstDirection);
        var secondLengthSquared = Dot3(secondDirection, secondDirection);
        var lengthsProduct = Sse.Multiply(secondLengthSquared, firstLengthSquared);
        var determinant = Absolute(Sse.Subtract(lengthsProduct, directionsDotSquared));
        var numerator = Sse.Subtract(Sse.Multiply(secondLengthSquared, firstAlongBetween), Sse.Multiply(secondAlongBetween, directionsDot));
        int flags;
        Vector128<float> firstFraction;
        Vector128<float> secondFraction;
        if (numerator.ToScalar() * determinant.ToScalar() >= determinant.ToScalar() * determinant.ToScalar())
        {
            flags = SegmentFirstAtEnd;
            firstFraction = Broadcast(1f);
        }
        else if (numerator.ToScalar() <= 0f)
        {
            flags = SegmentFirstAtStart;
            firstFraction = Vector128<float>.Zero;
        }
        else if (determinant.ToScalar() <= (lengthsProduct.ToScalar() + directionsDotSquared.ToScalar()) * ParallelTolerance)
        {
            flags = SegmentFirstAtEnd;
            firstFraction = Broadcast(1f);
        }
        else
        {
            flags = 0;
            firstFraction = Sse.Multiply(numerator, Reciprocal(determinant));
        }

        var secondNumerator = Sse.Subtract(Sse.Multiply(firstFraction, directionsDot), secondAlongBetween);
        var needsFirstAgain = true;
        if (secondNumerator.ToScalar() < secondLengthSquared.ToScalar())
        {
            if (secondNumerator.ToScalar() > 0f)
            {
                secondFraction = Sse.Multiply(secondNumerator, Reciprocal(secondLengthSquared));
                needsFirstAgain = false;
            }
            else
            {
                secondFraction = Vector128<float>.Zero;
                flags = SegmentSecondAtStart;
            }
        }
        else
        {
            secondFraction = Broadcast(1f);
            flags = SegmentSecondAtEnd;
        }

        if (needsFirstAgain)
        {
            firstFraction = Vector128<float>.Zero;
            var firstNumerator = Sse.Add(Sse.Multiply(secondFraction, directionsDot), firstAlongBetween);
            if (firstNumerator.ToScalar() > 0f)
            {
                if (firstNumerator.ToScalar() < firstLengthSquared.ToScalar()) firstFraction = Sse.Multiply(firstNumerator, Reciprocal(firstLengthSquared));
                else
                {
                    firstFraction = Broadcast(1f);
                    flags |= SegmentFirstAtEnd;
                }
            }
            else flags |= SegmentFirstAtStart;
        }

        var pointOnSecond = Sse.Add(Sse.Multiply(secondFraction, secondDirection), secondStart);
        var pointOnFirst = Sse.Add(Sse.Multiply(firstDirection, firstFraction), firstStart);
        var difference = Sse.Subtract(pointOnFirst, pointOnSecond);
        closest = new HavokSegmentClosestPoints
        {
            PointOnFirst = pointOnFirst,
            Difference = difference,
            DistanceSquared = Vector128.CreateScalar(Dot3Scalar(difference, difference)),
            FirstFraction = firstFraction,
            SecondFraction = secondFraction,
        };
        return flags;
    }

    public static void ClosestPoints(ReadOnlySpan<Vector128<float>> capsule, float capsuleRadius, ReadOnlySpan<Vector128<float>> triangle, float triangleRadius, in HavokTrianglePlanes planes, float tolerance, Span<HavokContact> results)
    {
        var inverse = planes.InverseEdgeAndNormalLengths;
        var fromTwoToZero = Sse.Subtract(triangle[0], triangle[2]);
        var fromOneToTwo = Sse.Subtract(triangle[2], triangle[1]);
        var unitEdge0 = Sse.Multiply(fromOneToTwo, Splat(inverse, 0));
        var unnormalisedNormal = Sse.Subtract(Sse.Multiply(YZX(fromTwoToZero), fromOneToTwo), Sse.Multiply(YZX(fromOneToTwo), fromTwoToZero));
        var unitEdge1 = Sse.Multiply(fromTwoToZero, Splat(inverse, 1));
        var normal = Sse.Multiply(Splat(inverse, 3), YZX(unnormalisedNormal));
        var unitEdge2 = Sse.Multiply(Splat(inverse, 2), Sse.Subtract(triangle[1], triangle[0]));
        var normalYzx = YZX(normal);
        var edgeNormal0 = YZX(Sse.Subtract(Sse.Multiply(normalYzx, unitEdge0), Sse.Multiply(YZX(unitEdge0), normal)));
        var edgeNormal1 = YZX(Sse.Subtract(Sse.Multiply(normalYzx, unitEdge1), Sse.Multiply(YZX(unitEdge1), normal)));
        var edgeNormal2 = YZX(Sse.Subtract(Sse.Multiply(unitEdge2, normalYzx), Sse.Multiply(YZX(unitEdge2), normal)));
        var lowEdges01 = Sse.Shuffle(edgeNormal0, edgeNormal1, 68);
        var highEdges01 = Sse.Shuffle(edgeNormal0, edgeNormal1, 238);
        var highEdge2Normal = Sse.Shuffle(edgeNormal2, normal, 238);
        var lowEdge2Normal = Sse.Shuffle(edgeNormal2, normal, 68);
        var rowX = Sse.Shuffle(lowEdges01, lowEdge2Normal, 136);
        var rowY = Sse.Shuffle(lowEdges01, lowEdge2Normal, 221);
        var rowZ = Sse.Shuffle(highEdges01, highEdge2Normal, 136);
        var rowW = Vector128.CreateScalar((0f - inverse.ToScalar()) * planes.NormalLength);

        Span<Vector128<float>> distances = stackalloc Vector128<float>[2];
        for (var index = 1; index >= 0; index--)
        {
            var relative = Sse.Subtract(capsule[index], triangle[0]);
            distances[index] = Sse.Add(Sse.Add(Sse.Add(Sse.Multiply(Splat(relative, 1), rowY), Sse.Multiply(Splat(relative, 0), rowX)), Sse.Multiply(Splat(relative, 2), rowZ)), rowW);
        }
        var distances0 = distances[0];
        var distances1 = distances[1];

        var radiusSumScalar = capsuleRadius + triangleRadius;
        var reach = Broadcast(radiusSumScalar + tolerance);
        var radiusSum = Broadcast(radiusSumScalar);
        var initialDistance = Sse.Subtract(Broadcast(InitialDistance), Sse.Add(radiusSum, radiusSum));
        for (var index = 0; index < 3; index++) results[index].Normal = WithW(results[index].Normal, initialDistance);
        var negativeReach = Sse.Xor(SignBits, reach);
        var outsideAll = Sse.MoveMask(Sse.And(Sse.CompareLessThan(reach, distances0), Sse.CompareLessThan(reach, distances1)));
        var belowFace = Sse.MoveMask(Sse.And(Sse.CompareLessThan(distances1, negativeReach), Sse.CompareLessThan(distances0, negativeReach)));
        if ((outsideAll | (belowFace & 8)) != 0) return;

        Span<int> inside = stackalloc int[2];
        inside[0] = Sse.MoveMask(Sse.CompareLessThan(distances0, Vector128<float>.Zero));
        inside[1] = Sse.MoveMask(Sse.CompareLessThan(distances1, Vector128<float>.Zero));
        if (((inside[0] ^ inside[1]) & 8) != 0)
        {
            var faceDistance1 = Splat(distances1, 3);
            var faceDistance0 = Splat(distances0, 3);
            var change = Sse.Subtract(distances1, distances0);
            var faceSpan = Sse.Subtract(faceDistance0, faceDistance1);
            var estimate = Sse.Reciprocal(faceSpan);
            var crossing = Sse.Add(Sse.Multiply(change, Sse.Multiply(Sse.Multiply(Sse.Subtract(Broadcast(2f), Sse.Multiply(estimate, faceSpan)), estimate), faceDistance0)), distances0);
            if ((Sse.MoveMask(Sse.CompareLessThan(crossing, Vector128<float>.Zero)) & 7) == 7)
            {
                PiercingContact(capsule, triangleRadius, normal, rowX, rowY, rowZ, distances0, distances, inside, change, faceDistance0, radiusSum, ref results[1]);
                return;
            }
        }

        var capsuleDirection = Sse.Subtract(capsule[1], capsule[0]);
        var combined = false;
        var firstPass = false;
        var endpoint = 0;
        var savedEndpoint = 0;
        var slot = 0;
        var featureEndpoint = 0;
        if (((inside[0] | inside[1]) & 7) == 7) goto PerEndpoint;
        combined = true;
        firstPass = true;

    Edges:
        featureEndpoint = endpoint;
        var endpointBit = 1 << endpoint;
        for (int edge = 0, edgeBit = 1; edge < 3; edge++, edgeBit *= 2)
        {
            var consider = combined ? (inside[0] & inside[1] & edgeBit) == 0 : (edgeBit & inside[featureEndpoint]) == 0;
            if (!consider) continue;
            var start = triangle[EdgeStartVertex[edge]];
            var edgeDirection = Sse.Subtract(triangle[EdgeEndVertex[edge]], start);
            var flags = SegmentClosest(capsule[0], capsuleDirection, start, edgeDirection, out var closest);
            var limit = results[slot].Normal.GetElement(3) + radiusSumScalar;
            var write = false;
            if (limit * limit > closest.DistanceSquared.ToScalar())
            {
                if ((flags & endpointBit) == 0)
                {
                    endpoint = savedEndpoint;
                    write = true;
                }
                else if (firstPass)
                {
                    endpoint = 1;
                    savedEndpoint = 1;
                    endpointBit = 2;
                    featureEndpoint = 1;
                    write = true;
                }
                else endpoint = savedEndpoint;
            }
            else endpoint = savedEndpoint;
            if (write) results[slot] = EdgeContact(flags, closest, capsuleDirection, edgeDirection, rowX, rowY, rowZ, edge, triangleRadius, radiusSum);
            combined = firstPass;
        }
        if (combined) return;
        slot++;
        savedEndpoint = ++endpoint;

    PerEndpoint:
        while (endpoint < 2)
        {
            var mask = inside[endpoint];
            if ((mask & 7) != 7) goto Edges;
            var faceDistance = Splat(distances[endpoint], 3);
            if ((mask & 8) != 0)
            {
                var depth = Sse.Subtract(Sse.Subtract(Vector128<float>.Zero, Broadcast(triangleRadius)), faceDistance);
                var flipped = Sse.Xor(normal, SignBits);
                results[slot].Position = Sse.Add(Sse.Multiply(depth, normal), capsule[endpoint]);
                results[slot].Normal = WithW(flipped, Sse.Subtract(Sse.Subtract(Vector128<float>.Zero, faceDistance), radiusSum));
            }
            else
            {
                results[slot].Position = Sse.Add(Sse.Multiply(Sse.Subtract(Broadcast(triangleRadius), faceDistance), normal), capsule[endpoint]);
                results[slot].Normal = WithW(normal, Sse.Subtract(faceDistance, radiusSum));
            }
            slot++;
            savedEndpoint = ++endpoint;
        }
    }

    private static HavokContact EdgeContact(int flags, in HavokSegmentClosestPoints closest, Vector128<float> capsuleDirection, Vector128<float> edgeDirection, Vector128<float> rowX, Vector128<float> rowY, Vector128<float> rowZ, int edge, float triangleRadius, Vector128<float> radiusSum)
    {
        Vector128<float> reference;
        Vector128<float> axis;
        if (flags == 0)
        {
            reference = closest.Difference;
            axis = Cross(capsuleDirection, edgeDirection);
        }
        else if (DegenerateLengthSquared < closest.DistanceSquared.ToScalar())
        {
            reference = closest.Difference;
            axis = reference;
        }
        else
        {
            reference = Vector128.Create(rowX.GetElement(edge), rowY.GetElement(edge), rowZ.GetElement(edge), 0f);
            axis = Cross(capsuleDirection, edgeDirection);
            if (!(DegenerateLengthSquared < Dot3Scalar(axis, axis))) axis = reference;
        }
        var lengthSquared = Dot3(axis, axis);
        var estimate = Sse.ReciprocalSqrt(lengthSquared);
        var inverseLength = Sse.Multiply(Sse.Subtract(Broadcast(3f), Sse.Multiply(Sse.Multiply(lengthSquared, estimate), estimate)), Sse.Multiply(estimate, Broadcast(0.5f)));
        var direction = Sse.Multiply(axis, Sse.AndNot(Sse.CompareLessThanOrEqual(lengthSquared, Vector128<float>.Zero), inverseLength));
        var signedDistance = Dot3(reference, direction);
        var sign = Sse2.ShiftLeftLogical(Sse2.ShiftRightLogical(signedDistance.AsUInt32(), 31), 31).AsSingle();
        var outward = Sse.Xor(direction, sign);
        var distance = Sse.Xor(signedDistance, sign);
        return new HavokContact
        {
            Normal = WithW(outward, Sse.Subtract(distance, radiusSum)),
            Position = Sse.Add(Sse.Multiply(Sse.Subtract(Broadcast(triangleRadius), distance), outward), closest.PointOnFirst),
        };
    }

    private static void PiercingContact(ReadOnlySpan<Vector128<float>> capsule, float triangleRadius, Vector128<float> normal, Vector128<float> rowX, Vector128<float> rowY, Vector128<float> rowZ,
        Vector128<float> distances0, ReadOnlySpan<Vector128<float>> distances, ReadOnlySpan<int> inside, Vector128<float> change, Vector128<float> faceDistance0, Vector128<float> radiusSum, ref HavokContact result)
    {
        result.Normal = WithW(result.Normal, Broadcast(LowestDistance));
        for (var index = 0; index < 2; index++)
        {
            if ((inside[index] & 7) != 7) continue;
            var faceDistance = Splat(distances[index], 3);
            var depth = Sse.Subtract(Sse.Subtract(Vector128<float>.Zero, Absolute(faceDistance)), radiusSum);
            if (!(depth.ToScalar() > result.Normal.GetElement(3))) continue;
            var triangleRadiusVector = Broadcast(triangleRadius);
            if ((inside[index] & 8) != 0)
            {
                result.Normal = normal;
                result.Position = Sse.Add(Sse.Multiply(Sse.Subtract(triangleRadiusVector, faceDistance), normal), capsule[index]);
            }
            else
            {
                result.Position = Sse.Add(Sse.Multiply(Sse.Subtract(Sse.Subtract(Vector128<float>.Zero, triangleRadiusVector), faceDistance), normal), capsule[index]);
                result.Normal = Sse.Xor(SignBits, normal);
            }
            result.Normal = WithW(result.Normal, depth);
        }

        var faceChange = Splat(change, 3);
        var faceChangeSquared = Sse.Multiply(faceChange, faceChange);
        for (var edge = 0; edge < 3; edge++)
        {
            var edgeChange = Splat(change, (byte)edge);
            var lengthSquared = Sse.Add(Sse.Multiply(edgeChange, edgeChange), faceChangeSquared);
            var estimate = Sse.Reciprocal(lengthSquared);
            var inverseLengthSquared = Sse.Multiply(Sse.Subtract(Broadcast(2f), Sse.Multiply(lengthSquared, estimate)), estimate);
            var edgeDistance0 = Splat(distances0, (byte)edge);
            var limit = result.Normal.GetElement(3) + radiusSum.ToScalar();
            var cross = Sse.Subtract(Sse.Multiply(faceDistance0, edgeChange), Sse.Multiply(edgeDistance0, faceChange));
            var separationSquared = Sse.Multiply(Sse.Multiply(cross, cross), inverseLengthSquared);
            if (!(limit * limit > separationSquared.ToScalar())) continue;
            var fraction = Sse.Multiply(Sse.Add(Sse.Multiply(edgeDistance0, edgeChange), Sse.Multiply(faceDistance0, faceChange)), Sse.Subtract(Vector128<float>.Zero, inverseLengthSquared));
            if (!(fraction.ToScalar() > EdgeFractionMinimum && EdgeFractionMaximum > fraction.ToScalar())) continue;
            var sign = Sse2.ShiftLeftLogical(Sse2.ShiftRightLogical(Sse.CompareLessThan(faceChange, Vector128<float>.Zero).AsUInt32(), 31), 31).AsSingle();
            var edgeNormal = Vector128.Create(rowX.GetElement(edge), rowY.GetElement(edge), rowZ.GetElement(edge), 0f);
            var axis = Sse.Add(Sse.Multiply(Sse.Subtract(Vector128<float>.Zero, Sse.Xor(edgeChange, sign)), normal), Sse.Multiply(edgeNormal, Sse.Xor(faceChange, sign)));
            var axisLengthSquared = Dot3(axis, axis);
            var axisEstimate = Sse.ReciprocalSqrt(axisLengthSquared);
            var direction = Sse.Multiply(Sse.AndNot(Sse.CompareLessThanOrEqual(axisLengthSquared, Vector128<float>.Zero),
                Sse.Multiply(Sse.Subtract(Broadcast(3f), Sse.Multiply(Sse.Multiply(axisLengthSquared, axisEstimate), axisEstimate)), Sse.Multiply(axisEstimate, Broadcast(0.5f)))), axis);
            var separationEstimate = Sse.ReciprocalSqrt(separationSquared);
            var inverseSeparation = Sse.Multiply(Sse.Subtract(Broadcast(3f), Sse.Multiply(Sse.Multiply(separationSquared, separationEstimate), separationEstimate)), Sse.Multiply(separationEstimate, Broadcast(0.5f)));
            var negativeSeparation = Sse.Subtract(Vector128<float>.Zero, Sse.AndNot(Sse.CompareLessThanOrEqual(separationSquared, Vector128<float>.Zero), Sse.Multiply(inverseSeparation, separationSquared)));
            result.Position = Sse.Add(Sse.Multiply(Sse.Subtract(Broadcast(triangleRadius), negativeSeparation), direction),
                Sse.Add(Sse.Multiply(Sse.Subtract(capsule[1], capsule[0]), fraction), capsule[0]));
            result.Normal = WithW(direction, Sse.Subtract(negativeSeparation, radiusSum));
        }
    }

    private static Vector128<float> WithW(Vector128<float> xyz, Vector128<float> w) => Sse.Shuffle(xyz, Sse.UnpackHigh(xyz, w), 196);
}
