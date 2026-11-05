using Gw2.Formats;
using System.Numerics;
using Gw2.Dat.Archive;

namespace Gw2.Dat.Content;

public sealed record Gw2ZoneScatterInstance(
    PackFileReference ModelFilename,
    Vector3 Position,
    Vector3 Rotation,
    float Scale,
    uint ModelFlags,
    int LayerIndex,
    uint LayerFlags,
    bool IsSubModel,
    uint Color = Gw2ZoneScatter.UntintedColor,
    Gw2ZoneScatterBlock Block = default,
    float FadeDistance = 0f,
    bool IsGroundCover = false)
{
    public uint ModelFileId => ModelFilename.TryGetFileId(out var fileId) ? fileId : 0u;
}

public readonly record struct Gw2ZoneScatterBlock(uint DefToken, int X, int Y);

public readonly record struct Gw2ZoneScatterMapBounds(Vector2 Minimum, Vector2 Maximum)
{
    public const float PlacementInset = 1f;

    public static Gw2ZoneScatterMapBounds From(IReadOnlyList<float> bounds) => new(new Vector2(bounds[0], bounds[1]), new Vector2(bounds[2], bounds[3]));

    public Vector2 ClampPlacement(Vector2 position) =>
        Vector2.Clamp(position, Minimum + new Vector2(PlacementInset), Maximum - new Vector2(PlacementInset));
}

public interface IGw2ZoneScatterGround
{
    float HeightAt(Vector2 position);
    Vector3 NormalAt(Vector2 position);
}

public static class Gw2ZoneScatter
{
    public const float CellSize = 48f;
    public const float HalfCell = CellSize / 2f;
    public const int CellsPerBlock = 32;
    public const float ScaleStep = 0.077343747f;
    public const float ZOffsetSpan = 72f;
    public const float ZOffsetCeiling = 36f;
    public const float RandomHeightLimit = 120f;
    public const int TilingRowCount = 25;

    public const uint ModelFlagNoJitter = 0x1;
    public const uint ModelFlagNormalAligned = 0x2;
    public const uint ModelFlagNormalQuarterTurn = 0x4;
    public const uint ModelFlagNormalDamped = 0x8;
    public const uint ModelFlagRandomTilt = 0x10;
    public const uint ModelFlagRandomTiltUnsigned = 0x20;
    public const uint ModelFlagRandomHeight = 0x40;
    public const uint ModelFlagUpsideDown = 0x80;
    public const uint ModelFlagYawFromNormal = 0x100;
    public const uint ModelFlagNoGroundCoverReflection = 0x400000;
    public const int GroundCoverReflectionMinimumSetting = 2;
    public const uint ModelFlagRandomHeightEnabled = 0x4000;
    public const uint ModelFlagFloatsOnWater = 0x8000;
    public const uint ModelFlagHighQualityOnly = 0x100000;
    public const uint LayerFlagSubModelMarked = 0x200;
    public const uint LayerFlagTinted = 0x80;
    public const uint LayerFlagRandomAlpha = 0x100;
    public const uint UntintedColor = 0xFF808080;
    public const uint SubModelMarkedColor = 0x7F7F7F7F;
    public const float SubModelMarkedValue = 0.5f;

    private static readonly float[] FadeDistanceScaleByQualityMode = [0.85f, 1f, 1.5f, 2f];
    private const int FadeDistanceRangeIndex = 1;
    private const float DegreesToRadians = 0.017453292f;
    private const float NormalDamping = 0.35f;

    public static IReadOnlyList<Gw2ZoneScatterInstance> Place(Gw2MapZones zones, Gw2ZoneScatterMapBounds mapBounds, IGw2ZoneScatterGround ground, int qualityMode, Gw2CollisionWater? water = null)
    {
        ArgumentNullException.ThrowIfNull(zones);
        ArgumentNullException.ThrowIfNull(ground);
        var definitions = new Dictionary<uint, Gw2MapZoneDef>();
        foreach (var definition in zones.Definitions) definitions.TryAdd(definition.Token, definition);
        var instances = new List<Gw2ZoneScatterInstance>();
        foreach (var zone in zones.Zones)
            if (definitions.TryGetValue(zone.DefToken, out var definition))
                PlaceZone(zone, definition, mapBounds, ground, water, qualityMode, instances);
        return instances;
    }

