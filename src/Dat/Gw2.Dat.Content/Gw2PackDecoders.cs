namespace Gw2.Dat.Content;

public sealed record Gw2PackDecoder(string Name, string? RootTypePrefix, string? ChunkType, Func<Gw2AssetDocument, object?> Decode)
{
    public bool Accepts(Gw2ParsedChunk chunk) =>
        (RootTypePrefix is not null && chunk.Root is { } root && root.TypeName.StartsWith(RootTypePrefix, StringComparison.Ordinal))
        || (ChunkType is not null && chunk.Chunk.Type == ChunkType);

    public bool Accepts(Gw2AssetDocument document) => document.Chunks.Any(Accepts);
}

public sealed record Gw2DecodedObject(string Decoder, object Value);

public static class Gw2PackDecoders
{
    public static IReadOnlyList<Gw2PackDecoder> All { get; } =
    [
        Root("AmatDx9Material", Gw2AmatDx9Material.TryParse),
        Root("AmatGr", Gw2AmatGr.TryParse),
        Root("AmatMaterial", Gw2AmatMaterial.Parse),
        Root("AmatToolParams", Gw2AmatTool.TryParse),
        Root("BankFileData", Gw2AudioBank.TryParse),
        Root("BankIndexData", Gw2BankIndex.TryParse),
        Root("CollideModelManifest", Gw2CollideModelManifest.TryParse),
        Root("CollideNavMesh", Gw2CollideNavMesh.TryParse),
        Root("ContentPortalManifest", Gw2PortalManifest.TryParse),
        Root("KeyTableData", Gw2KeyTable.TryParse),
        Root("MapAudio", Gw2MapAudio.TryParse),
        Root("MapEditData", Gw2MapEdit.TryParse),
        Root("MapExpansionProperties", Gw2MapExpansionProperties.TryParse),
        Root("MapLegacy", Gw2MapSound.TryParse),
        Root("MapMission", Gw2MapMission.TryParse),
        Root("MapObstacles", Gw2MapObstacles.TryParse),
        Root("MapOcclusionTome", Gw2MapOcclusion.TryParse),
        Root("MapOcclusions", Gw2MapOcclusions.TryParse),
        Root("MapPackage", Gw2MapPackage.TryParse),
        Root("MapParam", Gw2MapInfo.TryParse),
        Root("MapParam", Gw2MapParam.TryParse),
        Root("MapShore", Gw2MapShore.TryParse),
        Root("MapSurfaces", Gw2MapSurfaces.TryParse),
        Root("MapTerrainImg", Gw2PagedImage.TryParse),
        Root("ModelFileAnimation", Gw2ModelAnimation.TryParse),
        Root("ModelFileCollision", Gw2ModelCollision.TryParse),
        Root("ModelFileData", Gw2Model.Parse),
        Root("ModelFileData", Gw2ModelParticles.Parse),
        Root("ModelFileData", Gw2ModelStreaks.Parse),
        Root("ModelFileExpansion", Gw2ModelExpansion.TryParse),
        Root("ModelFileGame", Gw2ModelGame.TryParse),
        Root("ModelFileGeometry", Gw2ModelGeometry.TryParse),
        Root("ModelFileGeometry", Gw2Model.Parse),
        Root("ModelFileGr2s", Gw2ModelGr2s.TryParse),
        Root("ModelFileIcon", Gw2ModelIcon.TryParse),
        Root("ModelFileProperties", Gw2ModelProperties.TryParse),
        Root("ModelFileRootMotion", Gw2ModelRootMotion.TryParse),
        Root("ModelFileSkeleton", Gw2ModelSkeleton.TryParse),
        Chunk("mach", Gw2AnimMachines.TryParse),
        Chunk("scal", Gw2BoneScaleFile.TryParse),
        Chunk("STAR", Gw2StarField.TryParse),
        Chunk("AFNT", Gw2FontManifest.TryParse),
        Root("PackAnimConfig", Gw2AnimConfig.TryParse),
        Root("PackAnimFallbacks", Gw2AnimFallbacks.TryParse),
        Root("PackAnimSequences", Gw2AnimSequences.TryParse),
        Root("PackAssetManifest", Gw2AssetManifest.TryParse),
        Root("PackAssetRootManifest", Gw2AssetRootManifest.TryParse),
        Root("PackComposite", Gw2Composite.TryParse),
        Root("PackContent", Gw2ContentStore.TryParse),
        Root("PackEmoteAnimations", Gw2EmoteAnimations.TryParse),
        Root("PackEula", Gw2Eula.TryParse),
        Root("PackMapAreas", Gw2MapAreas.TryParse),
        Root("PackMapBlock", Gw2MapBlock.TryParse),
        Root("PackMapCoarseNavGraph", Gw2CoarseNavGraph.TryParse),
        Root("PackMapCollide", Gw2MapCollision.TryParse),
        Root("PackMapCubeMap", Gw2MapCubeMap.TryParse),
        Root("PackMapDecals", Gw2MapDecals.TryParse),
        Root("PackMapEnvironment", Gw2MapEnvironment.TryParse),
        Root("PackMapLayers", Gw2MapLayers.TryParse),
        Root("PackMapLights", Gw2MapLights.TryParse),
        Root("PackMapMetadata", Gw2MapMetadata.TryParse),
        Root("PackMapNavMeshChunk", Gw2NavMeshChunkFile.TryParse),
        Root("PackMapNavMeshExternal", Gw2NavMeshExternal.TryParse),
        Root("PackMapPhysics", Gw2MapPhysics.TryParse),
        Root("PackMapPhysicsNavMesh", Gw2PhysicsNavMesh.TryParse),
        Root("PackMapProp", Gw2MapProps.TryParse),
        Root("PackMapResourceMap", Gw2MapResourceMap.TryParse),
        Root("PackMapRivers", Gw2MapRivers.TryParse),
        Root("PackMapShadow", Gw2MapShadow.TryParse),
        Root("PackMapShadowExt", Gw2MapShadowExt.TryParse),
        Root("PackMapTerrain", Gw2MapTerrain.TryParse),
        Root("PackMapToolFs", Gw2MapToolFs.TryParse),
        Root("PackMapWater", Gw2MapWater.TryParse),
        Root("PackMapZones", Gw2MapZones.TryParse),
        Root("PagedImageTableData", Gw2PagedImage.TryParse),
        Root("SceneData", Gw2Scene.TryParse),
        Root("ScriptFileData", Gw2AudioScript.TryParse),
        Root("TextPackManifest", Gw2TextManifest.TryParse),
        Root("TextPackPasswords", Gw2TextPasswords.TryParse),
        Root("TextPackVariants", Gw2TextVariants.TryParse),
        Root("TextPackVoices", Gw2TextVoices.TryParse),
        Root("WaveformData", Gw2Waveform.TryParse),
    ];

