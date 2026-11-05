using System;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Gw2.Client.Scene.Havok;

public sealed class HavokClosestRayHit
{
    public float Fraction { get; private set; } = 1f;
    public Vector128<float> Normal { get; private set; }
    public int Key { get; private set; } = -1;
    public bool HasHit { get; private set; }

    public void Reset(float earlyOutFraction = 1f)
    {
        Fraction = earlyOutFraction;
        Normal = default;
        Key = -1;
        HasHit = false;
    }

    public void AddHit(float fraction, Vector128<float> normal, int key)
    {
        if (!(fraction < Fraction)) return;
        Fraction = fraction;
        Normal = normal;
        Key = key;
        HasHit = true;
    }
}

public static class HavokHeightFieldRay
{
    private const int X = 0;
    private const int Diagonal = 1;
    private const int Z = 2;
    private const float SmallestDelta = 1f / 32768f;

    private static readonly float LargestFloat = float.MaxValue;
    private static readonly Vector128<float> AbsoluteMask = Vector128.Create(0x7FFFFFFF).AsSingle();
    private static readonly Vector128<float> IntegerThreshold = Vector128.Create(8388608f);

    private static float Absolute(float value) => Sse.And(Vector128.CreateScalar(value), AbsoluteMask).ToScalar();

    private static int FloorToInt(float value)
    {
        var lane = Vector128.CreateScalar(value);
        var rounded = Sse.Subtract(Sse.Add(Sse.Add(Sse.Subtract(lane, IntegerThreshold), IntegerThreshold), IntegerThreshold), IntegerThreshold);
        var tooLarge = Sse.CompareLessThan(IntegerThreshold, Sse.And(lane, AbsoluteMask));
        var floored = Sse.Add(Sse2.ConvertToVector128Single(Sse.CompareLessThan(lane, rounded).AsInt32()), rounded);
        var result = Sse.Or(Sse.AndNot(tooLarge, floored), Sse.And(tooLarge, lane));
        return Sse.ConvertToInt32WithTruncation(result);
    }

    private static Vector128<float> Lerp(float fraction, Vector128<float> from, Vector128<float> to) =>
        Sse.Add(Sse.Multiply(Vector128.Create(fraction), Sse.Subtract(to, from)), from);

    private static Vector128<float> Normalize(Vector128<float> value)
    {
        var squared = Sse.Multiply(value, value);
        var lengthSquared = Sse.Add(Sse.Add(Sse.Shuffle(squared, squared, 0x55), Sse.Shuffle(squared, squared, 0x00)), Sse.Shuffle(squared, squared, 0xAA));
        var estimate = Sse.ReciprocalSqrt(lengthSquared);
        var refined = Sse.Multiply(
            Sse.Subtract(Vector128.Create(3f), Sse.Multiply(Sse.Multiply(lengthSquared, estimate), estimate)),
            Sse.Multiply(estimate, Vector128.Create(0.5f)));
        return Sse.Multiply(value, Sse.AndNot(Sse.CompareLessThanOrEqual(lengthSquared, Vector128<float>.Zero), refined));
    }

    private static void Report(HavokTerrainHeightField field, HavokClosestRayHit collector, float fraction, float normalX, float normalZ, int key)
    {
        var normal = Normalize(Sse.Multiply(Vector128.Create(normalX, 1f, normalZ, 0f), field.InverseScale));
        collector.AddHit(fraction, normal, key);
    }