    public static void PlaceZone(Gw2MapZone zone, Gw2MapZoneDef definition, Gw2ZoneScatterMapBounds mapBounds, IGw2ZoneScatterGround ground, Gw2CollisionWater? water, int qualityMode, List<Gw2ZoneScatterInstance> instances)
    {
        if (!zone.TryDecodeCoverage(out var width, out var height, out var coverage)) return;
        var x0 = (int)zone.VertRect[0];
        var y0 = (int)zone.VertRect[1];
        var x1 = (int)zone.VertRect[2];
        var y1 = (int)zone.VertRect[3];
        var blockX0 = FloorDiv(x0, CellsPerBlock);
        var blockY0 = FloorDiv(y0, CellsPerBlock);
        var blockX1 = CeilDiv(x1, CellsPerBlock);
        var blockY1 = CeilDiv(y1, CellsPerBlock);
        var hasCollideData = zone.CollideData.Count > 0;
        var blockInstances = new List<Gw2ZoneScatterInstance>();
        var context = new PlacementContext(definition, mapBounds, ground, water, qualityMode, blockInstances);

        for (var blockY = blockY0; blockY < blockY1; blockY++)
        for (var blockX = blockX0; blockX < blockX1; blockX++)
        {
            var cellX0 = Math.Max(blockX * CellsPerBlock, x0);
            var cellY0 = Math.Max(blockY * CellsPerBlock, y0);
            var cellX1 = Math.Min(blockX * CellsPerBlock + CellsPerBlock, x1);
            var cellY1 = Math.Min(blockY * CellsPerBlock + CellsPerBlock, y1);
            if (cellX1 <= cellX0 || cellY1 <= cellY0) continue;

            var collideIndex = 0;
            if (hasCollideData)
            {
                var blockIndex = (blockX1 - blockX0) * (blockY - blockY0) - blockX0 + blockX;
                collideIndex = blockIndex < zone.OffsetData.Count ? zone.OffsetData[blockIndex] : int.MaxValue;
            }

            for (var y = cellY0; y < cellY1; y++)
            for (var x = cellX0; x < cellX1; x++)
            {
                var rowRelative = y - y0;
                var columnRelative = x - x0;
                var linearIndex = x + rowRelative * width - x0;
                var value = coverage[linearIndex];
                if (value == 0) continue;
                Gw2MapZoneCollideData? collide = null;
                if (hasCollideData)
                {
                    if (collideIndex >= zone.CollideData.Count) continue;
                    collide = zone.CollideData[collideIndex++];
                }

                var rng = new Gw2ZoneRandom(CellSeed(zone.Seed, rowRelative, columnRelative, linearIndex));
                var cellCorner = new Vector2(CellSize * x + mapBounds.Minimum.X, CellSize * y + mapBounds.Minimum.Y);
                if ((value & 1) != 0) context.PlaceTiled(value, cellCorner, collide, ref rng);
                else context.PlaceSingle(value, cellCorner, collide, ref rng);
            }

            SortLikeBlockJob(blockInstances);
            Tint(blockInstances, definition, new Gw2ZoneRandom(unchecked((uint)blockX * (uint)blockY)));
            var block = new Gw2ZoneScatterBlock(definition.Token, blockX, blockY);
            instances.AddRange(blockInstances.Select(instance => instance with { Block = block, FadeDistance = FadeDistance(definition.Layers[instance.LayerIndex], qualityMode) }));
            blockInstances.Clear();
        }
    }

