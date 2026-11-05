using System;
using System.Numerics;

namespace Gw2.Client.Scene;

public readonly record struct Gw2BoneDifference(int Bone, float Distance, float AngleRadians);

public static class Gw2PoseDifference
{
    public static Gw2BoneDifference[] Compare(ReadOnlySpan<Matrix4x4> expected, ReadOnlySpan<Matrix4x4> actual, ReadOnlySpan<Vector3> probePoints)
    {
        var count = Math.Min(expected.Length, actual.Length);
        var differences = new Gw2BoneDifference[count];
        for (var bone = 0; bone < count; bone++)
        {
            var distance = 0f;
            foreach (var point in probePoints)
                distance = MathF.Max(distance, Vector3.Distance(Vector3.Transform(point, expected[bone]), Vector3.Transform(point, actual[bone])));
            differences[bone] = new Gw2BoneDifference(bone, distance, RotationAngle(expected[bone], actual[bone]));
        }
        return differences;
    }

    public static Gw2BoneDifference[] CompareWithBindPose(ReadOnlySpan<Matrix4x4> palette, ReadOnlySpan<Vector3> probePoints)
    {
        if (palette.IsEmpty) return [];
        var bindPose = new Matrix4x4[palette.Length];
        Array.Fill(bindPose, palette[0]);
        return Compare(bindPose, palette, probePoints);
    }

    public static Gw2BoneDifference Largest(ReadOnlySpan<Gw2BoneDifference> differences)
    {
        var largest = default(Gw2BoneDifference);
        foreach (var difference in differences)
            if (difference.Distance > largest.Distance) largest = difference;
        return largest;
    }

    private static float RotationAngle(Matrix4x4 expected, Matrix4x4 actual)
    {
        if (!Matrix4x4.Decompose(expected, out _, out var expectedRotation, out _)) return float.NaN;
        if (!Matrix4x4.Decompose(actual, out _, out var actualRotation, out _)) return float.NaN;
        var dot = MathF.Min(1f, MathF.Abs(Quaternion.Dot(Quaternion.Normalize(expectedRotation), Quaternion.Normalize(actualRotation))));
        return 2f * MathF.Acos(dot);
    }
}
