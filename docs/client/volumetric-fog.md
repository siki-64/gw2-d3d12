# Volumetric fog

**Not in the exe.** This is a graphics upgrade over the original client, behind `--volumetric-fog on|off` (default off). With it
off, no pass runs and the image is the exe image.

## What the exe has

Only the per-vertex distance + height fog `VSCalculateFog` ([fog.md](../../gw2-research/docs/rendering/fog.md)), baked into every archive shader. It is
the same everywhere along a view ray: fog in the shadow of a rock is as bright as fog in the sun.

The exe also has a **Light Rays** post pass (enable bit `0x200`, only with the sun on screen;
[post-processing.md](../../gw2-research/docs/rendering/post-processing.md) "Enable mask"). It is off live and not mirrored. The volumetric fog does not
depend on it.

## What the client adds

The exe vertex fog stays on for every draw. After the opaque passes, a correction removes the part of the fog that the
sun could not light:

1. **March** (`GrVolumetricFogMarch.fx`, half resolution, end of `RenderLightBuffer`). It reads the normal/spec pass
   depth and walks 32 steps from the eye to the surface (`along²` spacing, a 2×2 jitter). Sky and far-plane pixels
   march to the far plane. Each step evaluates the exe fog alpha (the `VSCalculateFog` formula) and takes the alpha
   added since the previous step. That increment is weighted by sun visibility at the step: one bilinear gather from
   the shadow cascades, or the far shadow VSM outside them (lit outside its map). The output is the lit ratio, the
   sun-lit share of the pixel's total fog alpha.
2. **Composite** (`GrVolumetricFogComposite.fx`, full resolution, after the opaques, before water). It recomputes the
   exe fog `F` per pixel from depth and averages the lit ratio over 4 bilinear taps. It then subtracts
   `F.rgb · sunShare · (1 − litRatio) · (1 − |shSunData.w|)` from the scene (reverse-subtract blend).
   - `sunShare` is the part of the fog light that comes from the sun. It is 0.35 across the sun and rises to 0.8
     toward it, following a Henyey-Greenstein phase with g = 0.6 normalised to 1 at the sun.
   - `|shSunData.w|` fades the effect the way the sun light term fades.

Where all the fog is lit, the image is the exe image. Shadowed fog darkens, so occluders cut shafts into the haze, and
more so when looking into the sun. Transparents, water and distortion draw after the composite with their own exe fog.

## Why not the exe fog off plus a full froxel fog

The first version zeroed the fog alphas (`FogColorFar.a`, `FogColorNearMinusFar.a`, `FogColorHeight.a`) for the opaques
and drew the whole fog in the composite. Two cases broke it:

- Geometry at or past the far plane (backdrop mountains, far terrain) has depth 1 in D24. Its real distance is lost, so
  the composite either fogs it as sky or at the far plane, and it washes out.
- Any opaque draw missing from the normal/spec pass would lose its fog completely.

The correction approach keeps the exe fog where nothing better is known.

## Why a pixel shader and not compute

When the pass was written, the engine bound only structured buffers to compute, not textures, so a compute froxel pass
could not sample the shadow maps. The half-resolution pixel march gives the same per-ray integral without a 3D volume.

## Cost and open points

- Two fullscreen passes: a half-resolution march (32 steps, 1 gather each) and a full-resolution composite. Not yet
  benchmarked ([benchmarks.md](benchmarks.md)).
- No temporal accumulation. The 2×2 jitter is averaged by the 4-tap composite.
- The constants (`SunShareOfFogAcrossSun`, `SunShareOfFogTowardSun`, `SunScatteringAnisotropy` in `GrVolumetricFog`)
  were picked by eye at the 1595 day and dusk poses.
