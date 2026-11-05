# pack-layouts

Byte layouts of `.dat` pack chunks, in the style of `gw2-research/src/Gw2.Exe.Objects`: explicit structs with field offsets,
one file per chunk type and version, in namespace `Gw2.Exe.Objects.Dat`.

- **Generated:** `tools/re/GeneratePackLayouts.py` writes the structs from a catalog written by `tools/re/ExtractPackSchemas.py <exe> <catalog>`.
  Offsets follow the size rules of `Gw2PackReader`: fields are packed without padding, and pointers are relative
  to the field that holds them. The layouts aren't committed; generate them when needed. `--check <directory>`
  regenerates into a temporary directory and fails on any difference.
- **Selection:** `--chunk` and `--version` pick the registration; `--root` picks the root type when several pack
  types register the same chunk (as `MODL` does). A type name that occurs twice in one tree gets its schema
  address as a suffix.
- **Versions:** a root type shared by several versions gets one file; the index lists its versions. When two
  different root types share a name, the file name gets the lowest version that uses it.
- **Shared structs:** a struct generated identically for two or more root types is written once, to
  `Shared.cs` in namespace `Gw2.Exe.Objects.Dat`, together with the structs it references. Per-file
  namespaces are children of it, so they see the shared structs; a struct whose fields differ between versions
  stays in each file that uses it. The index counts all structs of a root type and the ones its file defines.
- **Pointer size:** layouts are for 64-bit packs. `Gw2PackLayout.cs` holds the shared array, pointer, string and
  vector types at 64-bit sizes.
- **Not compiled:** no project includes these files or `Gw2PackLayout.cs`. `Gw2PackArray<T>` names the
  element type for reading; its real layout is the count and the relative offset.
- **Generated only:** enums and `[Flags]` types for field values live with the decoders in `src/Dat/Gw2.Dat.Content`,
  with the evidence in `gw2-research/docs/content/` and the subject notes.

Every root type the catalog registers, one file per root type, grouped by family (the root name without its
version suffix):

```
python3 tools/re/GeneratePackLayouts.py --catalog <catalog> \
  --all <directory>
```

A single root:

```
python3 tools/re/GeneratePackLayouts.py --catalog <catalog> \
  --chunk lght --version 19 --root PackMapLights --namespace Gw2.Exe.Objects.Dat.MapLights \
  --output <file>
```

## Files

The generated `INDEX.md` lists every file with its family, chunks, versions and struct count: 547 root types, 7266
structs, 45511 fields. It covers every registered version, including versions that never occur in the archive.