    public static void CastRay(HavokTerrainHeightField field, Vector128<float> from, Vector128<float> to, HavokClosestRayHit collector)
    {
        var inverseScale = field.InverseScale;
        var scaledFrom = Sse.Multiply(inverseScale, from);
        var scaledTo = Sse.Multiply(to, inverseScale);
        var startCells = Sse2.ShiftRightArithmetic(Sse2.ShiftLeftLogical(Sse2.ConvertToVector128Int32WithTruncation(Sse.Multiply(Sse.Add(field.Offset, from), inverseScale)), 16), 16);

        var fromX = scaledFrom.GetElement(0);
        var toX = scaledTo.GetElement(0);
        var fromHeight = scaledFrom.GetElement(1);
        var toHeight = scaledTo.GetElement(1);
        var fromZ = scaledFrom.GetElement(2);
        var toZ = scaledTo.GetElement(2);
        var fromDiagonal = fromX + fromZ;
        var start = Vector128.Create(fromX, fromDiagonal, fromZ, fromHeight);
        var end = Vector128.Create(toX, toX + toZ, toZ, toHeight);

        var cell = new int[3];
        var step = new int[3];
        var next = new float[3];
        var stepFraction = new float[3];
        var inverseDelta = new float[3];
        var absoluteDelta = new float[3];
        cell[X] = startCells.GetElement(0);
        cell[Z] = startCells.GetElement(2);
        for (var axis = 0; axis < 3; axis++)
        {
            var origin = start.GetElement(axis);
            var delta = end.GetElement(axis) - origin;
            absoluteDelta[axis] = Absolute(delta);
            if (absoluteDelta[axis] >= SmallestDelta)
            {
                var inverse = 1f / delta;
                inverseDelta[axis] = inverse;
                if (delta >= 0f) { stepFraction[axis] = inverse; step[axis] = 1; }
                else { step[axis] = -1; stepFraction[axis] = -inverse; }
                if (origin * delta > 0f) cell[axis] += step[axis];
                next[axis] = ((float)cell[axis] - origin) * inverse;
            }
            else
            {
                stepFraction[axis] = 0f;
                step[axis] = -1;
                next[axis] = LargestFloat;
            }
        }

        var resolutionX = (uint)field.ResolutionX;
        var resolutionZ = field.ResolutionZ;
        if (stepFraction[X] + stepFraction[Z] == 0f || stepFraction[Diagonal] + stepFraction[X] == 0f || stepFraction[Diagonal] + stepFraction[Z] == 0f)
        {
            CastVertical(field, collector, cell[X], cell[Z], fromX, fromZ, fromHeight, toHeight);
            return;
        }

        var startCellX = cell[X];
        var point = start;
        var limitX = (float)(int)(resolutionX - 1);
        float shift;
        if (step[X] <= 0)
        {
            if (toX > limitX || fromX < 0f) return;
            if (cell[X] > (int)(resolutionX - 2))
            {
                cell[X] = (int)(resolutionX - 2);
                shift = (fromX - limitX) * stepFraction[X];
                ClampAlongZ();
            }
        }
        else
        {
            if (toX < 0f || fromX > limitX) return;
            if (cell[X] <= 0)
            {
                cell[X] = 1;
                shift = -(stepFraction[X] * fromX);
                ClampAlongZ();
            }
        }

        void ClampAlongZ()
        {
            next[X] = stepFraction[X] + shift;
            point = Lerp(shift, start, end);
            var crossed = step[Z] * FloorToInt(absoluteDelta[Z] * shift) + cell[Z] - 2;
            var pointCell = FloorToInt(point.GetElement(2));
            if (pointCell >= crossed) crossed = pointCell + 1;
            cell[Z] = crossed + (step[Z] >> 1);
            if (stepFraction[Z] != 0f) next[Z] = ((float)cell[Z] - fromZ) * inverseDelta[Z];
        }

        var limitZ = (float)(resolutionZ - 1);
        if (step[Z] <= 0)
        {
            if (toZ > limitZ || fromZ < 0f) return;
            if (cell[Z] > resolutionZ - 2)
            {
                cell[Z] = resolutionZ - 2;
                shift = (fromZ - limitZ) * stepFraction[Z];
                ClampAlongX();
            }
        }
        else
        {
            if (toZ < 0f || fromZ > limitZ) return;
            if (cell[Z] <= 0)
            {
                cell[Z] = 1;
                shift = -(stepFraction[Z] * fromZ);
                ClampAlongX();
            }
        }

        void ClampAlongX()
        {
            next[Z] = stepFraction[Z] + shift;
            point = Lerp(shift, start, end);
            var crossed = step[X] * FloorToInt(absoluteDelta[X] * shift) + startCellX - 2;
            var pointCell = FloorToInt(point.GetElement(0));
            if (pointCell >= crossed) crossed = pointCell + 1;
            cell[X] = crossed + (step[X] >> 1);
            if (stepFraction[X] != 0f) next[X] = ((float)cell[X] - fromX) * inverseDelta[X];
        }

        if ((uint)cell[X] >= resolutionX || (uint)(cell[X] - step[X]) >= resolutionX) return;
        var diagonalAgainstCells = (step[X] ^ step[Z]) >= 0 && stepFraction[Diagonal] != 0f;
        var diagonalPending = true;
        var diagonalCell = cell[Z] + cell[X];
        var diagonalOffset = point.GetElement(1) - (float)diagonalCell;
        if (diagonalAgainstCells)
        {
            if (Absolute(diagonalOffset) <= 1f) diagonalPending = false;
            else diagonalCell -= step[Diagonal];
        }
        else if ((float)step[Diagonal] * diagonalOffset > 0f)
        {
            diagonalPending = false;
            diagonalCell += step[Diagonal];
        }
        cell[Diagonal] = diagonalCell;
        next[Diagonal] = stepFraction[Diagonal] == 0f ? LargestFloat : (((float)diagonalCell - fromDiagonal) * stepFraction[Diagonal]) * (float)step[Diagonal];
        var previousX = stepFraction[X] == 0f ? -LargestFloat : next[X] - stepFraction[X];
        var previousDiagonal = stepFraction[Diagonal] == 0f ? -LargestFloat : next[Diagonal] - stepFraction[Diagonal];
        var previousZ = stepFraction[Z] != 0f ? next[Z] - stepFraction[Z] : -LargestFloat;

        var mostlyAlongX = absoluteDelta[X] >= absoluteDelta[Z];
        int ChooseLatestCrossed()
        {
            if (diagonalAgainstCells) return diagonalPending ? (previousX > previousZ ? X : Z) : Diagonal;
            if (mostlyAlongX) return diagonalPending ? X : (previousDiagonal <= previousZ ? Z : Diagonal);
            return diagonalPending ? Z : (previousX <= previousDiagonal ? Diagonal : X);
        }

        int ChooseNextCrossing()
        {
            if (diagonalAgainstCells) return diagonalPending ? (next[Z] > next[X] ? X : Z) : Diagonal;
            if (mostlyAlongX) return diagonalPending ? X : (next[Z] <= next[Diagonal] ? Z : Diagonal);
            return diagonalPending ? Z : (next[Diagonal] <= next[X] ? Diagonal : X);
        }

        var axisToCross = ChooseLatestCrossed();
        var lastFraction = -1f;
        var lastPointHeight = 0f;
        var lastSurfaceHeight = LargestFloat;
        next[axisToCross] -= stepFraction[axisToCross];
        cell[axisToCross] -= step[axisToCross];
        if ((uint)cell[X] >= resolutionX) return;

        while (true)
        {
            var cellX = cell[X];
            var cellZ = cell[Z];
            if ((uint)cellZ >= (uint)resolutionZ || lastFraction > collector.Fraction) return;
            var previousCellX = cellX - step[X];
            var crossing = next[axisToCross];
            var crossingPoint = Lerp(crossing, start, end);
            float surfaceHeight;
            if (axisToCross == X)
            {
                var behind = field.HeightAt(cellX, cellZ - step[Z]);
                var here = field.HeightAt(cellX, cellZ);
                surfaceHeight = (behind - here) * Absolute(crossingPoint.GetElement(2) - (float)cellZ) + here;
            }
            else if (axisToCross == Z)
            {
                var behind = field.HeightAt(previousCellX, cellZ);
                var here = field.HeightAt(cellX, cellZ);
                surfaceHeight = (behind - here) * Absolute(crossingPoint.GetElement(0) - (float)cellX) + here;
            }
            else
            {
                var across = Absolute(crossingPoint.GetElement(0) - (float)cellX);
                var behindZ = cellZ;
                var hereZ = cellZ;
                if (diagonalAgainstCells) hereZ = cellZ - step[Z];
                else behindZ = cellZ - step[Z];
                var behind = field.HeightAt(previousCellX, behindZ);
                var here = field.HeightAt(cellX, hereZ);
                surfaceHeight = (behind - here) * across + here;
            }

            var pointHeight = crossingPoint.GetElement(3);
            if (pointHeight >= surfaceHeight || lastPointHeight < lastSurfaceHeight)
            {
                lastFraction = crossing;
            }
            else
            {
                var belowBy = pointHeight - surfaceHeight;
                var hit = true;
                float hitFraction = 0f;
                if (lastFraction < 0f)
                {
                    var blend = crossing / (crossing - lastFraction);
                    var aboveAtStart = fromHeight - ((1f - blend) * surfaceHeight + blend * lastSurfaceHeight);
                    if (aboveAtStart < 0f) hit = false;
                    else hitFraction = aboveAtStart / (aboveAtStart - belowBy) * crossing;
                }
                else
                {
                    var aboveBefore = lastPointHeight - lastSurfaceHeight;
                    hitFraction = aboveBefore / (aboveBefore - belowBy) * (crossing - lastFraction) + lastFraction;
                }

                lastFraction = crossing;
                if (hit)
                {
                    if (hitFraction > collector.Fraction) return;
                    var previousCellZ = cellZ - step[Z];
                    float slopeX, slopeZ;
                    bool upperTriangle;
                    if (diagonalAgainstCells)
                    {
                        var cornerBehindZ = field.HeightAt(cellX, previousCellZ);
                        var cornerBehindX = field.HeightAt(previousCellX, cellZ);
                        if (axisToCross != Diagonal)
                        {
                            var here = field.HeightAt(cellX, cellZ);
                            slopeX = cornerBehindX - here;
                            slopeZ = cornerBehindZ - here;
                            upperTriangle = cellZ > previousCellZ;
                        }
                        else
                        {
                            var farCorner = field.HeightAt(previousCellX, previousCellZ);
                            slopeZ = farCorner - cornerBehindX;
                            slopeX = farCorner - cornerBehindZ;
                            upperTriangle = previousCellZ > cellZ;
                        }
                    }
                    else
                    {
                        var farCorner = field.HeightAt(previousCellX, previousCellZ);
                        var here = field.HeightAt(cellX, cellZ);
                        if (axisToCross != X && (axisToCross != Diagonal || mostlyAlongX))
                        {
                            var cornerBehindX = field.HeightAt(previousCellX, cellZ);
                            slopeZ = farCorner - cornerBehindX;
                            slopeX = cornerBehindX - here;
                            upperTriangle = cellZ > previousCellZ;
                        }
                        else
                        {
                            var cornerBehindZ = field.HeightAt(cellX, previousCellZ);
                            slopeX = farCorner - cornerBehindZ;
                            slopeZ = cornerBehindZ - here;
                            upperTriangle = previousCellZ > cellZ;
                        }
                    }
                    var key = (upperTriangle ? 1 : 0) - 65538 + 2 * (cellX + ((cellZ - (step[Z] >> 1)) << 15) - (step[X] >> 1));
                    Report(field, collector, hitFraction, slopeX * (float)step[X], slopeZ * (float)step[Z], key);
                }
            }

            next[axisToCross] = crossing + stepFraction[axisToCross];
            cell[axisToCross] += step[axisToCross];
            diagonalPending = !diagonalPending;
            axisToCross = ChooseNextCrossing();
            lastSurfaceHeight = surfaceHeight;
            lastPointHeight = pointHeight;
            if ((uint)cell[X] >= resolutionX) return;
        }
    }

