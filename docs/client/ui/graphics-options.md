# Graphics Options

The Esc menu opens a Graphics Options window. The client keeps its graphics settings in its own file,
`%LocalAppData%\Gw2ClientRemake\GraphicsSettings.xml`, and never writes the game's `GFXSettings.Gw2-64.exe.xml`.
The renderers differ, and the client has settings the game does not.

- `Gw2GraphicsSettings.All` lists every setting: key, label, kind (choice, toggle, amount), whether it applies while
  the client runs, and how it reads and writes `Gw2GraphicsOptions` plus the frame limit.
- The window groups the settings into Display, Detail, Lighting and shadows, Post-processing and Performance. Settings
  the game does not have (volumetric fog, physical bloom, the extra post-processing effects, Hi-Z, parallel recording,
  GPU-driven rendering) sit in the group they belong to.
- Shaders go up to Ultra. The dat carries `ultra` techniques that the game never selects.
- The frame limiter offers 30, 60, 120, 144, 240, 360 and Unlimited. The game's `unlimited` is a 240 cap, so it seeds
  240; the client's Unlimited (`uncapped` on the command line) does not wait at all.
- On first start, when the client's file does not exist yet, the values are taken from the game's `GFXSettings` (read
  only, `Gw2SavedGraphicsSettings`). From then on only the client's file is used.
- Every change is saved at once. Settings marked `*` (shadows, textures, LOD, sampling, shaders, vertical sync,
  screen-space shadows, GPU-driven rendering) apply after a restart, because they decide how passes and resources are
  built at startup. The rest apply immediately.
- Command-line switches still override the file for that run. Runs with `--benchmark`, `--flight` or `--screenshot`
  ignore the file.
