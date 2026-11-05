using Gw2.Formats;
using System.Numerics;

namespace Gw2.Dat.Content;

public sealed record Gw2MapZoneModel(
    PackFileReference Filename,
    float Probability,
    uint Flags,
    IReadOnlyList<float> HslOffset,
    ReadOnlyMemory<byte> ZOffsets,
    ulong Permutation);

public sealed record Gw2MapZoneLayer(
    byte Type,
    byte Height,
    byte Width,
    byte RadiusGround,
    byte SortGroup,
    byte Tiling,
    IReadOnlyList<float> ScaleRange,
    float Probability,
    IReadOnlyList<float> FadeRange,
    IReadOnlyList<float[]> RotRange,
    IReadOnlyList<float[]> HslRanges,
    float InstanceScaleJitter,
    byte Noise,
    uint LayerFlags,
    PackFileReference MaterialName,
    IReadOnlyList<Gw2MapZoneModel> Models,
    Gw2MapZoneModel? SubModel);

public sealed record Gw2MapZonePage(
    ReadOnlyMemory<byte> Flags,
    IReadOnlyList<uint> ChunkCoord,
    byte Seed,
    IReadOnlyList<uint> PaintFlags);

public sealed record Gw2MapZonePageTable(IReadOnlyList<Gw2MapZonePage> Pages, uint Flags);

public sealed record Gw2MapZoneDef(
    PackFileReference DefFilename,
    uint Token,
    IReadOnlyList<Gw2MapZoneLayer> Layers,
    ulong TimeStamp,
    Gw2MapZonePageTable? PageTable);

public sealed record Gw2MapZoneEncoding(ushort Index, byte Offset);

public sealed record Gw2MapZoneCollideData(float NormalX, float NormalY, float ZPos);

public sealed record Gw2MapZone(
    uint ZoneFlags,
    IReadOnlyList<uint> VertRect,
    float WaterHeight,
    byte Seed,
    uint DefToken,
    IReadOnlyList<float> Range,
    float ZPos,
    ReadOnlyMemory<byte> Flags,
    IReadOnlyList<Gw2MapZoneEncoding> EncodeData,
    IReadOnlyList<Gw2MapZoneCollideData> CollideData,
    IReadOnlyList<ushort> OffsetData,
    IReadOnlyList<Vector2> Vertices,
    ushort BroadId)
{
    public bool TryDecodeCoverage(out int width, out int height, out byte[] coverage)
    {
        width = 0;
        height = 0;
        coverage = [];
        if (VertRect.Count < 4 || EncodeData.Count == 0 || Flags.Length < 2) return false;

        width = (int)VertRect[2] - (int)VertRect[0];
        height = (int)VertRect[3] - (int)VertRect[1];
        if (width <= 0 || height <= 0) return false;

        var pairs = Flags.Length / 2;
        coverage = new byte[width * height];
        var rows = Math.Min(height, EncodeData.Count);
        for (var row = 0; row < rows; row++)
        {
            var run = EncodeData[row].Index;
            var skip = EncodeData[row].Offset;
            var col = 0;
            var first = true;
            while (col < width && run < pairs)
            {
                var length = Flags.Span[run * 2] - (first ? skip : 0);
                first = false;
                var take = Math.Min(Math.Max(length, 0), width - col);
                if (take > 0)
                {
                    Array.Fill(coverage, Flags.Span[run * 2 + 1], row * width + col, take);
                    col += take;
                }
                run++;
            }
        }

        return true;
    }
}

public sealed record Gw2MapZoneBroadphase(byte[] Data, uint MaxBroadId)
{
    public Gw2Broadphase? Decoded => Gw2Broadphase.TryParse(Data);
}

public sealed class Gw2MapZones
{
    public Gw2MapZones(IReadOnlyList<Gw2MapZoneDef> definitions, IReadOnlyList<Gw2MapZone> zones)
    {
        Definitions = definitions;
        Zones = zones;
    }

    public IReadOnlyList<Gw2MapZoneDef> Definitions { get; }
    public IReadOnlyList<Gw2MapZone> Zones { get; }
    public Gw2MapZoneBroadphase? Broadphase { get; init; }

    public static Gw2MapZones? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.FindRoot("PackMapZones");
        if (root is null) return null;