    public static void SortLikeBlockJob(List<Gw2ZoneScatterInstance> records)
    {
        if (records.Count < 2) return;
        var pending = new Stack<(int Low, int High)>();
        var low = 0;
        var high = records.Count;
        while (true)
        {
            var mid = low + (high - low) / 2;
            (records[mid], records[low]) = (records[low], records[mid]);
            var i = low;
            var j = high;
            while (true)
            {
                for (i++; i != high; i++)
                    if (CompareRecords(records[i], records[low]) > 0) break;
                for (j--; j != low; j--)
                    if (CompareRecords(records[low], records[j]) > 0) break;
                if (j < i) break;
                (records[i], records[j]) = (records[j], records[i]);
            }

            (records[low], records[j]) = (records[j], records[low]);
            if (j - low < high - i)
            {
                if (i + 1 < high) pending.Push((i, high));
                if (low + 1 < j)
                {
                    high = j;
                    continue;
                }
            }
            else
            {
                if (low + 1 < j) pending.Push((low, j));
                if (i + 1 < high)
                {
                    low = i;
                    continue;
                }
            }

            if (pending.Count == 0) return;
            (low, high) = pending.Pop();
        }
    }

    private static int CompareRecords(Gw2ZoneScatterInstance left, Gw2ZoneScatterInstance right)
    {
        if (left.LayerIndex != right.LayerIndex) return left.LayerIndex > right.LayerIndex ? 1 : 0;
        return CompareFilenames(left.ModelFilename, right.ModelFilename);
    }

    private static int CompareFilenames(PackFileReference left, PackFileReference right)
    {
        ReadOnlySpan<ushort> leftCharacters = [left.Low, left.High, left.Terminator];
        ReadOnlySpan<ushort> rightCharacters = [right.Low, right.High, right.Terminator];
        for (var index = 0; index < leftCharacters.Length; index++)
        {
            if (leftCharacters[index] != rightCharacters[index]) return leftCharacters[index] > rightCharacters[index] ? 1 : -1;
            if (leftCharacters[index] == 0) return 0;
        }

        return 0;
    }

    private static void Tint(List<Gw2ZoneScatterInstance> records, Gw2MapZoneDef definition, Gw2ZoneRandom rng)
    {
        for (var index = 0; index < records.Count; index++)
        {
            var record = records[index];
            if ((record.ModelFlags & LayerFlagSubModelMarked) != 0)
            {
                records[index] = record with { Color = SubModelMarkedColor };
                continue;
            }

            var layer = definition.Layers[record.LayerIndex];
            var hue = Draw(layer, 0, ref rng);
            if (hue > 1f) hue -= 1f;
            var saturation = Draw(layer, 1, ref rng);
            var value = Draw(layer, 2, ref rng);
            var alpha = (layer.LayerFlags & LayerFlagRandomAlpha) != 0 ? rng.NextUnit() : Draw(layer, 3, ref rng);
            records[index] = record with { Color = (layer.LayerFlags & LayerFlagTinted) != 0 ? HsvColor(hue, saturation, value, alpha) : UntintedColor };
        }
    }

    public static float FadeDistance(Gw2MapZoneLayer layer, int qualityMode) =>
        (layer.FadeRange.Count > FadeDistanceRangeIndex ? layer.FadeRange[FadeDistanceRangeIndex] : 0f) * FadeDistanceScaleByQualityMode[qualityMode];

    public static bool DrawsInWaterReflection(Gw2ZoneScatterInstance instance, int reflectionSetting) =>
        !instance.IsGroundCover || (reflectionSetting >= GroundCoverReflectionMinimumSetting && (instance.ModelFlags & ModelFlagNoGroundCoverReflection) == 0);

    public static uint GroundCoverMaterialFileId(Gw2MapZones zones, Gw2ZoneScatterInstance instance)
    {
        if (!instance.IsGroundCover) return 0u;
        var definition = zones.Definitions.FirstOrDefault(candidate => candidate.Token == instance.Block.DefToken);
        if (definition is null || instance.LayerIndex >= definition.Layers.Count) return 0u;
        return definition.Layers[instance.LayerIndex].MaterialName.TryGetFileId(out var fileId) ? fileId : 0u;
    }

    public static IEnumerable<IGrouping<(Gw2ZoneScatterBlock Block, int LayerIndex, uint ModelFileId), Gw2ZoneScatterInstance>> DrawGroups(IEnumerable<Gw2ZoneScatterInstance> instances) =>
        instances.GroupBy(instance => (instance.Block, instance.LayerIndex, instance.ModelFileId));

