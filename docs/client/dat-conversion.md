# Converting `src/Dat` into layouts and routines

`src/Dat` decodes `Gw2.dat` with offset constants and hand-written readers. The exe reads the same bytes as structs
once its loaders have run, so each decoder is replaced by a layout in `Gw2.Exe.Objects` and, where the exe runs logic
on the data, a routine in `Gw2.Exe.Routines`. Both sit in the folder of the `.cpp` file their exe functions assert.
When the last file is gone, `src/Dat` is deleted.

## Where the code ends up

The repository splits into three parts once the conversion is done:

- Game internals: `Gw2.Exe.*`, everything read from the exe and the dat. A private submodule.
- Engine: the renderer and runtime pieces that know nothing about Guild Wars 2. A public submodule later.
- D3D12 client: the application that puts the two together. A public repository later.

So a converted decoder leaves nothing behind in client or engine code that knows a dat or exe offset. Client code
calls the routine and copies the result into its own types (for example `Gw2ColorShift`), and the engine never
references `Gw2.Exe.*`.

## Rules

- Every converted decoder keeps a test against `Gw2.dat`, now in `gw2-research/tests/Gw2.Exe.Routines.Tests`, that asserts
  the same values as before.
- A layout is read through real pointers. Content goes through `LoadedContent`, which relocates the packs.
  Packfile chunks go through the packfile loader once it is ported (below).
- Each object is looked up by the id its type info records: `Find` (uid) or `FindTyped` (data id). Many decoders
  call the uid at 0x14 a data id; the conversion renames it.
- Client code moves from the `Gw2*` records to the layouts, or to a thin client-side view over them, in the same
  commit as its decoder.

## Map

| Group | Files now | Exe source | Becomes |
|---|---|---|---|
| Content objects (cntc) | the former `Gw2ContentObjects` decoders (all converted) | `Gw2\Game\<feature>` | one layout per content type, found through `LoadedContent` |
| Content store | `Gw2CntcPack`, `Gw2CntcRow`, `Gw2CntcArrayReader`, `Gw2CntcObjectHeader`, `Gw2ContentStore`, `Gw2CntcPackCache` | `Gw2\Game\Content\CnData.cpp` | `CnPack` and `CnData` (layouts). `LoadedContent` now takes the decoded pack bytes, resolves each Main block into a `CnPack` and relocates through its fixup arrays, so `Gw2CntcPack` is left only to the dat tools and the pack cache |
| Packfiles | `Gw2PackFile`, `Gw2PackReader`, `Gw2PackSchemaCatalog`, `Gw2AssetDocument`, `Gw2PackDecoders` (`PackObject`, `PackValue`, `PackFieldAccess` moved to `Gw2.Formats`) | `Arena\Services\Packfile\Packfile.cpp` | `Packfile` in `Gw2.Formats` (`Arena/Services/Packfile`, done) opens a decoded file, widens 4-byte-pointer packs, walks its chunks, converts each chunk's relative pointers and upgrades old chunk versions to the newest schema, see `gw2-research/docs/archive/packfile.md`. `Gw2AssetDocument.Parse` now takes its roots from `Packfile.ChunkObject`, the schema view over the upgraded chunk; `Gw2PackReader` remains only for synthetic test catalogs |
| Packfile chunks | `Gw2Map*`, `Gw2Model*`, `Gw2Anim*`, `Gw2Amat*`, `Gw2Zone*`, `Gw2Environment*` and the other chunk decoders | the `.cpp` that reads each chunk | one layout per chunk at its newest version, read from the upgraded chunk. Done: model streaks (`ModelFileData`, `ModelStreakData`), the character composite (`PackComposite`, read by `CpsData`) and the text manifest (`TextPackManifest`) |
| Archive | `FileArchive`, `SplitStore` (`File3`), `ArchiveHeader`, `MftEntry`, `ArchiveCrc`, `ArchiveEntry` (`Archive3`) in `Gw2.Formats` (done); `Gw2DatScanner`, `Gw2DatTypeIndex`, `Gw2DatFileKind`, `Gw2FourCc` left in `src/Dat` | `Arena\Services\Archive3\Archive.cpp`, `File3\FileArchive.cpp` | the archive reader routine |
| Decompression | `ArchiveEntry` (CRC framing and entry header only) | `Arena\Services\Compress\CmpApi.cpp`, `CmpDict.cpp`, `CmpHuff.cpp` | `CmpApi`, `CmpDict`, `CmpHuff` and `CmpBitReader` in `Gw2.Formats` (done) |
| Text and keys | `Gw2Text`, `Gw2TextDecoder` (entry status over `TextDecode` and `CptRc4` in `Gw2.Formats`, done), `Gw2TextVariants`, `Gw2KeyStore`, `Gw2KeyTable`, `Gw2TextKeyCoverage`, `CodedNpcName` | the text manager's `.cpp` | routines; the embedded keys stay data |
| Textures | `Gw2AtexDecoder`, `Gw2AtexBlit`, `Gw2DxtnDecoder`, `Gw2PagedImage`, `Gw2Texture`, `Gw2RawDds` | the texture loader's `.cpp` | routines |
| Identity | `Gw2ManifestIds` left in `Gw2.Dat.Identity` | the file id and name hash functions | `FileArchiveIds` and `Token` in `Gw2.Formats` (done); the client uses plain `uint` ids |

