using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using Gw2.Dat.Content;

namespace Gw2.Client.Scene.Havok;

public readonly record struct Gw2SurfaceHit(Vector3 Position, Vector3 Normal, float Fraction, long BodyId, bool IsTerrain);

public sealed class Gw2HavokMapWorld
{
    public const float CollisionUnitsPerWorldUnit = 0.03125f;
    public const float WorldUnitsPerCollisionUnit = 32f;
    public const uint ChunkWithoutCollisionFlag = 4;
    private const float BroadphaseMargin = 0.05f;
    public const int PropModelLayer = 11;
    public const int TerrainLayer = 12;
    public const int ZoneModelLayer = 13;
    public const int ObsModelLayer = 14;
    public const uint AllLayers = 0;
    public const uint TerrainOnlyMask = 0x1000;
    public const uint CameraLineOfSightMask = 0x3830;
    public const uint CameraSweepMask = 0x7830;

    public const uint BlockerBodyTypesForOwnKey = 0x30;
    public const uint BlockerBodyTypesForOtherKey = 0x7830;

    public static bool BuildsBlockers(int bodyType, bool usesOwnKey) =>
        ((usesOwnKey ? BlockerBodyTypesForOwnKey : BlockerBodyTypesForOtherKey) & (1u << (bodyType & 0x1F))) != 0;

    public static bool LayerPasses(uint mask, int layer) => mask == AllLayers || (mask & (1u << (layer & 0x1F))) != 0;

    private readonly HavokWorldBodyBounds[] _bodies;

    private Gw2HavokMapWorld(HavokWorldBodyBounds[] bodies) => _bodies = bodies;

    public IEnumerable<HavokWorldBody> Bodies => _bodies.Select(bounds => bounds.Body);

    public static Gw2HavokMapWorld Build(Gw2MapCollision? collision, Gw2MapTerrain? terrain, IReadOnlyList<float> mapBounds)
    {
        var bodies = new List<HavokWorldBodyBounds>();
        if (terrain is not null && mapBounds.Count >= 4) AddTerrain(bodies, terrain, mapBounds);
        if (collision is not null) AddPlacements(bodies, collision);
        return new Gw2HavokMapWorld(bodies.ToArray());
    }

    public Gw2HavokMapWorld WithBodyIds(Func<int, long> idOfBody) =>
        new(_bodies.Select((bounds, index) => bounds with { Body = bounds.Body with { Id = idOfBody(index) } }).ToArray());

    public int BodyCount => _bodies.Length;

    public void CollectOverlapping(Vector128<float> min, Vector128<float> max, List<HavokWorldBody> overlapping, uint layerMask = AllLayers)
    {
        overlapping.Clear();
        foreach (var body in _bodies)
        {
            if (!LayerPasses(layerMask, body.Body.Layer)) continue;
            var separated = Sse.Or(Sse.CompareGreaterThan(body.Min, max), Sse.CompareLessThan(body.Max, min));
            if ((Sse.MoveMask(separated) & 7) != 0) continue;
            overlapping.Add(body.Body);
        }
    }

    public Gw2SurfaceHit? CastGameRay(Vector3 from, Vector3 to, uint layerMask = AllLayers)
    {
        var closest = new HavokClosestRayHit();
        if (!CastRay(Gw2CharacterStep.CollisionOf(from), Gw2CharacterStep.CollisionOf(to), closest, out var body, layerMask)) return null;
        var normal = closest.Normal;
        return new Gw2SurfaceHit(Vector3.Lerp(from, to, closest.Fraction), new Vector3(normal.GetElement(0), normal.GetElement(1), -normal.GetElement(2)),
            closest.Fraction, body.Id, body.Collection is HavokTerrainCollection);
    }