    private static float Draw(Gw2MapZoneLayer layer, int channel, ref Gw2ZoneRandom rng)
    {
        var range = channel < layer.HslRanges.Count && layer.HslRanges[channel].Length >= 2 ? layer.HslRanges[channel] : [0f, 0f];
        return rng.NextUnit() * (range[1] - range[0]) + range[0];
    }

    public static uint HsvColor(float hue, float saturation, float value, float alpha)
    {
        var alphaByte = ColorByte(alpha);
        if (saturation < 0.000001f)
        {
            var grey = ColorByte(value);
            return alphaByte << 24 | grey << 16 | grey << 8 | grey;
        }

        var sector = hue * 6f;
        if (sector >= 6f) sector = 0f;
        var whole = (int)MathF.Floor(sector);
        var fraction = sector - whole;
        var p = (1f - saturation) * value;
        var q = (1f - fraction * saturation) * value;
        var t = (1f - (1f - fraction) * saturation) * value;
        var (red, green, blue) = whole switch
        {
            0 => (value, t, p),
            1 => (q, value, p),
            2 => (p, value, t),
            3 => (p, q, value),
            4 => (t, p, value),
            _ => (value, p, q),
        };
        return alphaByte << 24 | ColorByte(red) << 16 | ColorByte(green) << 8 | ColorByte(blue);
    }

    private static uint ColorByte(float unit) => (uint)Math.Clamp((int)MathF.Floor(unit * 255f), 0, 255);

    public static uint CellSeed(byte zoneSeed, int rowRelative, int columnRelative, int linearIndex) =>
        (uint)(zoneSeed << 8)
        ^ Gw2ZoneSeedTable.Values[(int)((uint)(rowRelative * columnRelative + linearIndex) % (uint)Gw2ZoneSeedTable.Values.Length)]
        ^ ((uint)(columnRelative ^ (4 * rowRelative)) << 14);

    public static int TilingFillCount(byte tiling, int qualityMode)
    {
        if (tiling < 2 || qualityMode == 2) return tiling;
        if (qualityMode == 1) return tiling == 4 ? 3 : tiling;
        if (qualityMode == 0) return tiling == 4 ? 2 : tiling - 1;
        return tiling;
    }

    public static float ZOffset(ReadOnlySpan<byte> zOffsets, ref Gw2ZoneRandom rng)
    {
        var low = zOffsets.Length > 0 ? zOffsets[0] : (byte)0;
        var high = zOffsets.Length > 1 ? zOffsets[1] : (byte)0;
        var span = (float)(high - low);
        var draw = rng.NextUnit();
        return ZOffsetCeiling - (low + draw * span) / 255f * ZOffsetSpan;
    }

    private static int FloorDiv(int value, int divisor) => (int)MathF.Floor(value / (float)divisor);
    private static int CeilDiv(int value, int divisor) => (int)MathF.Ceiling(value / (float)divisor);

    private sealed class PlacementContext(Gw2MapZoneDef definition, Gw2ZoneScatterMapBounds mapBounds, IGw2ZoneScatterGround ground, Gw2CollisionWater? water, int qualityMode, List<Gw2ZoneScatterInstance> instances)
    {
        public void PlaceSingle(byte value, Vector2 cellCorner, Gw2MapZoneCollideData? collide, ref Gw2ZoneRandom rng)
        {
            var layerIndex = (value >> 4) - 1;
            var variant = (value >> 1) & 7;
            if (layerIndex < 0 || layerIndex >= definition.Layers.Count) return;
            var layer = definition.Layers[layerIndex];
            if (variant >= layer.Models.Count) return;
            var model = layer.Models[variant];
            var zOffset = ZOffset(model.ZOffsets.Span, ref rng);
            var instance = Place(layer, layerIndex, model, model.Flags, cellCorner, zOffset, collide, ref rng);
            instances.Add(instance);
            if (layer.SubModel is { } subModel && rng.NextUnit() <= subModel.Probability && ((subModel.Flags & ModelFlagHighQualityOnly) == 0 || qualityMode != 0))
                instances.Add(instance with
                {
                    ModelFilename = subModel.Filename,
                    ModelFlags = (layer.LayerFlags & LayerFlagSubModelMarked) != 0 ? instance.ModelFlags | LayerFlagSubModelMarked : instance.ModelFlags,
                    IsSubModel = true,
                });
        }