        var definitions = new List<Gw2MapZoneDef>();
        if (root.Fields.GetValueOrDefault("zoneDefArray") is object?[] defObjects)
            foreach (var defObject in defObjects)
                if (defObject is PackObject def)
                    definitions.Add(new(
                        Reference(def, "defFilename"),
                        PackValue.AsU32(def.Fields.GetValueOrDefault("token")),
                        Layers(def.Fields.GetValueOrDefault("layerDefArray")),
                        def.Fields.GetValueOrDefault("timeStamp") is ulong timeStamp ? timeStamp : 0,
                        PageTable(def.Fields.GetValueOrDefault("pageTable") as PackObject)));

        var zones = new List<Gw2MapZone>();
        if (root.Fields.GetValueOrDefault("zoneArray") is object?[] zoneObjects)
            foreach (var zoneObject in zoneObjects)
                if (zoneObject is PackObject zone)
                    zones.Add(new(
                        PackValue.AsU32(zone.Fields.GetValueOrDefault("zoneFlags")),
                        Dwords(zone.Fields.GetValueOrDefault("vertRect")),
                        PackValue.AsF32(zone.Fields.GetValueOrDefault("waterHeight")),
                        AsByte(zone.Fields.GetValueOrDefault("seed")),
                        PackValue.AsU32(zone.Fields.GetValueOrDefault("defToken")),
                        PackValue.AsFloats(zone.Fields.GetValueOrDefault("range")),
                        PackValue.AsF32(zone.Fields.GetValueOrDefault("zPos")),
                        Bytes(zone.Fields.GetValueOrDefault("flags")),
                        Encodings(zone.Fields.GetValueOrDefault("encodeData")),
                        CollideData(zone.Fields.GetValueOrDefault("collideData")),
                        Words(zone.Fields.GetValueOrDefault("offsetData")),
                        PackValue.AsVec2List(zone.Fields.GetValueOrDefault("vertices")),
                        zone.Fields.GetValueOrDefault("broadId") is ushort broadId ? broadId : (ushort)0));