    public bool CastRay(Vector128<float> from, Vector128<float> to, HavokClosestRayHit closest, out HavokWorldBody hitBody, uint layerMask = AllLayers)
    {
        hitBody = default;
        var bodyHit = new HavokClosestRayHit();
        var low = Sse.Min(from, to);
        var high = Sse.Max(from, to);
        foreach (var bounds in _bodies)
        {
            if (!LayerPasses(layerMask, bounds.Body.Layer)) continue;
            var separated = Sse.Or(Sse.CompareGreaterThan(bounds.Min, high), Sse.CompareLessThan(bounds.Max, low));
            if ((Sse.MoveMask(separated) & 7) != 0) continue;
            if (!SegmentTouchesBox(from, to, bounds.Min, bounds.Max)) continue;
            var body = bounds.Body;
            var localFrom = HavokBvTreeCast.InverseRotate(body.Transform, Sse.Subtract(from, body.Transform.Translation));
            var localTo = HavokBvTreeCast.InverseRotate(body.Transform, Sse.Subtract(to, body.Transform.Translation));
            bodyHit.Reset(closest.Fraction);
            switch (body.Collection)
            {
                case HavokTerrainCollection terrain:
                    HavokHeightFieldRay.CastRay(terrain.Field, localFrom, localTo, bodyHit);
                    break;
                case HavokMoppMesh mesh:
                    CastMesh(mesh, localFrom, localTo, bodyHit);
                    break;
            }
            if (!bodyHit.HasHit) continue;
            closest.AddHit(bodyHit.Fraction, RotateToWorld(body.Transform, bodyHit.Normal), bodyHit.Key);
            hitBody = body;
        }
        return closest.HasHit;
    }

    private static void CastMesh(HavokMoppMesh mesh, Vector128<float> from, Vector128<float> to, HavokClosestRayHit hit)
    {
        var vertices = new Vector128<float>[3];
        HavokMoppRay.CastRay(mesh.Code, from, to, hit.Fraction, key =>
        {
            mesh.Triangle(key, vertices);
            var fraction = hit.Fraction;
            if (HavokTriangleRay.CastRay(from, to, vertices[0], vertices[1], vertices[2], ref fraction, out var normal))
                hit.AddHit(fraction, normal, (int)key);
            return hit.Fraction;
        });
    }

    private static Vector128<float> RotateToWorld(in HavokTransform transform, Vector128<float> normal) =>
        Sse.Add(Sse.Add(Sse.Multiply(Splat(normal, 1), transform.Column1), Sse.Multiply(Splat(normal, 0), transform.Column0)), Sse.Multiply(Splat(normal, 2), transform.Column2));

    private static Vector128<float> Splat(Vector128<float> value, byte lane) => Vector128.Create(value.GetElement(lane));

    private static bool SegmentTouchesBox(Vector128<float> from, Vector128<float> to, Vector128<float> min, Vector128<float> max)
    {
        var enter = 0f;
        var leave = 1f;
        for (var axis = 0; axis < 3; axis++)
        {
            var start = from.GetElement(axis);
            var delta = to.GetElement(axis) - start;
            var low = min.GetElement(axis);
            var high = max.GetElement(axis);
            if (delta == 0f)
            {
                if (start < low || start > high) return false;
                continue;
            }
            var first = (low - start) / delta;
            var second = (high - start) / delta;
            enter = MathF.Max(enter, MathF.Min(first, second));
            leave = MathF.Min(leave, MathF.Max(first, second));
            if (enter > leave) return false;
        }
        return true;
    }

    public static Quaternion RotationOf(Vector3 rotate)
    {
        var halfX = rotate.X * 0.5;
        var halfY = rotate.Y * 0.5;
        var halfZ = rotate.Z * -0.5;
        var (sinX, cosX) = Math.SinCos(halfX);
        var (sinY, cosY) = Math.SinCos(halfY);
        var (sinZ, cosZ) = Math.SinCos(halfZ);
        return new Quaternion(
            (float)(cosZ * cosY * sinX + sinZ * sinY * cosX),
            (float)(cosZ * sinY * cosX - sinZ * cosY * sinX),
            (float)(sinZ * cosY * cosX - cosZ * sinY * sinX),
            (float)(sinZ * sinY * sinX + cosZ * cosY * cosX));
    }

