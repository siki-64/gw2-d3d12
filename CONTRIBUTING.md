# Contributing

This repository is one git repository for deliberately separated areas, described in
[`README.md`](README.md). Everything below is the git workflow that keeps those boundaries visible in
history.

## Branches

- `main` is the only long-lived branch. It is expected to build and test at every commit.
- Do work on short-lived topic branches named `<area>/<topic>`, where `<area>` is `client`,
  `dat`, `protocol`, `tools` or `repo`. Research notes and libraries are committed in the `gw2-research` submodule on
  its own branch, and the pointer moves in the same PR.
- Rebase topic branches onto `main`; do not merge `main` back into them. History stays linear, with no
  merge commits on `main`.

## Commits

- One logical change per commit. A commit must leave `main` buildable on its own.
- Write an imperative subject line. Add a body that explains **why** and the evidence, matching the
  existing history (`git log`).
- Prefix the subject with the owning area when it helps (`dat:`, `protocol:`, `client:`, `tools:`); it is
  convention, not enforced.
- Keep a change inside its owning area when practical. A `src/Dat/` or `gw2-research/src/` change that affects a consumer
  must update that consumer's references, comments, CI paths and ignore rules in the same change.
- Never carry build-local facts across builds. A build is one stamp: message ids, RVAs and schema
  chains are recorded per build and re-derived, never copied forward.
- Reference the game build in the body when a contract changes (`205780`, `207109`, ...).
- Do not commit generated binaries, private captures, game assets, or secrets; see *What never gets
  committed*.

## Pull requests

- Open a PR against `main`. Keep it scoped to one area where possible.
- CI is path-filtered:
  - `.github/workflows/original-ci.yml` builds the `src/Dat/` libraries and validates the protocol
    catalog when `src/`, the catalog tooling, the fixtures or the workflow change.
- A PR is ready when CI is green (where it runs) and the change is reviewed.

## Pre-push checks

Run these from the repository root before pushing:

```powershell
dotnet restore gw2.slnx -m:1
dotnet build gw2.slnx --configuration Release --no-restore -m:1 -v:minimal
dotnet test gw2.slnx --configuration Release --no-build -m:1 -v:minimal
dotnet format gw2.slnx --verify-no-changes --no-restore --verbosity minimal --include src tools tests
```

## Line endings and attributes

- Top-level `.gitattributes` normalizes text to LF and marks artifacts as binary. Let git own this; do
  not hand-edit line endings or add per-directory overrides.
- Batch/cmd scripts are pinned to CRLF for `cmd.exe`; everything else is LF.

## What never gets committed

Private captures, memory dumps, `Gw2.dat` or extracted assets, generated assets, session material, and
any credentials or keys. The root `.gitignore` already
excludes these; do not add exceptions.
