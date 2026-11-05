using Gw2.Formats;
namespace Gw2.Dat.Content;

public sealed record Gw2MapLayerProp(ulong Guid, IReadOnlyList<uint> LayerIds);

public sealed record Gw2MapLayerLight(ulong Guid, IReadOnlyList<uint> LayerIds);

public sealed record Gw2MapLayer(uint LayerId, IReadOnlyList<uint> MetaData);

public sealed class Gw2MapLayers
{
    public Gw2MapLayers(
        IReadOnlyList<Gw2MapLayerProp> props,
        IReadOnlyList<Gw2MapLayerLight> lights,
        IReadOnlyList<Gw2MapLayer> layers)
    {
        Props = props;
        Lights = lights;
        Layers = layers;
    }

    public IReadOnlyList<Gw2MapLayerProp> Props { get; }
    public IReadOnlyList<Gw2MapLayerLight> Lights { get; }
    public IReadOnlyList<Gw2MapLayer> Layers { get; }

    public IReadOnlyList<uint> LayerMaskOf(ulong guid)
    {
        foreach (var prop in Props)
            if (prop.Guid == guid)
                return prop.LayerIds;
        return [];
    }

    public IReadOnlyList<uint> LightLayerMaskOf(ulong guid)
    {
        foreach (var light in Lights)
            if (light.Guid == guid)
                return light.LayerIds;
        return [];
    }

    public static Gw2MapLayers? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.Chunks
            .Select(chunk => chunk.Root)
            .FirstOrDefault(candidate => candidate is not null && candidate.TypeName == "PackMapLayers");
        if (root is null) return null;

        var props = new List<Gw2MapLayerProp>();
        if (root.Fields.GetValueOrDefault("props") is object?[] propObjects)
            foreach (var propObject in propObjects)
                if (propObject is PackObject prop)
                    props.Add(new(
                        PackValue.AsU64(prop.Fields.GetValueOrDefault("guid")),
                        PackValue.AsU32List(prop.Fields.GetValueOrDefault("layerIds"))));

        var lights = new List<Gw2MapLayerLight>();
        if (root.Fields.GetValueOrDefault("lights") is object?[] lightObjects)
            foreach (var lightObject in lightObjects)
                if (lightObject is PackObject light)
                    lights.Add(new(
                        PackValue.AsU64(light.Fields.GetValueOrDefault("guid")),
                        PackValue.AsU32List(light.Fields.GetValueOrDefault("layerIds"))));

        var layers = new List<Gw2MapLayer>();
        if (root.Fields.GetValueOrDefault("layers") is object?[] layerObjects)
            foreach (var layerObject in layerObjects)
                if (layerObject is PackObject layer)
                    layers.Add(new(
                        PackValue.AsU32(layer.Fields.GetValueOrDefault("layerId")),
                        PackValue.AsU32List(layer.Fields.GetValueOrDefault("metaData"))));

        return new(props, lights, layers);
    }
}