    private static void AddTerrain(List<HavokWorldBodyBounds> bodies, Gw2MapTerrain terrain, IReadOnlyList<float> mapBounds)
    {
        var tiles = terrain.TilesPerChunk;
        var chunksX = (int)terrain.Dims[0] / tiles;
        var chunksY = (int)terrain.Dims[1] / tiles;
        var blockSize = terrain.HeightBlockSize;
        if ((long)chunksX * chunksY * blockSize * blockSize != terrain.Heights.Count) return;
        var heights = terrain.Heights;
        var resolution = tiles + 1;
        var unitsPerSample = (int)terrain.WorldUnitsPerHeightSample;
        var spacing = unitsPerSample * CollisionUnitsPerWorldUnit;
        var rotation = HavokQuaternion.Multiply(HavokQuaternion.Identity, HavokQuaternion.FromAxisAngle(Vector128.Create(1f, 0f, 0f, 0f), 1.5707964f));
        var minX = mapBounds[0];
        var maxY = mapBounds[3];
        var sampleColumns = chunksX * tiles + 1;
        var sampleRows = chunksY * tiles + 1;
        for (var firstRow = 0; firstRow < sampleRows - 1; firstRow += tiles)
        for (var firstColumn = 0; firstColumn < sampleColumns - 1; firstColumn += tiles)
        {
            var chunkIndex = firstRow / tiles * chunksX + firstColumn / tiles;
            var flags = terrain.Chunks.Count == chunksX * chunksY ? terrain.Chunks[chunkIndex].Flags : 0;
            if ((flags & ChunkWithoutCollisionFlag) != 0) continue;
            var originX = (float)(firstColumn * unitsPerSample) + minX;
            var originY = maxY - (float)(unitsPerSample * firstRow);
            var columnStart = firstColumn;
            var rowStart = firstRow;
            var field = new HavokTerrainHeightField(resolution, resolution, Vector128.Create(spacing, CollisionUnitsPerWorldUnit, spacing, 0f),
                (x, z) => -StoredHeight(heights, chunksX, chunksY, tiles, blockSize, x + columnStart, z + rowStart));
            var translation = Vector128.Create(originX * CollisionUnitsPerWorldUnit, originY * CollisionUnitsPerWorldUnit, -0f * CollisionUnitsPerWorldUnit, 0f);
            var transform = HavokQuaternion.ToTransform(rotation, translation);
            var collection = new HavokTerrainCollection(field);
            var low = float.MaxValue;
            var high = float.MinValue;
            for (var z = 0; z < resolution; z++)
            for (var x = 0; x < resolution; x++)
            {
                var height = -StoredHeight(heights, chunksX, chunksY, tiles, blockSize, x + columnStart, z + rowStart) * CollisionUnitsPerWorldUnit;
                low = MathF.Min(low, height);
                high = MathF.Max(high, height);
            }
            var extent = tiles * spacing;
            var min = Vector128.Create(originX * CollisionUnitsPerWorldUnit, originY * CollisionUnitsPerWorldUnit - extent, low, 0f);
            var max = Vector128.Create(originX * CollisionUnitsPerWorldUnit + extent, originY * CollisionUnitsPerWorldUnit, high, 0f);
            bodies.Add(Bounded(new HavokWorldBody { Id = bodies.Count + 1, Collection = collection, Transform = transform, Motion = FixedMotion, Layer = TerrainLayer }, min, max, HavokTerrainHeightField.TriangleRadius));
        }
    }

    private static float StoredHeight(IReadOnlyList<float> heights, int chunksX, int chunksY, int tiles, int blockSize, int column, int row)
    {
        var chunkX = column / tiles;
        var offsetX = column % tiles;
        if (chunkX == chunksX)
        {
            chunkX--;
            offsetX = tiles;
        }
        var chunkY = row / tiles;
        var offsetY = row % tiles;
        if (chunkY == chunksY)
        {
            chunkY--;
            offsetY = tiles;
        }
        var block = (chunkY * chunksX + chunkX) * blockSize * blockSize;
        return heights[block + (offsetY + Gw2MapTerrain.HeightBlockBorder) * blockSize + offsetX + Gw2MapTerrain.HeightBlockBorder];
    }