        public void PlaceTiled(byte value, Vector2 cellCorner, Gw2MapZoneCollideData? collide, ref Gw2ZoneRandom rng)
        {
            var layerIndex = (value >> 4) - 1;
            var variant = (value >> 1) & 7;
            if (layerIndex < 0 || layerIndex >= definition.Layers.Count) return;
            var layer = definition.Layers[layerIndex];
            if (variant >= layer.Models.Count) return;
            var model = layer.Models[variant];
            var zOffset = ZOffset(model.ZOffsets.Span, ref rng);
            var flags = model.Flags;
            if ((flags & ModelFlagHighQualityOnly) != 0 && qualityMode == 0) return;
            var fillCount = TilingFillCount(layer.Tiling, qualityMode);
            var row = rng.NextInt(0, TilingRowCount - 1);
            var count = rng.NextInt(1, fillCount);
            var offsets = Gw2ZoneTilingOffsets.For(count, row);
            if (count >= 2) flags |= ModelFlagNoJitter;
            for (var index = 0; index < count; index++)
            {
                var offset = index < offsets.Length ? offsets[index] : Vector2.Zero;
                instances.Add(Place(layer, layerIndex, model, flags, cellCorner + offset + new Vector2(HalfCell), zOffset, collide, ref rng) with { IsGroundCover = true });
            }
        }

        private Gw2ZoneScatterInstance Place(Gw2MapZoneLayer layer, int layerIndex, Gw2MapZoneModel model, uint flags, Vector2 anchor, float zOffset, Gw2MapZoneCollideData? collide, ref Gw2ZoneRandom rng)
        {
            var position = Position(flags, anchor, zOffset, collide, ref rng);
            var rotation = Rotation(layer, flags, new Vector2(position.X, position.Y), collide, ref rng);
            var scale = Scale(layer, ref rng);
            return new Gw2ZoneScatterInstance(model.Filename, position, rotation, scale, flags, layerIndex, layer.LayerFlags, false);
        }

        private Vector3 Position(uint flags, Vector2 anchor, float zOffset, Gw2MapZoneCollideData? collide, ref Gw2ZoneRandom rng)
        {
            var xy = anchor;
            if ((flags & ModelFlagNoJitter) == 0)
            {
                var jitterX = HalfCell - rng.NextUnitExclusive() * CellSize;
                var jitterY = HalfCell - rng.NextUnitExclusive() * CellSize;
                xy = new Vector2(anchor.X + jitterX, anchor.Y + jitterY);
            }
            xy = mapBounds.ClampPlacement(xy);

            var z = ground.HeightAt(xy);
            if ((flags & ModelFlagFloatsOnWater) != 0 && water is not null) z = Math.Min(z, water.SurfaceZAt(new Vector3(xy, z)));
            if (collide is not null) z = collide.ZPos;
            else if ((flags & (ModelFlagRandomHeight | ModelFlagRandomHeightEnabled)) == (ModelFlagRandomHeight | ModelFlagRandomHeightEnabled))
                z = Math.Max(rng.NextUnitExclusive() * ground.HeightAt(xy), RandomHeightLimit);
            return new Vector3(xy, z + zOffset);
        }

