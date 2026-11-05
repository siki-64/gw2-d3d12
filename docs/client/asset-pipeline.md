# Archive decode stages

Content goes from the local `Gw2.dat` to renderer-facing objects in memory; there are no `.dds`, `.verts`,
`.indices`, `.height` or manifest intermediates.

| Assembly | Responsibility |
| --- | --- |
| `Gw2.Dat.Content` | Opens the `.dat`, decompresses records, parses pack containers and dispatches by chunk type (`src/Dat/Gw2.Dat.Content/`). |
| `Gw2.Client.Model` | Builds the client objects (`Gw2Model`, `Gw2Mesh`, `Gw2Material`, animations, zone scatter) from the parser output, including vertex-buffer decode and material texture-role resolution. |

The `Gw2.DatSurvey` tool runs the coverage surveys, the shader disassembly and the key coverage report, and writes the content pack cache. It exports no assets.

Positions stay in the archive's z-up game units; there is no axis or scale conversion.

## Content facts

- Texture block data, authored mip chains and block formats are carried through unchanged.
- Uncompressed raw-DDS records are read through their DDS channel masks; `DXT2`/`DXT3` use the explicit-alpha
  block layout.
- Terrain surface and normal palettes mix DXT1 and DXT5 slices of different sizes ([terrain.md](../../gw2-research/docs/maps/terrain.md)).
- Water is a plane spanning the map bounds at `waterPlaneZ` (the fall-catch water every map carries) plus one
  polygon per decoded `watr` entry, tinted by `PackMapEnvDataWater` ([water.md](../../gw2-research/docs/rendering/water.md)).
- The game places glyphs with per-band ink profiles instead of stored advances ([fonts.md](../../gw2-research/docs/ui/fonts.md)). A range
  file that fails to load is skipped.
- Zone scatter is the bounded reconstruction in [zone-scatter.md](../../gw2-research/docs/maps/zone-scatter.md).
- Archive IDs are normalized to their base ID, so a zone looked up by its file ID (`0x003139A8`) is base ID
  `0x0002DCDB` ([identity](../../gw2-research/docs/archive/identity.md)).

## Tests

`tests/Client/Gw2.Client.Model.Tests` tests `Gw2.Client.Model` against the archive.
