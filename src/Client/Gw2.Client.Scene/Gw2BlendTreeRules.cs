using System.Numerics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Gw2.Dat.Content;

namespace Gw2.Client.Scene;

internal static class Gw2BlendTreeRules
{
    internal const float NarrowestComparisonRange = 0.0001f;

    internal static float? ComparisonValue(float parameterValue, float rangeMinimum, float rangeMaximum)
    {
        float width = rangeMaximum - rangeMinimum;
        if (width < NarrowestComparisonRange) return null;
        return Math.Clamp((parameterValue - rangeMinimum) / width, 0f, 1f);
    }

    internal static bool Compares(Gw2BlendComparisonKind comparison, float value, float threshold) => comparison switch
    {
        Gw2BlendComparisonKind.Equal => value == threshold,
        Gw2BlendComparisonKind.Greater => value > threshold,
        Gw2BlendComparisonKind.GreaterOrEqual => value >= threshold,
        Gw2BlendComparisonKind.Less => value < threshold,
        Gw2BlendComparisonKind.LessOrEqual => value <= threshold,
        _ => throw new ArgumentOutOfRangeException(nameof(comparison)),
    };

    internal static float ComparisonBlendTime(float opBlendTime, float treeBlendTime) => opBlendTime < 0 ? treeBlendTime : opBlendTime;

    internal const uint DirectionVectorKinds = 3;

    internal static float Heading(Vector3 vector)
    {
        float lengthSquared = vector.X * vector.X + vector.Y * vector.Y;
        if (lengthSquared < 1e-12f) return 0f;
        float length = MathF.Sqrt(lengthSquared);
        return MathF.Atan2(vector.Y / length, vector.X / length);
    }

    internal static int DirectionIndex(float measuredHeading, float referenceHeading)
    {
        float difference = measuredHeading - referenceHeading;
        float rounding = difference <= 0f ? -0.5f : 0.5f;
        return (int)(rounding + difference / MathF.PI * 4f) & 7;
    }

    internal const uint SelectorMeasuresVectorLength = 0;

    internal static int SelectorChildIndex(uint parameter, bool vectorIsActive, float vectorLength, float lengthThreshold)
    {
        float measured = parameter == SelectorMeasuresVectorLength && vectorIsActive ? vectorLength : 0f;
        return measured > lengthThreshold ? 1 : 0;
    }
}
