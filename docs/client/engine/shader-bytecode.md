# Shader bytecode: new client

How the new client implements [Shader bytecode](../../../gw2-research/docs/engine/shader-bytecode.md). The retail behaviour and its evidence are in that note.

## In the remake

`GrShaderEffectDescription` describes a vertex and fragment `Gw2BgfxShaderBinary` as a Crystal
`ShaderProgramDescription`. Crystal builds the root signature from it and hands the bytecode to the pipeline state
as-is:

- Bytecode: the checksummed container (`ChecksummedCode`), the same bytes bgfx passes to D3D.
- Constants: one buffer per stage (`VertexConstants`, `FragmentConstants`) of the trailer size, at the register of
  the stage's `dcl_constantbuffer` (b0 in every binary). Each bgfx constant uniform becomes a float4 variable,
  or a float4 array when it spans several registers, named as in the binary. The uniform's register field is a byte
  offset (the water material's `World` sits at 64, `WorldView` at 128). A name used by both stages is one shared
  parameter.
- Textures: one resource per `dcl_resource`, named after the sampler uniform on the same register, with the
  declared dimension. A vertex texture that shares a name with a different fragment texture gets a `Vertex` prefix.
- Samplers: one per `dcl_sampler`, named `<texture>Sampler` after the texture on the same register.
- Vertex inputs: taken from `ISGN` with their registers. The archive vertex layouts (`GrVertexStatic`,
  `GrVertexTerrain`) name the bitangent `BITANGENT`, as the archive shaders do.
- Effect key: FNV-1a over both codes, like `BgfxShader_HashBinary`. `GrDevice.ShaderEffect` caches one program per pair,
  like `BgfxShader_GetProgram`, and binds a transparent 2D or black cube fallback to every texture until a material binds
  its own.

`GrShaderEffectDescriptionTests` checks the description of the above-water material (187551) on the real `.dat`.

`GrArchiveShaderMaterial` picks the highest-quality technique, its default effect, and vertex variant 0 (static;
the client skins on the CPU). Material textures go to the material's own resources (the body binds the composite atlas
and normal atlas). Engine textures come from `GrEngineTextures`, and the global constants from `GrEngineConstants`.

The body pixel shader writes `StencilId` (`cb0[1].x`) to output alpha, so `--screenshot` saves opaque pixels.

## Global defaults

`GrEngineConstants` holds these values.

## Texture references and binding

The remake: `GrDrawTextures.Bind` binds the samplers of the fragment program, and
`Gw2AmatSamplerState.WithAnisotropicFiltering` does the upgrade.

The remake adds vertex references to `GrArchiveShaderMaterial.Samplers` with `IsVertexStage`: item references read the
mesh's texture at the slot, engine references bind the black fallback (the exe's empty-slot case), and
`GrDrawTextures` sets `VertexSamplerStates`.

## Who binds the view engine textures

The remake samples the normal/spec pass's depth buffer the same way (a depth view of the render target in Crystal) instead of drawing the scene a second time.

## Engine texture slots in the color passes

`GrScreenCopy` (`BgfxDraw_ScreenCopy`) copies the main color into the screen copy when decals or distortions follow.

The remake binds the same set in `GrEngineTextures`: the depth copy (3, the light buffer's depth target), the light
buffer (2), the far shadow (5) and the cascade shadow map (7). The remake does not copy the depth target, because its
color pass draws into a different target than the depth it reads. `GrCharacterPass.Draw` draws every opaque draw
first, then copies the main color into the screen copy (`GrScreenCopy`, a full-screen copy) and binds it as slot 4 for
the blended draws, which follow.

## Dynamic shadow cascades (WinShadow.cpp)

`Gw2.Client --shadows none|low|medium|high|ultra` builds the settings with `GrShadowSettings.FromGraphicsOptions`
(resolution `256 << option`, count clamped to 1..3).

## Contact shadows (Screen Space Shadows)

`GrContactShadowSettings` holds the block. `Gw2.Client` turns them on by default (`--no-screen-space-shadows` turns
them off).

The slope bias is applied in `GrSceneDepth.fx` as `slope · max(|ddx z|, |ddy z|)`. Nothing that reads `+0x14D034` was
found statically. `GrLightBuffer` runs it the way PostProcLightBuffer does:

`GrSceneDepth.fx` writes clip depth into a second target, standing in for the hardware depth buffer.

## Technique, effect and variant selection

The remake: `Gw2AmatMaterial.TechniqueIndexFor`, `Gw2AmatEffectKeys.FallbacksOf` and
`Gw2AmatEffect.VertexShaderVariantFor`. `GrArchiveShaderMaterial.Create` uses High by default; `--shader-quality
low|medium|high|ultra` overrides it for prop and character materials, so ultra can be drawn although the game never
selects it.

## Draw state

The remake's terrain casters cull the same way (`RasterizerState.CullBack`).

The remake: `Gw2AmatDrawState.Of(shaderPassFlags, passIndex, effectKey, drawFlags, viewDepthBias)` decodes the rules.
`GrDrawStates` maps them to Crystal depth-stencil, blend (colour write channels) and rasterizer (depth bias) state.
`GrCharacterPass.DrawMaterial` applies them outside the debug views.

The remake's shadow maps do not go through the material path.

## Passes

The remake: `GrArchiveShaderMaterial` holds pass 0 and `LaterPasses`, one shader material per pass, each with its own
effect, samplers and draw state. `GrCharacterPass.DrawMaterial` draws them in order (pass 0 only in the debug views).

## Sort buckets

The remake: `Gw2AmatMaterial.SortOrder`/`SortBucket` and `Gw2SortBuckets` (`Gw2SortBucketTests`). `GrCharacterPass`
collects terrain, props, instance groups and character meshes, then sorts them by bucket and sort type. It draws
buckets 1-8, then water, then the screen copy, then buckets 9 and up.

The remake never releases materials or texture lists, so its numbering matches this allocator; the remaining
difference is load order, which follows the exe once content streams in the exe's order (ROADMAP step 1).

The remake does the same: a bucket-11 material adds its first pass to bucket 12 and its later passes to bucket 13
(`Gw2SortBuckets.SplitPasses`, `GrCharacterPass` `GrPassRange`).

## Effect render state

`Gw2AmatRenderState` decodes the flags, `GrBlendStates` maps them, and blended meshes draw after opaque ones.

## Sampler states

`Gw2AmatSamplerState` decodes the flags and `GrSamplerStates` maps them to Crystal sampler states.