        private Vector3 Rotation(Gw2MapZoneLayer layer, uint flags, Vector2 xy, Gw2MapZoneCollideData? collide, ref Gw2ZoneRandom rng)
        {
            var rotX = (flags & ModelFlagUpsideDown) != 0 ? MathF.PI : 0f;
            var rotY = 0f;
            var yawRange = RangeOf(layer, 2);
            var rotZ = (rng.NextUnitExclusive() * (yawRange.Y - yawRange.X) + yawRange.X) * DegreesToRadians;
            if ((flags & (ModelFlagRandomTilt | ModelFlagRandomTiltUnsigned)) != 0)
            {
                var signed = (flags & ModelFlagRandomTiltUnsigned) == 0;
                var tiltX = RangeOf(layer, 0);
                rotX += rng.NextUnitExclusive() * (tiltX.Y - tiltX.X) + tiltX.X;
                var signX = signed && rng.NextUnitExclusive() > 0.5f ? -1f : 1f;
                rotX = rotX * DegreesToRadians * signX;
                var tiltY = RangeOf(layer, 1);
                rotY += rng.NextUnitExclusive() * (tiltY.Y - tiltY.X) + tiltY.X;
                var signY = signed && rng.NextUnitExclusive() > 0.5f ? -1f : 1f;
                rotY = rotY * DegreesToRadians * signY;
            }
            else if ((flags & (ModelFlagNormalAligned | ModelFlagNormalQuarterTurn | ModelFlagNormalDamped)) != 0)
            {
                var normal = collide is not null
                    ? new Vector3(collide.NormalX, collide.NormalY, MathF.Sqrt(MathF.Abs(1f - collide.NormalX * collide.NormalX - collide.NormalY * collide.NormalY)))
                    : ground.NormalAt(xy);
                rotX = -MathF.Atan2(normal.Y, MathF.Sqrt(normal.Z * normal.Z + normal.X * normal.X));
                rotY = MathF.Atan2(normal.X, MathF.Sqrt(normal.Z * normal.Z + normal.Y * normal.Y));
                if ((flags & ModelFlagYawFromNormal) != 0) rotZ = -MathF.Atan2(normal.Y, normal.X) - MathF.PI / 2f;
                if ((flags & ModelFlagNormalQuarterTurn) != 0)
                {
                    if (MathF.Abs(normal.X) <= MathF.Abs(normal.Y)) rotX += normal.Y <= 0f ? MathF.PI / 2f : -MathF.PI / 2f;
                    else rotY += normal.X <= 0f ? -MathF.PI / 2f : MathF.PI / 2f;
                }

                if ((flags & ModelFlagNormalDamped) != 0)
                {
                    rotX *= NormalDamping;
                    rotY *= NormalDamping;
                }
            }

            return new Vector3(rotX, rotY, rotZ);
        }

        private static float Scale(Gw2MapZoneLayer layer, ref Gw2ZoneRandom rng)
        {
            var minimum = layer.ScaleRange.Count > 0 ? layer.ScaleRange[0] : 1f;
            var maximum = layer.ScaleRange.Count > 1 ? layer.ScaleRange[1] : minimum;
            var drawn = rng.NextUnit() * (maximum - minimum) + minimum;
            return RoundToEven((drawn - minimum) / ScaleStep) * ScaleStep + minimum;
        }

        private static Vector2 RangeOf(Gw2MapZoneLayer layer, int axis) =>
            axis < layer.RotRange.Count && layer.RotRange[axis].Length >= 2 ? new Vector2(layer.RotRange[axis][0], layer.RotRange[axis][1]) : Vector2.Zero;

    }

    private static float RoundToEven(float value) => MathF.Round(value, MidpointRounding.ToEven);
}

public struct Gw2ZoneRandom
{
    public const uint Fallback = 123459876u;
    private const uint Modulus = 0x7FFFFFFFu;
    private const uint Multiplier = 48271u;
    private const uint SchrageQuotient = 44488u;
    private const float UnitScale = 4.6566129e-10f;
    private const float UnitScaleExclusive = 4.6566126e-10f;

    private uint _state;

    public Gw2ZoneRandom(uint seed) => _state = seed == 0u ? Fallback : seed;

    public uint Next()
    {
        var value = unchecked(Multiplier * _state - Modulus * (_state / SchrageQuotient));
        if (value > Modulus) value = unchecked(value + 0x80000000u);
        _state = value == 0u ? Fallback : value;
        return _state;
    }

    public float NextUnit() => (int)Next() * UnitScale;

    public float NextUnitExclusive() => (int)Next() * UnitScaleExclusive;

    public int NextInt(int minimum, int maximum) => minimum + (int)(Next() % (uint)(maximum - minimum + 1));
}
