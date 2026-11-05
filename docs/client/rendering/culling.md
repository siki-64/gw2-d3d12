# Culling: new client

How the new client implements [Culling](../../../gw2-research/docs/rendering/culling.md). The retail behaviour and its evidence are in that note.

## Client

`GrCullFrustum` tests world-space boxes with the p-vertex test against the planes of a view-projection matrix.

| Pass | Frustum |
| --- | --- |
| Main, light buffer, scene depth | Camera |
| Water reflection | Mirrored camera with the oblique water-plane projection |
| Shadow cascades | Cascade light view-projection without its near plane, so casters between the light and the cascade stay |

Each pass culls terrain chunks, props and instance groups. Characters are always drawn. An instance group is culled
as one box, the union of its instances.

Open:

- The sphere pre-test and the near-plane depth output.
- The capsule shadow-caster test.
- Umbra occlusion and zone culling.

## Batching

Instance data goes to `GrInstanceStream`, a dynamic buffer that is discarded on the first write of a frame and
appended with no-overwrite after that. Each `GrStaticMeshBatch` draws its range with a base instance. Props intern
their constant dictionaries by content (`GrMaterialConstantsComparer`), as the exe's `memcmp` does.

## GPU-driven props (client only)

The exe culls and batches on the CPU. The client can move the static props onto the GPU with the same image. This
is on by default; `--gpu-driven off` keeps the CPU path as the reference.

`GrGpuScene` keeps every resident prop in a structured buffer of 112-byte `GrGpuInstance` entries. Each entry holds:

- the 64-byte instance row (three world rows and the model colour);
- the world box;
- the LOD state index and the prop's LOD level;
- a draw group and a shadow-caster group.

Draw groups use the same key as the batching above: mesh, constants and model colour. Caster groups are keyed by mesh,
since the alpha mask is per mesh. `GrGpuSceneGroups` packs each group's instances into one range, and the range start
is the group's base instance. The layout is rebuilt in `BeginFrame` after props are added or removed.

Each frame:

- the CPU uploads the current level of every LOD state;
- the CPU counts each group's props at the current level and unions their boxes;
- a pass culls these group boxes on the CPU and draws only groups that are visible and non-empty.

`GrGpuCull.hlsl` (cs_6_0, compiled by dxc at build time) then runs one thread per prop:

- it checks the LOD level;
- it runs the same p-vertex test as `GrCullFrustum`;
- it does an `InterlockedAdd` on the group's instance count;
- it writes the row into the group's range.

Each pass has its own row buffer and argument buffer, and the CPU resets the argument template (index count,
zero instances, base instance) before the dispatch. Every visible group becomes one `ExecuteIndirect` draw, with its
PSO, textures and constants bound as before. The archive shaders stay unchanged: indirect draws use the instanced
vertex variant that already reads the rows from a vertex stream.

| Pass | GPU-driven subset | Cull |
| --- | --- | --- |
| Normal/spec | Props with an opaque normal/spec material and an instanced variant | Camera, shared with the main pass |
| Main | Props in the default bucket: opaque, LessEqual depth with writes, no later passes | Camera |
| Shadow cascades | Props whose normal/spec material is opaque | Cascade without its near plane |

Main-pass GPU groups are drawn after the opaque buckets below the default one and before the rest. Order inside the
default bucket does not change the image for opaque LessEqual draws.

Everything else stays on the CPU path:

- blended, decal, distortion and split-pass materials;
- scatter instance groups;
- terrain;
- characters;
- water;
- the water reflection.

## Hi-Z occlusion (client only)

**Not in the exe.** `--hi-z on|off` (default off, needs `--gpu-driven on`) adds an occlusion test to the main-pass
cull of GPU-driven props.

After the normal/spec pass, `GrHiZ` builds a max-depth pyramid from the normal/spec depth (D24S8, near 0, far 1).

- Mip 0 is a `Single` render target with unordered access, floor(W/2)×floor(H/2), with D3D floor mip sizes.
- `GrHiZFirstLevel.hlsl` reduces 2×2 depth pixels into each mip 0 texel. The last row and column also take the odd
  extra pixel.
- `GrHiZReduce.hlsl` reduces each mip into the next one in the same way, one dispatch per level.
- A depth pixel p maps to the level L texel `min(p >> (L + 1), size(L) - 1)`.

The main pass culls in its own slot, because normal/spec and the shadow cascades must not use the pyramid they are
drawn before. `GrGpuCull.hlsl` keeps the frustum test and then runs these steps for each prop that passes:

1. Project the 8 box corners with the camera view-projection. If any corner has w ≤ 0 or clip z < 0, the prop is
   visible.
2. Take the screen rectangle of the corners and the nearest NDC z.
3. Pick the lowest level at which the rectangle spans at most 2×2 texels, and read those 4 texels.
4. The prop is occluded if its nearest z is greater than the largest of the 4.

The normal/spec depth holds everything the main pass draws with depth writes, so a prop that fails the test cannot
reach a pixel. Normal/spec and shadow passes keep the frustum-only cull.

### Measurements

The image is exact. Day and boat poses on map 1595 with Hi-Z on match off at the run-to-run noise level, and off runs
alone show the same outliers from texture streaming timing.

At the boat pose (1920×1080, ultra shadows, terrain-and-sky reflections), main opaque GPU time drops from 1.74–1.76
to 1.67–1.71 ms. The pyramid adds about the same to normal/spec (1.54–1.56 to 1.61–1.62 ms). The draw count stays
3587, because a culled group is still one indirect draw with zero instances. FPS is 134–139 on vs 140–143 off,
see [benchmarks.md](../benchmarks.md). It stays off by default until a pose with more occlusion or a fork-side
draw-count path (count buffer for `ExecuteIndirect`) makes it pay off.