        return new(definitions, zones)
        {
            Broadphase = root.Optional("broadPhase") is PackObject broadphase
                ? new(PackValue.AsBytes(broadphase.Fields.GetValueOrDefault("broadphaseData")), PackValue.OptionalU32(root, "maxBroadId") ?? 0)
                : null,
        };
    }

    public IEnumerable<uint> EnumerateModelDependencies()
    {
        var seen = new HashSet<uint>();
        foreach (var layer in Definitions.SelectMany(definition => definition.Layers))
            foreach (var reference in layer.Models.Select(model => model.Filename)
                         .Append(layer.SubModel?.Filename ?? default))
                if (reference.TryGetFileId(out var fileId) && seen.Add(fileId))
                    yield return fileId;
    }

    private static Gw2MapZonePageTable? PageTable(PackObject? table)
    {
        if (table is null) return null;
        var pages = new List<Gw2MapZonePage>();
        if (table.Fields.GetValueOrDefault("pageArray") is object?[] pageObjects)
            foreach (var pageObject in pageObjects)
                if (pageObject is PackObject page)
                    pages.Add(new(
                        Bytes(page.Fields.GetValueOrDefault("flags")),
                        Dwords(page.Fields.GetValueOrDefault("chunkCoord")),
                        AsByte(page.Fields.GetValueOrDefault("seed")),
                        Dwords(page.Fields.GetValueOrDefault("paintFlags"))));
        return new(pages, PackValue.AsU32(table.Fields.GetValueOrDefault("flags")));
    }

    private static IReadOnlyList<Gw2MapZoneLayer> Layers(object? value)
    {
        if (value is not object?[] values) return [];
        var result = new List<Gw2MapZoneLayer>(values.Length);
        foreach (var item in values)
            if (item is PackObject layer)
                result.Add(new(
                    AsByte(layer.Fields.GetValueOrDefault("type")),
                    AsByte(layer.Fields.GetValueOrDefault("height")),
                    AsByte(layer.Fields.GetValueOrDefault("width")),
                    AsByte(layer.Fields.GetValueOrDefault("radiusGround")),
                    AsByte(layer.Fields.GetValueOrDefault("sortGroup")),
                    AsByte(layer.Fields.GetValueOrDefault("tiling")),
                    PackValue.AsFloats(layer.Fields.GetValueOrDefault("scaleRange")),
                    PackValue.AsF32(layer.Fields.GetValueOrDefault("probability")),
                    PackValue.AsFloats(layer.Fields.GetValueOrDefault("fadeRange")),
                    Vectors(layer.Fields.GetValueOrDefault("rotRange")),
                    Vectors(layer.Fields.GetValueOrDefault("hslRanges")),
                    PackValue.AsF32(layer.Fields.GetValueOrDefault("instanceScaleJitter")),
                    AsByte(layer.Fields.GetValueOrDefault("noise")),
                    PackValue.AsU32(layer.Fields.GetValueOrDefault("layerFlags")),
                    Reference(layer, "materialname"),
                    Models(layer.Fields.GetValueOrDefault("modelArray")),
                    layer.Fields.GetValueOrDefault("subModel") is PackObject subModel ? Model(subModel) : null));
        return result;
    }

    private static IReadOnlyList<Gw2MapZoneModel> Models(object? value)
    {
        if (value is not object?[] values) return [];
        var result = new List<Gw2MapZoneModel>(values.Length);
        foreach (var item in values)
            if (item is PackObject model)
                result.Add(Model(model));
        return result;
    }

    private static Gw2MapZoneModel Model(PackObject model) => new(
        Reference(model, "filename"),
        PackValue.AsF32(model.Fields.GetValueOrDefault("probability")),
        Gw2MapZoneModelFlags.FromPacked(PackValue.AsU32(model.Fields.GetValueOrDefault("flags"))),
        PackValue.AsFloats(model.Fields.GetValueOrDefault("hslOffset")),
        Bytes(model.Fields.GetValueOrDefault("zOffsets")),
        model.Fields.GetValueOrDefault("permutation") is ulong permutation ? permutation : 0);

    private static IReadOnlyList<Gw2MapZoneEncoding> Encodings(object? value)
    {
        if (value is not object?[] values) return [];
        var result = new List<Gw2MapZoneEncoding>(values.Length);
        foreach (var item in values)
            if (item is PackObject encoding)
                result.Add(new(
                    encoding.Fields.GetValueOrDefault("index") is ushort index ? index : (ushort)0,
                    AsByte(encoding.Fields.GetValueOrDefault("offset"))));
        return result;
    }

    private static IReadOnlyList<Gw2MapZoneCollideData> CollideData(object? value)
    {
        if (value is not object?[] values) return [];
        var result = new List<Gw2MapZoneCollideData>(values.Length);
        foreach (var item in values)
            if (item is PackObject collide)
                result.Add(new(
                    PackValue.AsF32(collide.Fields.GetValueOrDefault("normalX")),
                    PackValue.AsF32(collide.Fields.GetValueOrDefault("normalY")),
                    PackValue.AsF32(collide.Fields.GetValueOrDefault("zPos"))));
        return result;
    }

    private static PackFileReference Reference(PackObject obj, string name) =>
        obj.Fields.GetValueOrDefault(name) is PackFileReference reference ? reference : default;

    private static IReadOnlyList<float[]> Vectors(object? value)
    {
        if (value is not object?[] values) return [];
        var result = new List<float[]>(values.Length);
        foreach (var item in values)
            if (item is float[] floats)
                result.Add(floats);
        return result;
    }

    private static IReadOnlyList<uint> Dwords(object? value)
    {
        if (value is uint[] dwords) return dwords;
        if (value is not object?[] values) return [];
        var result = new List<uint>(values.Length);
        foreach (var item in values)
            if (item is uint dword) result.Add(dword);
        return result;
    }

    private static IReadOnlyList<ushort> Words(object? value)
    {
        if (value is not object?[] values) return [];
        var result = new List<ushort>(values.Length);
        foreach (var item in values)
            if (item is ushort word) result.Add(word);
        return result;
    }

    private static ReadOnlyMemory<byte> Bytes(object? value) =>
        value is ReadOnlyMemory<byte> memory ? memory : ReadOnlyMemory<byte>.Empty;

    private static byte AsByte(object? value) => value switch
    {
        byte byteValue => byteValue,
        uint unsigned => (byte)unsigned,
        int signed => (byte)signed,
        ushort shortValue => (byte)shortValue,
        _ => 0,
    };
}
