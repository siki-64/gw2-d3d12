using System;
using System.Collections.Generic;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using static Gw2.Client.Scene.Havok.HavokVector;

namespace Gw2.Client.Scene.Havok;

public delegate float HavokHeightSampler(int x, int z);

public sealed class HavokTerrainHeightField
{
    public const float TriangleRadius = 0.05f;

    private static readonly float LargestFloat = BitConverter.Int32BitsToSingle(0x7F7FFFEE);
    private static readonly float LowestFloat = BitConverter.Int32BitsToSingle(unchecked((int)0xFF7FFFEE));
    private static readonly float CellIndexLimit = BitConverter.Int32BitsToSingle(0x5F7FFFF0);
    private static readonly Vector128<float> OffsetLaneMask = Vector128.Create(-1, 0, -1, -1).AsSingle();
    private static readonly Vector128<float> ClipLaneMask = Vector128.Create(-1, 0, -1, 0).AsSingle();
    private static readonly Vector128<float> SignBits = Vector128.Create(-0f);
    private static readonly float RoundingOffset = ComputeRoundingOffset();

    private readonly HavokHeightSampler _sampleHeight;
    private float _minHeight;
    private float _maxHeight;
    private float _heightCenter;
    private Vector128<float> _extent;

    public HavokTerrainHeightField(int resolutionX, int resolutionZ, Vector128<float> scale, HavokHeightSampler sampleHeight)
    {
        ResolutionX = resolutionX;
        ResolutionZ = resolutionZ;
        Scale = scale;
        _sampleHeight = sampleHeight;
        _extent = Sse.Multiply(Vector128.Create((float)resolutionX - 1f, -1f - 0f, (float)resolutionZ - 1f, 0f), scale);
        _minHeight = LowestFloat;
        _maxHeight = LargestFloat;
        _heightCenter = -1f;
        _extent = _extent.WithElement(1, -1f);
        var scaleWithUnitW = scale.WithElement(3, 1f);
        var isZero = Sse.CompareEqual(scaleWithUnitW, Vector128<float>.Zero);
        InverseScale = Sse.Or(Sse.AndNot(isZero, Sse.Divide(Vector128.Create(1f), scaleWithUnitW)), Sse.And(Vector128.Create(LargestFloat), isZero));
        Offset = Sse.And(Sse.Multiply(Vector128.Create(RoundingOffset), scale), OffsetLaneMask);
    }

    public int ResolutionX { get; }
    public int ResolutionZ { get; }
    public Vector128<float> Scale { get; }
    public Vector128<float> InverseScale { get; }
    public Vector128<float> Offset { get; }

    public float HeightAt(int x, int z) => _sampleHeight(x, z);

    public void LocalAabb(out Vector128<float> min, out Vector128<float> max)
    {
        if (_extent.GetElement(1) < 0f)
        {
            var low = HeightAt(0, 0);
            var high = low;
            for (var x = 0; x < ResolutionX; x++)
                for (var z = 0; z < ResolutionZ; z++)
                {
                    var height = HeightAt(x, z);
                    low = MathF.Min(low, height);
                    high = MathF.Max(high, height);
                }
            _minHeight = low;
            _maxHeight = high;
            var scaledLow = low * Scale.GetElement(1);
            var scaledHigh = high * Scale.GetElement(1);
            var bottom = MathF.Min(scaledLow, scaledHigh);
            var top = MathF.Max(scaledLow, scaledHigh);
            _heightCenter = (top + bottom) * 0.5f;
            _extent = _extent.WithElement(1, top - bottom);
        }
        var half = Sse.Multiply(_extent, Vector128.Create(0.5f));
        var center = half.WithElement(1, _heightCenter);
        var column0 = Vector128.Create(1f, 0f, 0f, 0f);
        var column1 = Vector128.Create(0f, 1f, 0f, 0f);
        var column2 = Vector128.Create(0f, 0f, 1f, 0f);
        var radius = Sse.Add(
            Sse.Add(Sse.AndNot(SignBits, Sse.Multiply(Splat(half, 0), column0)), Sse.AndNot(SignBits, Sse.Multiply(Splat(half, 1), column1))),
            Sse.Add(Sse.AndNot(SignBits, Sse.Multiply(Splat(half, 2), column2)), Vector128<float>.Zero));
        var middle = Sse.Add(Sse.Add(Sse.Add(Sse.Multiply(Splat(center, 0), column0), Sse.Multiply(Splat(center, 1), column1)), Sse.Multiply(Splat(center, 2), column2)), Vector128<float>.Zero);
        max = Sse.Add(middle, radius);
        min = Sse.Add(Sse.Xor(radius, SignBits), middle);
    }

