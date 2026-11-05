# Water: new client

How the new client implements [Water](../../../gw2-research/docs/rendering/water.md). The retail behaviour and its evidence are in that note.

## Bodies and streaming

**In the remake.** `Gw2WaterBodies` (`Gw2.Client.Scene`) builds the bodies and meshes. `Gw2WaterLoader` (`Gw2.Client`)
uploads them once at map load and re-centres the global plane on the eye each frame. `GrWater` draws after the opaque
pass with the environment's water AMAT (see "Material") through the archive shader path, after a screen copy. Open:
the exact `Polygon.cpp` clip (cells are clipped against the polygon with Sutherland-Hodgman instead), the day/night
blend of the above set, and the waves (`Water_UpdateWaves`).

The remake does the same: `GrCharacterPass` skips the light buffer while it draws the reflection. The remake draws
reflected terrain and props with the reflection effect and falls back to the default effect when a material has none.
`GrWaterReflection` draws terrain and opaque props that way into a full-resolution colour + depth target, cleared to
`FogColorFar`, before the main pass.

The remake fills the inner grid the same way when the snap cell changes (`GrWaterMesh.Follow`), and the outer ring
stays opaque.

The stored value is `depth / depthAttenuation`. `Gw2WaterTerrainCut` mirrors this, and `Gw2TerrainGround.ChunkFlagsAt`
reads the chunk flags (dat chunk rows run top-down, like the height blocks). The remake assumes the quality gate is
on.

The remake writes the re-centred plane vertices in world space (`GrWaterMesh.Follow`) and draws every water mesh with
an identity World.

`GrArchiveShaderMaterial` binds parameters by these tokens (`Gw2AmatShaderBinary.ConstantTokensByName`).

## Material

The remake keeps positions (`PositionalFileList`) and binds a transparent stand-in for entry 0. **Sampler → texture.**
The bgfx draw uses the `textureSlot` field for both the stage and the material texture: `BgfxShader_DecodeSamplers`
copies each AMAT sampler into a 28-byte record (engine flag, vertex flags, record index, state, `textureSlot`,
`textureIndex & ~0x70`), and `BgfxDraw_SetTextures` binds `textureSet[textureSlot]`.

## Hidden global water

`GrCharacterPass.DrawWater` uses the same state.

## Empty water material

The remake does not read the exe. `Gw2BuiltInWaterMaterial` (in `Gw2.Dat.Content`) holds the pack addresses, sampler
table, pass flags and constant tokens; `Gw2BuiltInWaterMaterialTests` (DatSurvey tests) checks it against the packs in
the installed exe. The shaders are `src/Client/Gw2.Client.Graphics/Shaders/GrBuiltInWater.hlsl`, ported from
the decompiled HLSL (`Gw2.DatSurvey --exe-materials`), with matrices as `float4[4]` register rows. `Gw2WaterLoader`
builds the fallback when the reference is empty, and `GrArchiveShaderMaterial.FromBuiltIn` turns it into a normal
material draw.
