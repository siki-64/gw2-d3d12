# Reusable GW2 archive decoding

Reference `Gw2.Dat.Content.csproj` from a tool or future client; it brings in `Gw2.Dat.Archive` (archive,
compression, pack container, text and texture decode) and `Gw2.Dat.Identity`. The libraries have no
native calls, process attachment, renderer, or game-build gate.

```csharp
using Gw2.Dat.Archive;
using Gw2.Dat.Content;

using var archive = FileArchive.Open(datPath);
using var asset = new MemoryStream();
archive.DecodeFileId(fileId, asset);
byte[] decoded = asset.ToArray();
if (Gw2CntcPack.LooksLike(decoded))
{
    var content = Gw2CntcPack.Parse(decoded);
    foreach (var root in content.Objects)
        Console.WriteLine($"{root.Type:X}: {root.Offset:X}");
}
```

## Layers and ownership

- `FileArchive`: read-only file access, header/MFT/ATOC, cached file-ID lookup,
  raw extraction and decoded stream output. Record indices are zero-based;
  ATOC MFT indices are converted once. Positional reads permit concurrent callers
  with separate destination streams. Dispose only after those callers finish.
- `ArchiveEntry`: pure managed byte/span-to-stream routines, including
  Method 0 Huffman/LZ and Method 1 delta decoding. Method 1 requires the matching
  old decoded source supplied by the caller. The legacy class name is retained.
- `Gw2DatScanner`: record classification and bounded prefix capture. It still
  decodes the full record; a bounded prefix does not mean bounded decode work.
- `Gw2CntcPack`: indexed content roots, strings, and fixup queries over decoded
  PF/cntc data. Keep its backing memory unchanged for the lifetime of the view.
  Packs flagged encrypted are decrypted with the global content key first
  (`gw2-research/docs/archive/encryption.md`).

Destination streams belong to the caller and remain open. Decode errors can
leave partial output; publish an asset only after decoding succeeds. Compressed
entries currently buffer the raw entry and stripped input; decoded output is
streamed with a bounded LZ history. Use stream output for large assets.

## Coverage and remaining work

This is an archive-decoding foundation, not a complete client asset pipeline.
Supported header lengths are 0 (stored) and 8 (compressed), the only values in
the archive; others fail explicitly. Entry and run CRC words are verified when
the archive is opened with checksum verification. Stored-entry framing is
validated against the full archive (`gw2-research/docs/archive/compression.md`). The file-ID cache is a snapshot:
use a stable archive and reopen after updates.

Content-store parsing does not deserialize every content class or resolve all
cross-file references. `Gw2Texture.DecodeGpuBlocks` expands AN mip coding to GPU
block bytes (not RGBA); `Gw2DatScanner` classifies texture records; `Gw2Model`
assembles a MODL bundle into meshes, the material permutation and the material /
texture dependency file ids; `Gw2AmatMaterial` decodes an AMAT pack's `GRMT` header (the ordered `texTokens` texture parameter
table) and `BGFX` chunk (shader binaries, shader constants, sampler declarations, sampler states and
technique/pass/effect state); each shader's type code distinguishes vertex (1) from fragment (16), and
`Gw2ShaderReflection` reads the reflected constant/sampler table from the shader blob,
`Gw2Dxbc`/`Gw2ShaderProgram` decode the embedded DXBC SM5 instruction stream (opcodes and operands,
including each operand's component swizzle),
`Gw2ShaderUsage` slices sampled resources back from an output write and classifies each as base colour,
direction (a value feeding a dot product) or normal (a signed `2x-1` remap before the dot), and
`Gw2AmatMaterial.AnalyzeEffects` / `OutputResources` / `ResourceRoles` apply that to each effect's pixel
shader, and `TryResolveEffect` selects an effect by requested key using the renderer's recovered
fallback chain (build-local tokens), with `OutputResources(pixelShaderIndex)` /
`ResourceRoles(pixelShaderIndex)` / `MaterialResources(index, pixelShaderIndex)` /
`MaterialRole(index, pixelShaderIndex)` for variant-scoped classification, and the token-scoped
`MaterialResources(textureToken)` / `MaterialRole(textureToken)` / `TryGetTextureSampler` resolve a
material texture's role and sampler from its authored parameter token (`Gw2MaterialTokens`) and the
`AmatSamplerConstant.textureIndex` -> `AmatGr.texTokens` link, rather than from array position or the
bytecode hint; and
`Gw2MapInfo` / `Gw2PortalManifest` / `Gw2MapProps` /
`Gw2MapTerrain` / `Gw2MapCollision` / `Gw2ModelCollision` read a `mapc` bundle's GUID, bounds,
terrain heights, prop placements and collision meshes (the map collision resolves a placement's
geometry through geometry -> animations -> collision/blocker soups, local-space and placed by the
prop/zone/obs transform) plus a `prlt` portal manifest's start points
and preload models, with the recovered terrain world scale (`Gw2MapTerrain.ChunkWorldSize` = 3072
world units per 32-tile chunk, i.e. 96 world units per height sample), the per-chunk `uvData` layer
UV animation (`Gw2MapTerrainUvData`: a per-second scroll rate, bouncing scale ranges/speed and a
per-update rotation quaternion pushed to the model's texture-transform slot 2) and the map `rect`
bounds.
`Gw2MapDefinition` (content type `0x2D`) carries a map's id, name text id and the base id of its
map file; `Gw2ContentReader.ReadMapDefinitionsOfMapFile` lists the MapDefs that use a map file. The layouts are recorded in
`gw2-research/docs/models/model-content.md` and `map-content.md`. Terrain and
audio decoders, skeleton semantics, content-class coverage,
cross-file reference resolution, schema/version dispatch and renderer-ready resources
remain separate work. Texture dependencies are not always ATEX containers: a model's base map can be a
raw uncompressed `DDS ` blob (observed at file id `0x0002FB32`), which `Gw2RawDds` recognises and the
texture export passes through unchanged. Do not interpret an indexed root's estimated byte extent as a
recovered class layout. No proprietary archive fixtures are included.