    public void Triangle(uint key, Span<Vector128<float>> vertices)
    {
        var x = (int)((key & 0xFFFF) >> 1);
        var z = (int)(key >> 16);
        Span<Vector128<float>> corners = stackalloc Vector128<float>[4];
        for (var corner = 0; corner < 4; corner++)
        {
            var cornerX = x + (corner >> 1);
            var cornerZ = z + (corner & 1);
            corners[corner] = Sse.Multiply(Vector128.Create((float)cornerX, HeightAt(cornerX, cornerZ), (float)cornerZ, 0f), Scale);
        }
        var second = (int)(key & 1);
        vertices[0] = corners[3 * second];
        vertices[1] = corners[second + 1];
        vertices[2] = corners[2 - second];
    }

    public void QueryAabb(Vector128<float> min, Vector128<float> max, List<uint> keys)
    {
        var padding = Vector128.Create(TriangleRadius);
        min = Sse.Subtract(min, padding);
        max = Sse.Add(max, padding);
        LocalAabb(out var fieldMin, out var fieldMax);
        var clipMin = Sse.And(Sse.CompareLessThan(min, fieldMin), ClipLaneMask);
        min = Sse.Or(Sse.AndNot(clipMin, min), Sse.And(clipMin, fieldMin));
        var clipMax = Sse.And(Sse.CompareLessThan(fieldMax, max), ClipLaneMask);
        max = Sse.Or(Sse.AndNot(clipMax, max), Sse.And(clipMax, fieldMax));
        if ((Sse.MoveMask(Sse.CompareLessThan(max, min)) & 7) != 0) return;

        var limit = Vector128.Create((float)(ResolutionX - 2), 0f, (float)(ResolutionZ - 2), 0f);
        var fromMax = CellIndex(max, limit);
        var fromMin = CellIndex(min, limit);
        var firstX = Math.Min(fromMin.X, fromMax.X);
        var lastX = Math.Max(fromMin.X, fromMax.X);
        var firstZ = Math.Min(fromMin.Z, fromMax.Z);
        var lastZ = Math.Max(fromMin.Z, fromMax.Z);

        var countBefore = keys.Count;
        var heightScale = Scale.GetElement(1);
        var bottom = min.GetElement(1);
        var top = max.GetElement(1);
        var allBelow = true;
        var allAbove = true;
        void Classify(int x, int z)
        {
            var height = HeightAt(x, z) * heightScale;
            if (height > bottom) allBelow = false;
            if (top > height) allAbove = false;
        }

        for (var x = firstX; x <= lastX; x++)
            for (var z = firstZ; z <= lastZ; z++)
            {
                var key = (uint)(2 * (x + (z << 15)));
                keys.Add(key);
                keys.Add(key | 1);
                if (allBelow || allAbove) Classify(x, z);
            }
        if (!(allBelow || allAbove)) return;
        for (var x = firstX; x <= lastX + 1; x++) Classify(x, lastZ + 1);
        for (var z = firstZ; z <= lastZ + 1; z++) Classify(lastX + 1, z);
        if (allBelow || allAbove) keys.RemoveRange(countBefore, keys.Count - countBefore);
    }

    private (int X, int Z) CellIndex(Vector128<float> corner, Vector128<float> limit)
    {
        var cell = Sse2.ConvertToVector128Int32WithTruncation(Sse.Max(Sse.Min(Sse.Multiply(Sse.Add(Offset, corner), InverseScale), limit), Vector128<float>.Zero));
        return ((short)cell.GetElement(0) & 0xFFFF, (short)cell.GetElement(2) & 0xFFFF);
    }

    private static float ComputeRoundingOffset()
    {
        var low = 10f;
        var high = 11f;
        for (var step = 0; step < 23; step++)
        {
            var middle = (high + low) * 0.5f;
            var truncated = Sse2.ConvertToVector128Int32WithTruncation(Sse.Max(Sse.Min(Sse.Multiply(Sse.Add(Vector128.Create(middle), Vector128<float>.Zero), Vector128.Create(1f)), Vector128.Create(CellIndexLimit)), Vector128<float>.Zero));
            if ((ushort)(short)truncated.GetElement(0) >= 11) high = middle;
            else low = middle;
        }
        return (high + low) * 0.5f - 11f;
    }
}
