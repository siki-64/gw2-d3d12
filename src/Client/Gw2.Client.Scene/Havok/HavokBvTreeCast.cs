using System;
using System.Collections.Generic;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using static Gw2.Client.Scene.Havok.HavokVector;

namespace Gw2.Client.Scene.Havok;

public interface IHavokTriangleCollection
{
    float TriangleRadius { get; }
    void QueryAabb(Vector128<float> min, Vector128<float> max, List<uint> keys);
    void Triangle(uint key, Span<Vector128<float>> vertices);
}

public sealed class HavokMoppMesh(HavokMoppCode code, HavokExtendedMesh mesh) : IHavokTriangleCollection
{
    public HavokMoppCode Code { get; } = code;
    public HavokExtendedMesh Mesh { get; } = mesh;
    public float TriangleRadius => HavokExtendedMesh.TriangleRadius;
    public void QueryAabb(Vector128<float> min, Vector128<float> max, List<uint> keys) => HavokMopp.QueryAabb(Code, min, max, keys);
    public void Triangle(uint key, Span<Vector128<float>> vertices) => Mesh.Triangle(key, vertices);
}

public sealed class HavokTerrainCollection(HavokTerrainHeightField field) : IHavokTriangleCollection
{
    public HavokTerrainHeightField Field { get; } = field;
    public float TriangleRadius => HavokTerrainHeightField.TriangleRadius;
    public void QueryAabb(Vector128<float> min, Vector128<float> max, List<uint> keys) => Field.QueryAabb(min, max, keys);
    public void Triangle(uint key, Span<Vector128<float>> vertices) => Field.Triangle(key, vertices);
}

public static class HavokBvTreeCast
{
    private static readonly Vector128<float> XyzMask = Vector128.Create(-1, -1, -1, 0).AsSingle();

    public static HavokTransform MultiplyInverse(in HavokTransform first, in HavokTransform second)
    {
        Transposed(first, out var rowX, out var rowY, out var rowZ);
        return new HavokTransform
        {
            Column0 = RotateByRows(second.Column0, rowX, rowY, rowZ),
            Column1 = RotateByRows(second.Column1, rowX, rowY, rowZ),
            Column2 = RotateByRows(second.Column2, rowX, rowY, rowZ),
            Translation = RotateByRows(Sse.Subtract(second.Translation, first.Translation), rowX, rowY, rowZ),
        };
    }

    public static Vector128<float> InverseRotate(in HavokTransform transform, Vector128<float> direction)
    {
        Transposed(transform, out var rowX, out var rowY, out var rowZ);
        return RotateByRows(direction, rowX, rowY, rowZ);
    }

    public static void CapsuleAabb(ReadOnlySpan<Vector128<float>> capsuleLocal, float capsuleRadius, in HavokTransform transform, float tolerance, out Vector128<float> min, out Vector128<float> max)
    {
        var extent = Sse.And(Broadcast(tolerance + capsuleRadius), XyzMask);
        var end = transform.Apply(capsuleLocal[1]);
        var start = transform.Apply(capsuleLocal[0]);
        min = Sse.Subtract(Sse.Min(start, end), extent);
        max = Sse.Add(Sse.Max(start, end), extent);
    }

    public static void SweptCapsuleAabb(ReadOnlySpan<Vector128<float>> capsuleLocal, float capsuleRadius, in HavokTransform capsuleTransform, in HavokTransform collectionTransform, in HavokLinearCastSettings settings, out Vector128<float> min, out Vector128<float> max)
    {
        var relative = MultiplyInverse(collectionTransform, capsuleTransform);
        CapsuleAabb(capsuleLocal, capsuleRadius, relative, settings.Tolerance, out min, out max);
        var path = InverseRotate(collectionTransform, settings.Path);
        min = Sse.Add(Sse.Min(Vector128<float>.Zero, path), min);
        max = Sse.Add(Sse.Max(Vector128<float>.Zero, path), max);
    }

    public static void CapsuleCast<TSink>(ReadOnlySpan<Vector128<float>> capsuleLocal, float capsuleRadius, in HavokTransform capsuleTransform,
        IHavokTriangleCollection collection, in HavokTransform collectionTransform, in HavokLinearCastSettings settings, List<uint> keys,
        ref TSink castSink, ref TSink startSink, bool hasStartSink)
        where TSink : IHavokCdPointSink
    {
        SweptCapsuleAabb(capsuleLocal, capsuleRadius, capsuleTransform, collectionTransform, settings, out var min, out var max);
        keys.Clear();
        collection.QueryAabb(min, max, keys);
        var query = s_capsuleTriangleQuery ??= new CapsuleTriangleQuery();
        query.Capsule[0] = capsuleLocal[0];
        query.Capsule[1] = capsuleLocal[1];
        query.CapsuleRadius = capsuleRadius;
        query.TriangleRadius = collection.TriangleRadius;
        query.TriangleTransform = collectionTransform;
        foreach (var key in keys)
        {
            collection.Triangle(key, query.Triangle);
            castSink.BeginChild(key);
            if (hasStartSink) startSink.BeginChild(key);
            HavokLinearCast.Cast(capsuleTransform, settings, query.ClosestPoints, ref castSink, ref startSink, hasStartSink);
        }
    }

    [ThreadStatic]
    private static CapsuleTriangleQuery? s_capsuleTriangleQuery;

    private sealed class CapsuleTriangleQuery
    {
        public readonly Vector128<float>[] Capsule = new Vector128<float>[2];
        public readonly Vector128<float>[] Triangle = new Vector128<float>[3];
        public readonly HavokClosestPointsQuery ClosestPoints;
        public float CapsuleRadius;
        public float TriangleRadius;
        public HavokTransform TriangleTransform;

        public CapsuleTriangleQuery() => ClosestPoints = Collect;

        private void Collect(in HavokTransform moved, float tolerance, ref HavokClosestCollector collector) =>
            HavokLinearCast.CapsuleTriangle(Capsule, CapsuleRadius, moved, Triangle, TriangleRadius, TriangleTransform, tolerance, ref collector);
    }

    private static void Transposed(in HavokTransform transform, out Vector128<float> rowX, out Vector128<float> rowY, out Vector128<float> rowZ)
    {
        var low = Sse.UnpackLow(transform.Column0, transform.Column1);
        rowX = Sse.MoveLowToHigh(low, transform.Column2);
        rowY = Sse.Shuffle(Sse.MoveHighToLow(rowX, low), transform.Column2, 212);
        rowZ = Sse.Shuffle(Sse.UnpackHigh(transform.Column0, transform.Column1), transform.Column2, 228);
    }

    private static Vector128<float> RotateByRows(Vector128<float> vector, Vector128<float> rowX, Vector128<float> rowY, Vector128<float> rowZ) =>
        Sse.Add(Sse.Add(Sse.Multiply(Splat(vector, 1), rowY), Sse.Multiply(Splat(vector, 0), rowX)), Sse.Multiply(Splat(vector, 2), rowZ));
}