    private static readonly HavokBodyMotion FixedMotion = new() { MotionType = HavokMotionType.Fixed };

    private static void AddPlacements(List<HavokWorldBodyBounds> bodies, Gw2MapCollision collision)
    {
        var meshCache = new Dictionary<(int Soup, float Scale), HavokMoppMesh>();
        foreach (var placement in collision.ObsModels)
            AddPlacement(bodies, collision, meshCache, placement, Gw2MapCollision.DefaultPlacementSequence, Vector3.Zero, 1f, ObsModelLayer);
        foreach (var placement in collision.PropModels)
            AddPlacement(bodies, collision, meshCache, placement, Gw2MapCollision.PropPlacementSequence(placement), placement.Rotate, placement.Scale, PropModelLayer);
        foreach (var placement in collision.ZoneModels)
            AddPlacement(bodies, collision, meshCache, placement, Gw2MapCollision.ZonePlacementSequence, placement.Rotate, placement.Scale, ZoneModelLayer);
    }

    private static void AddPlacement(List<HavokWorldBodyBounds> bodies, Gw2MapCollision collision, Dictionary<(int, float), HavokMoppMesh> meshCache,
        Gw2CollisionPlacement placement, ulong sequence, Vector3 rotate, float scale, int layer)
    {
        if (!collision.TryResolveAnimation(placement.GeometryIndex, sequence, out var animation)) return;
        var rotation = RotationOf(rotate);
        var translation = Gw2CharacterStep.CollisionOf(placement.Translate);
        var transform = HavokQuaternion.ToTransform(Vector128.Create(rotation.X, rotation.Y, rotation.Z, rotation.W), translation);
        foreach (var soupIndex in animation.CollisionIndices)
        {
            if (soupIndex >= collision.Collisions.Count) continue;
            var soup = collision.Collisions[(int)soupIndex];
            if (soup.Indices.Count < 3 || soup.Vertices.Count == 0 || soup.MoppData.Length <= 20) continue;
            if (!meshCache.TryGetValue(((int)soupIndex, scale), out var mesh))
            {
                var vertices = new float[soup.Vertices.Count * 3];
                for (var index = 0; index < soup.Vertices.Count; index++)
                {
                    vertices[3 * index] = soup.Vertices[index].X;
                    vertices[3 * index + 1] = soup.Vertices[index].Y;
                    vertices[3 * index + 2] = soup.Vertices[index].Z;
                }
                mesh = new HavokMoppMesh(HavokMoppCode.FromBlob(soup.MoppData).Scaled(scale), new HavokExtendedMesh(vertices, soup.Indices.ToArray(), scale));
                meshCache[((int)soupIndex, scale)] = mesh;
            }
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            var matrix = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(translation.GetElement(0), translation.GetElement(1), translation.GetElement(2));
            foreach (var vertex in soup.Vertices)
            {
                var world = Vector3.Transform(vertex, matrix);
                min = Vector3.Min(min, world);
                max = Vector3.Max(max, world);
            }
            bodies.Add(Bounded(new HavokWorldBody { Id = bodies.Count + 1, Collection = mesh, Transform = transform, Motion = FixedMotion, Layer = layer },
                Vector128.Create(min.X, min.Y, min.Z, 0f), Vector128.Create(max.X, max.Y, max.Z, 0f), 0f));
        }
    }

    private static HavokWorldBodyBounds Bounded(HavokWorldBody body, Vector128<float> min, Vector128<float> max, float radius)
    {
        var padding = Vector128.Create(radius + BroadphaseMargin, radius + BroadphaseMargin, radius + BroadphaseMargin, 0f);
        return new HavokWorldBodyBounds(body, Sse.Subtract(min, padding), Sse.Add(max, padding));
    }

    private readonly record struct HavokWorldBodyBounds(HavokWorldBody Body, Vector128<float> Min, Vector128<float> Max);
}
