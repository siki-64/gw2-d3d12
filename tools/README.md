# Reverse-engineering tools

Reverse-engineering tooling for the reference image. Only
`Gw2.LiveMemory` reads a running retail client, read-only, to log and debug protocol and game state; nothing here
produces a wire codec.

## What was checked, reused and added

Before adding tooling:

| Capability | Where it lives | Decision |
| --- | --- | --- |
| Decompilation, xrefs, call graphs, strings, types, structure synthesis | **IDA Pro** (plus the IDA MCP for agent navigation) | Reused. Not wrapped, not duplicated. |
| vtable recovery from constructor anchors, assert-string anchors, master dispatch-table inspection | **IDA** workflow | Reused. Repeated exports that feed repository artifacts live in `ida/`. |
| Offline PE/disassembly inspection | IDA/MCP | Reused. No disassembler is shipped here. |
| Archive/texture/schema decoding | `src/Dat/` (its own tests) | Reused unchanged. |
| Byte-level opcode/schema probing against a live process | external debugger / live inspector | Not reimplemented. See *Live inspection* below. |
| Evidence bundle integrity | `tools` | **Added** as one operator wrapper `gw2re.ps1`, now shared under `tools`. It is the thin generator/consumer the methodology permits: it produces and checks maintained repo artifacts only. |

## Layout