    public static IReadOnlyList<Gw2PackDecoder> For(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return All.Where(decoder => decoder.Accepts(document)).ToArray();
    }

    public static IReadOnlyList<Gw2DecodedObject> Decode(Gw2AssetDocument document)
    {
        var decoded = new List<Gw2DecodedObject>();
        foreach (var decoder in For(document).GroupBy(decoder => decoder.Name).Select(routes => routes.First()))
            if (decoder.Decode(document) is { } value)
                decoded.Add(new(decoder.Name, value));
        return decoded;
    }

    public static T? Decode<T>(Gw2AssetDocument document) where T : class
    {
        ArgumentNullException.ThrowIfNull(document);
        var name = typeof(T).Name;
        var routes = All.Where(decoder => decoder.Name == name).ToArray();
        if (routes.Length == 0) throw new InvalidOperationException($"No pack decoder route for {name}");
        foreach (var decoder in routes)
            if (decoder.Accepts(document) && decoder.Decode(document) is T value)
                return value;
        return null;
    }

    private static Gw2PackDecoder Root<T>(string rootTypePrefix, Func<Gw2AssetDocument, T?> decode) where T : class =>
        new(typeof(T).Name, rootTypePrefix, null, document => decode(document));

    private static Gw2PackDecoder Chunk<T>(string chunkType, Func<Gw2AssetDocument, T?> decode) where T : class =>
        new(typeof(T).Name, null, chunkType, document => decode(document));
}
