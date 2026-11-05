# Benchmarks

Frame-rate measurements of the live client and of `Gw2.Client` at the same pose and settings. The streaming target
([ROADMAP.md](../../ROADMAP.md) step 1) is a frame rate equal to or above live, with both capped at 240
("unlimited" in live), and no frame over 33 ms during a flight across 1595.

Poses are named here. Their coordinates are kept outside the repo, together with the reference screenshots.

## Frame limiter

Live offers the caps 30, 60, 120, 144 and unlimited, which is 240. `Gw2.Client --frame-limit` takes those values plus
165, 200, 360 and uncapped (default unlimited); 165 and 200 sit under common variable refresh rate displays. Vertical sync is off and the client waits after present until the frame
period has passed.

## Benchmark mode

`Gw2.Client --benchmark <seconds>` records frames for that long once the prop and terrain loaders have settled and
`--benchmark-warmup` seconds (default 4) have passed since, prints one result row and exits. `--benchmark-name` labels the pose, `--benchmark-report <file>`
appends the row to a file, and `--resolution WIDTHxHEIGHT` sets the back buffer (default 1920x1080 for benchmarks and flights, 1600x900 otherwise). Run the Release
build: `dotnet run --project src/Client/Gw2.Client -c Release -- …`. The mouse does not move the camera while a benchmark or flight runs.

`Gw2FrameTimings` computes the row:

| Column | Meaning |
| --- | --- |
| FPS | frames / recorded seconds |
| 1% low | frames / seconds of the slowest 1% of frames |
| Worst | longest frame, ms |
| CPU | average time from the first update of a frame until present is called, ms |
| Wait | average frame time minus CPU: present, GPU back-pressure and the limiter, ms |
| GPU | average GPU time from the first to the last timestamp of a frame (Crystal `GraphicsProfiler`), ms |
| Draws | average draw calls per frame (`GraphicsDevice.Metrics.DrawCount` before present) |

A frame with CPU close to the frame time is CPU-bound. GPU above CPU, or a large Wait at an uncapped limit, means the
GPU is the limit. The benchmark also prints the GPU time per section: each section runs from the previous timestamp to
the one written at its end (`GrFrameSection`). A second line gives the CPU time between the same marks. Its first
entry, Update, runs from the first update of the frame to the start of draw. SceneUpdate covers the terrain and prop
loaders, LOD levels and scatter fade before the first pass. NormalSpec, Shadows, Lighting, Sky, Reflection and Opaque are recorded
on their own command lists (`--parallel-recording on|off`, default on, records four of them on worker threads). Their
GPU time is a scope inside their list, their CPU time is the recording time on whichever thread recorded them, and
SceneRecording covers the render thread from the end of SceneUpdate until the lists are submitted.

## Flight mode