| Path | Purpose |
| --- | --- |
| `gw2re.ps1` | Single operator wrapper. Every command takes explicit input/output paths and stamps the game build into its output. |
| `ida/gw2re_ida.py` | Shared helpers for the IDA scripts, built on the IDA Domain API (`ida_domain`): the image identity block and the build the image reports, name and byte maps, instruction and operand helpers, callee walks, decompiled dumps, JSON output. |
| `ida/export_anchors.py` | IDA Python script. Reads the analyzed image and writes the anchor/vtable seed JSON for lead lists. |
| `ida/export_asserts.py` | IDA Python script. Walks every call to the seven assert helpers and recovers each site's expression, `...\Code\...` source path and source line as JSON. The helper table is the one positional fact here; the expression, path and line are evidence, not coordinates. Output is git-ignored. |
| `ida/export_source_files.py` | IDA Python script. Places each function in a `\Code\` source file from the assert export: its own most asserted `.cpp`, else the file of the placed functions on both sides (the linker keeps one object file's functions together; this agreed with every own assert in a 5000-function sample), else the one bordering file a caller is placed in, else its own header. Callers or callees alone are not used: they named the wrong file 40% and 72% of the time. Output is git-ignored. |
| `ida/export_vtables.py` | IDA Python script. Finds constructors by their virtual-pointer stores and writes the tables they install: one entry per constructor store, one per vtable with its slot run. A store counts only while the register still holds the address its `lea` loaded, so a placement is never credited to an address the register has already lost. Output is git-ignored. |
| `ida/classify_classes.py` | IDA Python script. Combines the vtable and assert exports into the class model: for each constructor the class it builds, its RTTI name, the vtables it installs with their vptr offsets, and the source folder of the class, plus a per-vtable index carrying the assert path rolled up from its slot targets. See [`gw2-research/docs/engine/class-model.md`](../gw2-research/docs/engine/class-model.md). Output is git-ignored. |
| `ida/classify_contexts.py` | IDA Python script. Reports which `PropContext` slots its direct callers read, and which folder each slot's callers come from. The collection accessor is derived from the image, never hard-coded as an address. Output is git-ignored. |
| `ida/export_layout_anchors.py` | IDA Python script. Reads the anchor comments in `gw2-research/src/Gw2.Exe.Objects`, decompiles each anchor function, the first four slots of each anchor vtable and their direct callees, and writes one `.layout-anchors.c` text file grouped by struct, with referenced strings. Read-only on the database; output is git-ignored. |
| `ida/export_prop_context.py` | IDA Python script. Finds the `PropContext` TLS accessor by its instruction shape (`gs:[58h]` TLS array read, load of `+0x10`), the destructor by its `context != GetContextSafe()` assert, and the create and destroy registration functions by their table store and the destructor's destroy-table reference; no addresses are hard-coded. Then finds every slot registration, recovers the slot index from `ecx` and the create or destroy callback from `rdx`, and decompiles each callback with two levels of callees plus the collection accessor and destructor. Defines a function at each callback address IDA has not analyzed yet; nothing else in the database changes. Output is git-ignored. |
| `ida/export_handler_chains.py` | IDA Python script. `[output] [ids]` (ids like `game:39,login:2`). Starts at each handler in a receive handler table written by `Gw2.CaptureDecoder --receive-handlers <out.csv>` (first argument) and follows the record through the decompiled code: the object a handler allocates and builds from it, each `record+offset` copied to `object+offset`, the reader and validator calls, the vtables the builder stores, their execute slot (2) and two levels of callees, and the virtual calls on the receivers it reaches. Values are typed against `gw2-research/src/Gw2.Exe.Objects`: a builder whose vtable is named `<Layout>_vftable` types its object, `Get<Layout>` and `<Layout>_Get` return their layout, and offsets, `[Base]` subobjects and `[Slot]` declarations become field and slot names; an offset that stays numeric has no layout yet. Read-only on the database; output is git-ignored. |
| `ida/layout_types.txt` | Tracked manifest for `ida/export_layouts.py`: the IDA type names to emit, one per line. Adding a layout type is one line. |
| `ida/export_layouts.py` | IDA Python script. Reads `ida/layout_types.txt` and the open database, then regenerates the pure-layout `.cs` files under `gw2-research/src/Gw2.Exe.Objects` and `gw2-research/src/Gw2.Exe.Objects/Enums` in place, each stamped with its build. The build comes from the image itself: the number the client prints in its startup banner is followed to the constant that returns it, then cross-checked against the digits in the image file name. Offsets, sizes, padding, member order and `InlineArray` counts and elements come from the IDA types; the C# member spellings, constants, enum origin, method tables (`vtable=`/`slot=`) and nesting, which a C/C++ view cannot spell (`nint`/`nuint`, `System.Numerics.Vector4`, nested `X.Y` names), ride along in each type's comment. Equivalence is therefore at the layout level, not textual. Files that also hold C# code (extension or constant helpers) are hand-maintained and not regenerated. |
| `ida/import_layout_types.py` | IDA Python script. Reads the structs and enums under `gw2-research/src/Gw2.Exe.Objects` and builds them as local types in the open database, replacing any of the same name, so a new build's database starts from the reference layouts. A layout IDA cannot reproduce at the same offsets and size is left out and counted in the printed outcomes. Also writes `ida/layout_types.til` (not tracked). An optional argument limits the run to files whose path contains it. |
| `ida/export_symbols.py` | IDA Python script. Reads the open database and writes `gw2-research/research/<build>/symbols.json`: the identity block (build, file name, hashes, base, span) beside the image's function starts and the names its database marks. Tracked output, and the only place in the repository an exe address is carried. |
| `ida/verify_layouts.py` | IDA Python script. Checks the open database against `gw2-research/src/Gw2.Exe.Objects`: every `_vftable`/`_vbtable` name resolves to a virtual layout by the naming rule in that project's README, each vtable has its layout's `SlotCount`, a function that references each table stores it at the layout's offset (through the vbtable for a virtual base), and every top-level virtual layout has a named table. Prints the problems or `ok`; read-only. |
| `ida/port_table_names.py` | IDA Python script. `sequence <path>` writes the open database's referenced `.rdata` tables in address order with their slot counts, vbtable entries and names. `port <path>` aligns that sequence from the reference build with the open build's, names the matching tables and runs `verify_layouts.py`. A gap of the same length in both builds is matched by position, so a table whose slot count changed is still named and reported. The sequence is not tracked. |
| `ida/export_tokens.py` | IDA Python script. Walks every call to the constant-name hash, recovers the name string argument and writes the constant-token registry JSON; tokens are computed with the name hash (`Token.Encode`). |
| `ida/export_content_types.py` | IDA Python script. Finds the content accessors by their four thunk shapes and writes the accessor table and every `CONTENT_TYPE_` assert string as JSON; output is git-ignored. |
| `ida/export_content_type_names.py` | IDA Python script. Finds the assert helpers from the error reporter's source string, then walks every content lookup that passes the type as an immediate, every per-type def walker and every comparison of the content type field against an immediate, and writes each one whose result is asserted, with the type, the asserted name, the function and its source files. Output is git-ignored. |
| `ida/export_shaders.py` | IDA Python script. Writes the embedded engine shader source strings as JSON. |
| `re/Gw2TransportCipher.py` | Reference transport cipher (key schedule + PRGA). |
| `re/Gw2HandshakeKdf.py` | Reference handshake key derivation (DH exchange). |
| `re/ObjectLayoutCatalog.py` | Parses `gw2-research/src/Gw2.Exe.Objects` into types, fields, enum members and constants for the checks below. |
| `re/CheckLayouts.py` | Layout consistency: overlaps, `Size`, alignment, the build stamp against the image build in `gw2-research/research/<build>/symbols.json`, duplicate names and constant values. |
| `re/CheckLayoutReferences.py` | Docs name fields instead of repeating struct offsets; client mirrors in `gw2-research/docs/engine/layout-mirrors.md` match their originals. |
| `re/PortSymbols.py` | `--reference <symbols.json> --target <symbols.json> --output <json>`. Maps one build's functions and data onto another's from the two exes: functions whose bytes are unique and equal once branch and RIP-relative operands are masked, then the calls, data references and data pointers of matched pairs, and runs of the same length between matched functions when their size and references agree. The map is written under `artifacts/`. |
| `re/CheckNoteCoordinates.py` | Every function, data and address citation in `gw2-research/docs/**/*.md` resolves in the reference build's `gw2-research/research/<build>/symbols.json`: a function start or a named address inside the image, and a named function is cited by its name. |
| `re/CiteNames.py` | Replaces each `sub_` citation in tracked `.md` and `.cs` files with the reference build's database name for that address from `gw2-research/research/<build>/symbols.json`. Run it after naming routines and regenerating the symbols. |
| `re/MakeSyntheticFixtures.py` | Builds the synthetic 208784 `0x264` inbound and `0x11f` outbound wires. |
| `Gw2.TransportFixtures/` | Builds the captured protocol fixtures from a session recorded by `Gw2.LiveLogin --record-transport`; see `tests/fixtures/README.md`. |
| `Gw2.LiveMemory/` | Research only, for logging and debugging protocol and game state; the client never reads the retail process. Attaches read-only to a running 208784 client, finds its `PropContext` and dumps the controlled character, agent and health through the layouts; see `gw2-research/docs/engine/prop-context.md`. |
| `../tests/fixtures/<build>/` | Small synthetic byte fixtures the client tests use. The captured protocol fixtures are in `gw2-research/tests/fixtures/<build>/`; each file's `kind` says whether it is captured or synthetic. |
| `../gw2-research/tests/Gw2.Exe.Protocol.Tests/` | Offline MSTest harness that validates `src/Protocol/Gw2.Protocol` against the protocol fixtures in `gw2-research/tests/fixtures/<build>/`. |
| `README.md` | This file. |

Generated or reviewed outputs are checked in; private inputs (captures, dumps, exported JSON) are not.
`artifacts/` and `captures/local/` at the repository root are ignored by Git.

## Commands

Run `gw2re.ps1 <area> <action>` from the repository root (as `.\tools\gw2re.ps1`). Its path
defaults and explicit `-Path`/`-Manifest` values resolve relative to the repository root. Every command prints `info`/`warning`/`error` lines
and exits non-zero on any error. Add `-Json` for one machine-readable result object instead, but not
together with warning escalation. `-Strict` turns warnings into a non-zero exit.

```powershell
# Evidence --------------------------------------------------------------------------------