    private static void CastVertical(HavokTerrainHeightField field, HavokClosestRayHit collector, int cellX, int cellZ, float fromX, float fromZ, float fromHeight, float toHeight)
    {
        if ((uint)cellX >= (uint)(field.ResolutionX - 1)) return;
        if ((uint)cellZ >= (uint)(field.ResolutionZ - 1)) return;
        var alongZ = fromZ - (float)cellZ;
        var alongX = fromX - (float)cellX;
        var cornerX = field.HeightAt(cellX + 1, cellZ);
        var cornerZ = field.HeightAt(cellX, cellZ + 1);
        float slopeX, slopeZ, surfaceHeight;
        int triangle;
        if (alongX + alongZ > 1f)
        {
            var far = field.HeightAt(cellX + 1, cellZ + 1);
            var riseZ = far - cornerX;
            var riseX = far - cornerZ;
            slopeX = -riseX;
            slopeZ = -riseZ;
            surfaceHeight = (riseZ * alongZ + cornerX) + (alongX - 1f) * riseX;
            triangle = 1;
        }
        else
        {
            var origin = field.HeightAt(cellX, cellZ);
            var riseZ = cornerZ - origin;
            var riseX = cornerX - origin;
            slopeZ = -riseZ;
            slopeX = -riseX;
            surfaceHeight = (riseZ * alongZ + origin) + riseX * alongX;
            triangle = 0;
        }
        var startAbove = fromHeight - surfaceHeight;
        var endAbove = toHeight - surfaceHeight;
        if (endAbove > startAbove || startAbove < 0f || endAbove >= 0f) return;
        var fraction = startAbove / (startAbove - endAbove);
        if (!(fraction < collector.Fraction)) return;
        Report(field, collector, fraction, slopeX, slopeZ, triangle + 2 * (cellX + (cellZ << 15)));
    }
}