`Gw2.Client --flight "x,y,z;x,y,z;…"` moves the camera along the waypoints at `--flight-speed` units per second (default
1000), looking along each segment. The benchmark records for the length of the flight unless `--benchmark` is given.
Once a second it prints one `FLIGHT` line: the eye, process private bytes, managed heap, local video memory in use
(fork `GraphicsDevice.TryGetVideoMemoryInfo`, the device's adapter), resident terrain chunks, resident props, uploaded models and
textures, and the content cache.

Every recorded frame over 33 ms prints a `SLOW` line: frame and CPU time, the main-thread time of the terrain loader
(chunks and textures uploaded), water, the prop loader (activation time, textures and meshes uploaded, props and scatter
blocks placed) and prop LOD selection, the GC pause and gen0/gen2 collections since the frame started, and the CPU
time of every frame section.

The 1595 loop runs at z −3000 through `27102,6639; -27000,6639; -27000,-33000; 27000,-33000; 27102,6639`, about
218 000 units, at 2000 units per second (109 s per lap).

## Live settings

| Setting | Value |
| --- | --- |
| Animation, Environment, LOD Distance, Textures, Shaders | High |
| Antialiasing | None |
| Reflections | Terrain & Sky |
| Render Sampling | Native |
| Shadows | Ultra (resolution 2048, 3 cascades) |
| Character Model Limit, Character Model Quality | Medium |
| Best Texture Filtering, Effect LOD, High-Res Character Textures, Screen Space Shadows | on |
| Vertical Sync | off |
| Bloom, Color Grading | on |
| Frame limiter | unlimited (240) |

`Gw2.Client` runs with `--shadows ultra --resolution 1920x1080 --frame-limit unlimited --reflections terrain-and-sky`; the rest are its defaults.

## Results

shipwreck-strand-boat: map 1595 at night (04:00), on the boat, looking toward the map centre.

| Date | Client | Pose | Resolution | Limit | FPS | 1% low | Worst ms | CPU ms | Wait ms | GPU ms | Frames | Draws |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 2026-10-13 | live client | shipwreck-strand-boat | 1920x1080 | 240 | 115 | 111 | | | | | | |
| 2026-10-13 | Gw2.Client | shipwreck-strand-boat | 1920x1080 | 240 | 13.1 | 9.4 | 106.8 | 75.89 | 0.39 | | 263 | |
| 2026-10-13 | Gw2.Client | shipwreck-strand-boat | 1920x1080 | 240 | 13.7 | 9.6 | 110.0 | 72.72 | 0.41 | | 137 | 29000 |
| 2026-10-13 | Gw2.Client, frustum culling | shipwreck-strand-boat | 1920x1080 | 240 | 26.4 | 16.5 | 81.0 | 37.66 | 0.23 | | 265 | 17850 |
| 2026-10-13 | Gw2.Client, frustum culling, settled scene | shipwreck-strand-boat | 1920x1080 | 240 | 27.3 | 16.7 | 71.8 | 36.44 | 0.20 | | 273 | 17850 |
| 2026-10-13 | Gw2.Client, Terrain & Sky reflections | shipwreck-strand-boat | 1920x1080 | 240 | 36.1 | 20.4 | 67.9 | 27.49 | 0.22 | | 362 | 14941 |
| 2026-10-13 | Gw2.Client, instanced batching | shipwreck-strand-boat | 1920x1080 | 240 | 64.0 | 24.7 | 46.6 | 14.97 | 0.66 | | 640 | 4269 |
| 2026-10-13 | Gw2.Client, fewer per-frame allocations | shipwreck-strand-boat | 1920x1080 | 240 | 70.2 | 24.2 | 52.3 | 13.30 | 0.94 | | 703 | 4269 |
| 2026-10-13 | Gw2.Client, depth from the normal/spec pass | shipwreck-strand-boat | 1920x1080 | 240 | 78.7 | 27.7 | 42.3 | 11.67 | 1.03 | | 789 | 3518 |
| 2026-10-13 | Gw2.Client, map lights in the shader-quality square | shipwreck-strand-boat | 1920x1080 | 240 | 78.9 | 26.2 | 41.8 | 11.60 | 1.08 | | 789 | 3136 |
| 2026-10-13 | Gw2.Client, allocation-free draw constants and sorted draws | shipwreck-strand-boat | 1920x1080 | 240 | 111.9 | 42.5 | 44.6 | 8.77 | 0.16 | | 896 | 3136 |
| 2026-10-13 | Gw2.Client, allocation-free shadow casters | shipwreck-strand-boat | 1920x1080 | 240 | 123.5 | 87.8 | 12.2 | 7.97 | 0.13 | | 1235 | 3136 |
| 2026-10-13 | Gw2.Client, GPU timestamps | shipwreck-strand-boat | 1920x1080 | 240 | 119.6 | 83.1 | 13.8 | 8.22 | 0.15 | 6.53 | 1196 | 3136 |
| 2026-10-14 | Gw2.Client, GPU-driven props (`--gpu-driven off`) | shipwreck-strand-boat | 1920x1080 | 240 | 120.3 | 97.2 | 11.9 | 8.17 | 0.14 | 6.51 | 1203 | 3136 |
| 2026-10-14 | Gw2.Client, GPU-driven props | shipwreck-strand-boat | 1920x1080 | 240 | 137.5 | 112.2 | 10.1 | 7.13 | 0.15 | 5.90 | 1375 | 3145 |
| 2026-10-14 | Gw2.Client, cached frame constants and sampler bindings | shipwreck-strand-boat | 1920x1080 | 240 | 161.2 | 114.1 | 10.1 | 6.06 | 0.14 | 5.02 | 1613 | 3145 |

`Gw2.Client` is CPU-bound at the pose: almost the whole frame is spent before present. A `dotnet-trace` CPU sample of the
frame (2026-10-13) splits it into:

| Share | Where |
| --- | --- |
| 52% | Engine per-draw state: `GraphicsDevice.PlatformApplyState` 35% (re-binds constant buffers, textures and samplers for every draw), `RasterizerState.PlatformApplyState` 11% |
| 25% | client code: `GrStaticMesh.Draw`, sorting with LINQ, prop loader updates |
| 22% | GC pauses from per-frame allocations (LINQ, tuples, lists in `SortedDraws` and the caster lists) |

By pass: light buffer (normal/spec/depth) 48%, main pass 40%, shadow cascades 26%, water reflection props 20%
(nested, so they overlap). About 29000 draws per frame at about 2.5 µs each: every loaded prop and terrain chunk is
submitted in every pass, with no culling. Frustum culling per pass ([culling.md](../../gw2-research/docs/rendering/culling.md)) halves the frame time and leaves about 17850 draws. The per-draw cost was ordinary for the engine; the
draw count and the allocations are the client's. A character-only scene holds the caps (59.8 at 60, 143.9 at 144).

A second sample after instancing (2026-10-13, 78.9 fps) still spent about 15% of the frame in GC suspensions. Per pass,
measured with `GraphicsDevice.Metrics.DrawCount` and a stopwatch around each pass:

| Pass | Draws | ms before | ms after |
| --- | --- | --- | --- |
| Normal/spec | 927 | 2.66 | 1.81 |
| Shadow cascades | 608 | 1.80 | 1.49 |
| Water reflection | 370 | 1.60 | 1.03 |
| Main, opaque | 997 | 4.75 | 2.70 |

The allocations were per draw: a `Vector4[]` per material uniform (and one more to clear it), the frame globals
rebuilt per draw, a closure for the texture lookup and one for every sorted draw, a fade-colour dictionary per
instance group, and the shadow-caster LINQ chain. `GrDrawConstants` now writes registers into reused scratch arrays,
sorted draws are plain structs dispatched by kind, and depth casters fill reused lists. The frame rate is above live
at the pose; the draw count is unchanged.

### 1595 flight

Two laps of the loop at 2000 units per second (2026-10-14, night, live settings):

| Date | Client | Pose | Resolution | Limit | FPS | 1% low | Worst ms | CPU ms | Wait ms | GPU ms | Frames | Draws |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 2026-10-14 | Gw2.Client | 1595 flight, two laps | 1920x1080 | 240 | 159.5 | 38.4 | 848.2 | 5.13 | 1.14 | 4.40 | 29892 | 1685 |

| Time | Eye | Private MB | Managed MB | GPU MB | Chunks | Props | Models | Textures |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 0 s | 27102, 6639 | 5497 | 2405 | 1453 | 500 | 4075 | 548 | 1100 |
| 15 s | −2912, 6639 | 6194 | 1252 | 1967 | 500 | 7912 | 698 | 1018 |
| 45 s | −27000, −29265 | 5681 | 1768 | 1967 | 460 | 3546 | 300 | 554 |
| 75 s | 27006, −30733 | 5577 | 1612 | 1903 | 440 | 2529 | 326 | 668 |
| 105 s | 4482, 6639 | 5595 | 1303 | 1972 | 500 | 7516 | 742 | 1160 |
| 150 s | −8115, −33000 | 5424 | 1179 | 1973 | 440 | 5341 | 368 | 602 |
| 180 s | 27064, −8105 | 5274 | 1486 | 1845 | 500 | 5353 | 609 | 1145 |

Memory stays flat: process private bytes return to about 5.4 GB each lap, the managed heap cycles between 0.9 and
1.9 GB with the GC, and video memory reaches its high-water mark (about 1970 MB) on the first lap and holds there on
the second. Props, models and textures rise and fall with the region the camera crosses. The worst frame (848 ms) and
the 1% low come from loading on the main thread (ROADMAP streaming step 1, background loading).

### Slow frames

One lap (2026-10-14, night, live settings) after each background-loading change: terrain textures on the content worker
with a 3 ms upload budget, the prop placement grid, and AMAT decoding on the content worker:

| Date | Client | Pose | Resolution | Limit | FPS | 1% low | Worst ms | CPU ms | Wait ms | GPU ms | Frames | Draws |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 2026-10-14 | Gw2.Client | 1595 flight, one lap | 1920x1080 | 240 | 165.9 | 35.1 | 114.8 | 4.90 | 1.13 | 3.72 | 15541 | 1116 |
| 2026-10-14 | Gw2.Client, terrain on worker | 1595 flight, one lap | 1920x1080 | 240 | 170.9 | 38.0 | 115.1 | 4.65 | 1.20 | 3.73 | 16011 | 1122 |
| 2026-10-14 | Gw2.Client, prop grid | 1595 flight, one lap | 1920x1080 | 240 | 187.0 | 52.1 | 94.4 | 4.26 | 1.09 | 3.65 | 17517 | 1132 |
| 2026-10-14 | Gw2.Client, materials on worker | 1595 flight, one lap | 1920x1080 | 240 | 188.4 | 52.7 | 73.8 | 4.21 | 1.10 | 3.62 | 17651 | 1142 |
| 2026-10-14 | Gw2.Client, LOD Distance High | 1595 flight, one lap | 1920x1080 | 240 | 184.9 | 57.7 | 52.6 | 4.42 | 0.99 | 3.71 | 17319 | 1227 |

The first two runs have 22 frames over 33 ms each. No slow frame spends time in the terrain loader, so terrain was not the cause.
By the largest main-thread share:

| Cause | Before | After | Main-thread ms |
| --- | --- | --- | --- |
| Prop activation | 14 | 19 | 9–39 |
| Prop upload over the 5 ms budget | 5 | 3 | 14–83 |
| Present with low CPU | 2 | 0 | — |
| Other | 1 | 0 | — |

Prop activation walks every placement of the map, builds a set of the pending ones and re-sorts them each time the
activation cell changes. Prop uploads overshoot because the budget is checked only between whole texture and mesh
uploads. Slow frames also show 12–21 ms in the normal/spec pass, where first draws build pipeline states, and gen0 GC
pauses of 6–11 ms. The 848 ms frame of the two-lap run did not recur in one lap.

Prop activation now keeps the placements in a 1024-unit grid (`Gw2PlacementGrid`). A recentre re-tests only the
placements in cells that cross the edge of the old or the new rectangle, and removed props leave the pass in one batch;
the rule itself (the exe's 15360 rectangle, recentred past 121 units) is unchanged. That left 4 slow frames, each a
single prop placement of 15–31 ms: `AddProp` read and decoded the AMAT on the main thread for every material variant
it built. `Gw2ArchiveMaterials` now decodes each AMAT once on the content worker; terrain chunks, props and scatter
blocks are placed only after their materials are decoded, and no material is read on the main thread during the lap.

The last run has 7 slow frames (35–74 ms). None spends more than 3 ms in the loaders; their CPU time is 4–20 ms and
the rest is present and GPU wait, so they belong to the upload queue and pipeline-state steps.

With LOD Distance High (the live setting, now the default) the scatter places the full tiling fill and fades out 1.5×
farther, and props switch to coarser levels 1.5× farther out: 76 more draws, 2 slow frames (the first frame, 53 ms,
and one of 41 ms), against 193–202 fps and one slow frame at Medium.

### GPU time by section

With GPU timestamps (2026-10-13), the GPU spends 6.5 ms per frame and the CPU 8.2 ms, so the pose is still CPU-bound.

| Section | GPU ms |
| --- | --- |
| Normal/spec | 1.45 |
| Shadow cascades | 1.47 |
| Light accumulation | 0.66 |
| Sky, stars, sky cards | 0.27 |
| Water reflection | 0.69 |
| Main, opaque | 1.45 |
| Water | 0.29 |
| Translucent, distortion | 0.08 |
| Clouds, particles, post, present copy | 0.17 |

A CPU sample of the same run puts 42% of the frame in engine per-draw state (`GraphicsDevice.PlatformApplyState`
24.6%, `RasterizerState.PlatformApplyState` 13.5%, `BlendState.PlatformApplyState` 4.3%). CPU culling, sorting and
instance writes together are about 12% (`SortedDraws` 5.1%, `GrDepthCasterBatcher.Batch` 4.0%,
`GrInstanceStream.Append` 1.7%, `DepthCasters` 1.4%). GPU-driven culling can only remove the second part, so the fork's
per-draw path comes first.

### Per-draw cost bounds

The sample above comes from the EventPipe profiler, which only stops managed code at safe points and so over-weights
the code around P/Invoke calls. Throwaway fork builds that skip parts of the draw path bound the cost directly
(2026-10-13, two runs each, 3136 draws):

| Fork build | CPU ms |
| --- | --- |
| unchanged | 8.15–8.67 |
| native draw returns before `MGDX_ApplyState` | 6.88 |
| `GraphicsDevice.Draw*` skips `ApplyState` and the native draw | 6.19–6.51 |
| also `EffectPass.Apply` skipped | 5.14–5.45 |

The whole engine per-draw path is about 3 ms: 1.2 native (state apply and D3D12 recording, about 0.4 µs a draw),
0.9 managed state apply, 1.1 effect constant buffer and texture updates. The other 5.3 ms is the client's own work. A
native pipeline-state dirty flag that skips the per-draw PSO description hash measured 8.06 against 8.04 and was
dropped. Same-configuration runs differ by up to 0.9 ms, so single fixes under that need repeated runs.

### CPU time by section

With CPU section marks (2026-10-13, 8.55 ms CPU in that run):

| Section | CPU ms |
| --- | --- |
| Update | 0.27 |
| Scene update (loaders, LOD, scatter fade) | 0.43 |
| Normal/spec | 1.88 |
| Shadow cascades | 1.57 |
| Light accumulation | 0.11 |
| Sky, stars, sky cards | 0.07 |
| Water reflection | 1.11 |
| Main, opaque | 2.73 |
| Water | 0.22 |
| Translucent, distortion | 0.13 |
| Clouds, particles, post, present copy | 0.05 |

The four geometry passes hold 7.3 ms, which includes the 3 ms of engine draw path above. Their remaining 4.3 ms is
culling, sorting, batching, instance writes and effect parameter setting, the work GPU-driven culling moves off the
CPU.

GPU-driven props ([culling.md](../../gw2-research/docs/rendering/culling.md)) move the prop cull, grouping and instance writes of the
normal/spec, shadow and main passes into one compute dispatch per pass. In three clean runs each, CPU time is
7.0–7.2 ms with the path on and 8.2 ms off. Per section: main opaque 2.0 vs 2.7 ms, shadows 1.2 vs 1.5 ms,
normal/spec 1.7 vs 1.8 ms. The draw count stays about the same, because each visible group is still one draw. The
measured GPU time does not grow with the cull dispatches. With the CPU at 7.1 ms and the GPU at 5.9 ms, the pose is
close to GPU-bound on the test machine (Ryzen 7 9800X3D, Radeon RX 9060 XT).

Per-draw cost on the archive path (2232 draws per frame) was measured with a temporary stopwatch around each stage:
constants 1.50 ms, texture bindings 0.55, render states 0.12, effect apply 0.75, draw call 1.40. Frame-level uniforms
(view, projection, SH, fog, time, wind, screen) are now written to an effect only when the frame values change; the
clock is read once per pass instead of once per draw, which made every draw see a new frame. Sampler effect
parameters and resolved sampler states are cached per material. Constants drop to 0.98 ms and textures to 0.23 ms.
Back to back against main: 161 vs 142 fps, CPU 6.1 vs 6.9 ms. The remaining apply and draw cost sits in the fork.

Hi-Z occlusion ([culling.md](../../gw2-research/docs/rendering/culling.md)) at the boat pose, three alternating runs each, Release:

| Hi-Z | FPS | 1% low | Worst ms | CPU ms | GPU opaque ms | GPU normal/spec ms |
| --- | --- | --- | --- | --- | --- | --- |
| off | 140.4–142.9 | 90.4–97.3 | 12.1–18.8 | 6.84 | 1.74–1.76 | 1.54–1.56 |
| on | 134.1–139.1 | 67.0–78.2 | 23.9–35.7 | 6.99–7.20 | 1.67–1.71 | 1.61–1.62 |

### Crystal port

Crystal (#94, #95, with Crystal #6) against the build before the port (095404b), both Release, 1600x900, unlimited,
two alternating runs per pose: Crystal is 16–26% faster, with about 1.0–1.5 ms less CPU and 0.4–0.9 ms less GPU per
frame at equal draw counts. Before Crystal #6, `SetData` on a resource the GPU already used created and destroyed a
committed staging buffer per call, about 13 per frame, costing 0.6–0.7 ms of deferred releases in Update; in-frame
updates now copy from the per-frame upload ring.