# Import a private bundle (capture directory or a single file), recording build, hashes and timestamps
.\tools\gw2re.ps1 evidence import -Path .\captures\local\208784 -Build 208784 `
    -BuildLabel 208784 -Manifest .\artifacts\manifests\208784.json `
    -Process gw2-64.exe -RuntimeBase 0x7FF600000000

# Re-hash every manifest entry against the bytes on disk
.\tools\gw2re.ps1 evidence verify -Manifest .\artifacts\manifests\208784.json

# Which bundle produced this file, and which artifacts cite its hash?
.\tools\gw2re.ps1 evidence provenance -Path .\captures\local\208784\gw2-64.memdump
```

| Parameter | Default | Used by |
| --- | --- | --- |
| `-Build` | required | `evidence import` |
| `-Manifest` | required | `evidence verify`, `evidence provenance` |
| `-Exclude` | `artifacts/ida-exports/**`, `artifacts/manifests/**` | `evidence import` |
| `-WireEligible` | off | `evidence import`, payloads only |

## Evidence rules the tools enforce

- **No copies of native memory.** `evidence import` never records a dump as wire bytes. `--wire-eligible`
  on `asset`/`record` entries is only accepted when the representation is `wire-bytes`.
- **Unknown fields survive.** Ciphertext, compressed payloads and unparsed tails are recorded as
  `opaque` / `unknown` fields with raw hex preserved, never dropped.
- **No live-server claims.** `validation.liveInterop` stays false unless a controlled interoperability run
  was performed; synthetic and replay results are recorded separately.
- **Anonymous, hashed sources.** `sourceReferences` record a kind, a label and a SHA-256, not a raw capture
  path, so no capture content or filename leaves the ignored workspace.

## IDA scripts

IDA scripts live in `ida/` and target IDA Pro 9.4 with IDA Python. They use the IDA Domain API (`ida_domain`)
rather than the flat `idc`/`idautils`/`ida_*` SDK modules: same database, far fewer round trips per instruction.
Run them from the IDA MCP (`execute_python` with `runpy.run_path(...)["export"](outPath)`), from *File > Script
file* in IDA, or headless with `idat -A -S"tools/ida/export_anchors.py <outPath>" <database>.i64`.

Every script exposes one entry point, `export(...)`. Its first argument is the output path, and any later
arguments are its inputs, so a script can be re-run against a different database or a different build's export
without editing the file. Paths resolve relative to the repository, never to a fixed absolute directory. Without
an output path a script writes into `artifacts/ida-exports/`, named after the database module plus a suffix, so two
builds never overwrite each other. `export(...)` writes the whole artifact to that file and returns only a summary
(counts plus the artifact path), never the payload: a caller over the IDA MCP gets a small result, not megabytes.
Each one is listed in the Layout table with its inputs, side effects on the database and output. Output files are
git-ignored; a script that feeds a repository artifact names the promotion and validation step in the relevant
reverse-engineering note.

Notes, layouts and data cite a routine by its database name, `{SourceFile}_{Role}` (`GcAuthCmd_SendLogin`), the
file taken from its asserts or from its neighbours in the same object file. A routine without a name yet keeps IDA's
default with uppercase hex: `sub_1409B1E00` for a function, `j_sub_...` for a thunk, `unk_1428931B8` for data. Both
are build-local coordinates, not symbols.

## IDA anchor export

`ida/export_anchors.py` reads the open IDA database and writes one JSON file with:

- the image identity (program name, executable MD5/SHA-256, processor, image base, span);
- assert/source-path anchors, from the `...\Code\...*.cpp` strings;
- vtable candidates: runs of three or more consecutive pointer slots whose targets are functions.

It does not rename, retype, comment or analyse anything, and it decides no class semantics. Treat the
result as a lead list. Absolute addresses stay coordinates inside one analysed build; record the export as
an `image-anchor-export` source reference in the artifact whose evidence it supports.

## Tracked versus local

| Artifact | Location | Tracked |
| --- | --- | --- |
| IDA exports (`export(...)` artifacts) | `artifacts/ida-exports/` | no |
| Evidence bundle manifests | `artifacts/manifests/` | no |
| Private captures, module snapshots, session material | `captures/local/` | no |
| IDA database, `.i64`, `.id0`/`.id1`/`.id2`/`.nam`/`.til` | anywhere local | no |

## Live inspection

Live tracing and byte-level schema probing stay in a debugger, because that is where watchpoints,
hardware breakpoints and interactive stack walking already work. An external inspector that decodes the
schema table and logs opcode/handler/schema triples is useful as an *external* cross-check for
build-sensitive addresses; nothing from it is vendored, and none of its addresses are durable identities
here.

## Not implemented here, on purpose

- No x64 disassembler, PE parser or relocation engine: IDA owns decoded inspection.
- No scanner, AOB matcher or locator library: reusable locators belong in the discovery layer once one
  exists, and every image-coordinate case here would duplicate IDA byte search.
- No injector, hook engine, overlay or renderer: those are tied to the official client's in-process
  execution and are out of scope for this tooling tree.
- No live byte probing: that needs watchpoints and hardware breakpoints, which an offline PowerShell
  wrapper cannot provide.

## Target build

The reference image is build **208784**, the build Steam runs: `gw2-research/research/208784` holds `Gw2-208784.exe` and its IDA
database. Exe addresses in the docs are 208784, image base `0x140000000`. Content comes from the 208784
data stream: `gw2-research/research/208784/Gw2-208784.dat`.

The IDA database of the reference image is the truth for everything here, and a capture of it is read the way the
image reads itself: `gw2-research/research/208784/symbols.json` (written by `tools/ida/export_symbols.py`, not committed) records the build the image reports
([engine/identity.md](../gw2-research/docs/engine/identity.md)), the name and hashes it is filed under, the base it is loaded at
(`0x140000000`) and the span of the image.

The previous reference, 208039, lost its database; its names were moved with
[`re/PortSymbols.py`](re/PortSymbols.py) from its last `symbols.json` (in git history) and its exe,
which stays in `gw2-research/research/208039` as the port source.

Nothing build-specific is stored as data: the pack schemas, the protocol schemas, the handshake group and the
addresses `Gw2.Exe.Live` uses (`GameAddresses`, `BgfxLibrary`) are read from the running exe (see [`pack-schemas.md`](../gw2-research/docs/content/pack-schemas.md) and
[`protocol-baseline.md`](../gw2-research/docs/protocol/protocol-baseline.md)).

## Rules

- A build is one stamp. Per-build message ids and addresses are never carried across builds.
- Code finds exe addresses through code anchors at run time (`src/Formats/Gw2.Formats/Executable`). Notes and layouts
  name symbols of the reference image; the local `gw2-research/research/<build>/symbols.json` resolves them.
- Everything derived states which build and base it came from: `image` in an export, `// Build <build>.` in the
  layout sources, the build stamp in the protocol corpora. An artifact stamped with another build is evidence of
  that build, not of this one.
- Protocol contracts are build-stamped, not build-independent: wire ids, schema chains and dispatch ids
  are per-build facts recorded against the image hash and never carried across builds. Model every
  decoded layer as objects — codecs/streams, field trees, handler records, decoded state — and keep
  undecoded bytes as explicit `opaque`/`unknown` fields carrying their raw bytes rather than inventing
  semantics for them.
