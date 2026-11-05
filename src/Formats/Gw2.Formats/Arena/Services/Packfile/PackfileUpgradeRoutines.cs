using System.Runtime.InteropServices;

namespace Gw2.Formats;

internal unsafe delegate void PackfileUpgradeRoutine(byte* previous, byte* current, PackfileUpgradeAllocator allocator);

internal sealed unsafe class PackfileUpgradeAllocator(List<nint> allocations)
{
    internal byte* Allocate(int size)
    {
        var memory = (byte*)NativeMemory.AllocZeroed((nuint)Math.Max(size, 1));
        allocations.Add((nint)memory);
        return memory;
    }
}

internal static unsafe class PackfileUpgradeRoutines
{
    private static readonly Dictionary<string, PackfileUpgradeRoutine> RoutineByDestinationType = new()
    {
        ["ModelFileDataV67"] = MoveParticleEmitterPlaneSettings,
        ["PackMapTerrainV11"] = SplitTerrainMaterialTiling,
        ["PackMapTerrainV13"] = ClearTerrainFaderMaterialsAndSetFades,
        ["PackMapTerrainV15"] = SetTerrainVerticesPerChunkSide,
    };

    internal static PackfileUpgradeRoutine? For(PackType destinationType) =>
        RoutineByDestinationType.TryGetValue(destinationType.Name, out var routine) ? routine : null;

    private const int ModelCloudDataOffset = 12;
    private const int CloudEmittersCountOffset = 12;
    private const int CloudEmittersOffset = 16;
    private const int EmitterV66Size = 347;
    private const int EmitterV67Size = 270;
    private const int EmitterV67PlaneSettingsOffset = 254;
    private const int EmitterV67MeshSettingsOffset = 262;
    private const int PlaneEmitterSettingsV67Size = 93;

    private static void MoveParticleEmitterPlaneSettings(byte* previous, byte* current, PackfileUpgradeAllocator allocator)
    {
        byte* previousCloud = *(byte**)(previous + ModelCloudDataOffset);
        if (previousCloud == null) return;
        byte* currentCloud = *(byte**)(current + ModelCloudDataOffset);
        uint count = *(uint*)(previousCloud + CloudEmittersCountOffset);
        byte* previousEmitters = *(byte**)(previousCloud + CloudEmittersOffset);
        byte* currentEmitters = *(byte**)(currentCloud + CloudEmittersOffset);
        for (uint index = 0; index < count; index++)
        {
            byte* emitter = previousEmitters + EmitterV66Size * index;
            byte* settings = allocator.Allocate(PlaneEmitterSettingsV67Size);
            *(ulong*)(settings + 17) = *(ulong*)(emitter + 144);
            *(ulong*)settings = *(ulong*)(emitter + 52);
            *(uint*)(settings + 8) = *(uint*)(emitter + 60);
            settings[12] = emitter[345];
            *(uint*)(settings + 13) = *(uint*)(emitter + 116);
            *(ulong*)(settings + 25) = *(ulong*)(emitter + 160);
            *(uint*)(settings + 33) = *(uint*)(emitter + 168);
            *(ulong*)(settings + 37) = *(ulong*)(emitter + 172);
            new ReadOnlySpan<byte>(emitter + 320, 16).CopyTo(new Span<byte>(settings + 77, 16));
            *(ulong*)(settings + 45) = *(ulong*)(emitter + 180);
            *(ulong*)(settings + 61) = *(ulong*)(emitter + 196);
            *(ulong*)(settings + 53) = *(ulong*)(emitter + 188);
            *(ulong*)(settings + 69) = *(ulong*)(emitter + 204);
            byte* currentEmitter = currentEmitters + EmitterV67Size * index;
            *(byte**)(currentEmitter + EmitterV67PlaneSettingsOffset) = settings;
            *(byte**)(currentEmitter + EmitterV67MeshSettingsOffset) = null;
        }
    }

    private const int TerrainMaterialsOffset = 48;
    private const int TerrainMaterialsCountOffset = 32;
    private const int TerrainMaterialsArrayOffset = 36;
    private const int ChunkMaterialV10Size = 81;
    private const int ChunkMaterialV11Size = 83;
    private const int ChunkMaterialV13Size = 119;
    private const int ChunkMaterialV13FaderMaterialOffset = 75;
    private const int TerrainMaterialsMidFadeOffset = 44;
    private const int TerrainMaterialsFarFadeOffset = 52;
    private const int TerrainV15VerticesPerChunkSideOffset = 56;
    private const uint DefaultVerticesPerChunkSide = 32;

    private static void SplitTerrainMaterialTiling(byte* previous, byte* current, PackfileUpgradeAllocator allocator)
    {
        if (previous == null || current == null) return;
        byte* previousMaterials = *(byte**)(previous + TerrainMaterialsOffset);
        byte* currentMaterials = *(byte**)(current + TerrainMaterialsOffset);
        if (previousMaterials == null || currentMaterials == null) return;
        uint count = *(uint*)(previousMaterials + TerrainMaterialsCountOffset);
        byte* previousArray = *(byte**)(previousMaterials + TerrainMaterialsArrayOffset);
        byte* currentArray = *(byte**)(currentMaterials + TerrainMaterialsArrayOffset);
        for (uint index = 0; index < count; index++)
        {
            byte tiling = previousArray[ChunkMaterialV10Size * index];
            byte* currentTiling = currentArray + ChunkMaterialV11Size * index;
            currentTiling[0] = 0;
            currentTiling[1] = tiling;
            currentTiling[2] = tiling;
        }
    }

    private static void ClearTerrainFaderMaterialsAndSetFades(byte* previous, byte* current, PackfileUpgradeAllocator allocator)
    {
        if (previous == null || current == null) return;
        byte* materials = *(byte**)(current + TerrainMaterialsOffset);
        if (materials == null) return;
        uint count = *(uint*)(materials + TerrainMaterialsCountOffset);
        byte* array = *(byte**)(materials + TerrainMaterialsArrayOffset);
        for (uint index = 0; index < count; index++)
            *(ulong*)(array + ChunkMaterialV13Size * index + ChunkMaterialV13FaderMaterialOffset) = 0;
        var midFade = (float*)(materials + TerrainMaterialsMidFadeOffset);
        midFade[0] = 450f;
        midFade[1] = 1500f;
        var farFade = (float*)(materials + TerrainMaterialsFarFadeOffset);
        farFade[0] = 3500f;
        farFade[1] = 6500f;
    }

    private static void SetTerrainVerticesPerChunkSide(byte* previous, byte* current, PackfileUpgradeAllocator allocator)
    {
        if (current != null) *(uint*)(current + TerrainV15VerticesPerChunkSideOffset) = DefaultVerticesPerChunkSide;
    }
}