## Not exe code

These have no counterpart in the exe. They move out of `src/Dat`, but not into `Gw2.Exe.*`.

- `Gw2.Dat.Shaders` (DXBC to HLSL decompiler) and `Gw2Sm5*`, `Gw2ShaderReflection`, `Gw2Dxbc*`: build-side shader
  tooling. They move under `tools/`, next to `Gw2.DatSurvey`, which already compiles the shaders.
- `Gw2Dxt5Encoder`: an encoder the client uses for its own textures. It moves to `Gw2.Client.Graphics`.
- `Gw2CntcContentAccess`, `Gw2TextKeyCoverage`: survey instrumentation. These move to `tools/Gw2.DatSurvey`.

## Order

1. Content objects, because `LoadedContent` already relocates them. Done: blend trees (`AbtBlendTreeBlueprint`) mount definitions (`MountDef`) and movement packages, settings and modifiers (`MovementPackageDef`, `MovementSettingsDef`, `MovementModifierDef`), content constants (`ConfigDef`) and character creation configs (`CharCreateConfig`, with `CharConst` for the default hair), color palettes (`ColorPaletteDef`, found by name through `LoadedContent.FindByName` and resolved by `CharConst.FindColorSet` and `DyeManager.ResolveColor`), and day and night cycles (`DayNightCycleDef`, picked per map through a first `MapDef` by `TodCliContextRoutines.PickCycle` and weighed by `TimeOfDayConst`), and map definitions (`MapDef`, found by map id through `CnContext` slot 53; callers outside the exe code use `MapDefView`), and the map layout chain (`MlContinentDef` through `MlMapDef` and `MlDataFloorDef`, walked by `MlCompassPlacement.Find` into an `MlMapPlacement`, which the client's `Gw2CompassPlacementView` copies into `Gw2CompassMapPlacement`), and effect definitions (`IEffectDef` with its kind payloads `EffectModelDef`, `EffectWeaponTrailDef` and `EffectCompoundDef`), and items and skins (`ItemDefinition` with its sub-records and `SkinDef`; client code uses `ItemView` and `SkinView`), and skills, skill palettes, professions, specializations and traits (`SkillDefinition`, `SkillPaletteDef`, `ProfessionDef`, `SpecializationDefinition`, `TraitDefinition`, read by `SkillContent`; the bundle palette lookup is open, see `gw2-research/docs/gameplay/skills.md`). No `Gw2ContentObjects` decoder is left, so the store and the content survey are gone.
2. The packfile loader, then the chunk decoders, one chunk at a time.
3. The archive reader, decompression and text, which everything above sits on. These come last because every test
   uses them to open the dat. Done: decompression (`CmpApi`), RC4 and its key derivation (`CptRc4`, also used for
   encrypted content packs) and string decryption (`TextDecode`), in `Gw2.Formats`. That project references nothing,
   so `Gw2.Dat.Archive` calls it without a cycle; `Gw2.Protocol`'s `TransportCipher` still has its own key
   derivation, which zero-pads and truncates keys where `CptRc4_DeriveKey` repeats and folds them.
4. The tooling moves, then `src/Dat` is deleted.

## Project references

`Gw2.Formats`, `Gw2.Exe.Objects`, `Gw2.Exe.Routines`, `Gw2.Protocol`, `Gw2.Exe.Handlers` and `Gw2.Exe.Live`
reference only each other (and the Crystal source generator). The values the client shares with them live in
`Gw2.Formats`: `Profession`, `WeaponType`, `GameMode`, `SkillSelection`, `SkillEffectEvents`, `Token64` and
`NameHash`. The Exe routine and handler tests open the dat through `FileArchive`, `LoadedContent.Open`, `TextApi`
and `Packfile` only. `Gw2.Formats.Tests` still references `Gw2.Dat.Archive`, which it uses as the reference
decoder in its cross-checks.
