# Physical bloom

**Not in the exe.** This is a graphics upgrade over the original client, behind `--physical-bloom on|off` (default off). With
it off, the exe's bloom chain runs unchanged ([post-processing.md](../../gw2-research/docs/rendering/post-processing.md) "Bloom") and the image is the
exe image.

## What the exe has

One quarter-resolution target. `PostProcDownSampleIntensity` keeps `saturate(rgb · luma − glowLevel)`, then two
rounds of a 4-tap horizontal and vertical blur, the second with twice the step. Each blur pass multiplies by
`glowAmplify + 1`. The spread is a single narrow kernel: halos are tight and end abruptly.

## What the upgrade does

It replaces only the bloom texture. The combine (`GrPostProcessCombine.fx`) still adds `2 · glow · bloom`, so the
environment's glow colour, day/night blend and zone blend stay in charge.

1. **Prefilter** (half resolution): a 13-tap downsample of the scene. Each of its five 2×2 groups goes through the exe
   intensity curve with the same `glowLevel`, so the same pixels glow as in the exe. The groups are combined with a
   Karis average (`1 / (1 + luma)`), which keeps single bright pixels from flickering.
2. **Downsample**: the 13-tap filter again, down to an 8-pixel-high level, at most 7 levels.
3. **Upsample**: from the smallest level up, a 3×3 tent filter added onto the next larger level. Every level adds an
   equal share, so the point-spread has a long tail, close to a real lens.
4. **Resolve**: the sum is divided by the level count and multiplied by `(glowAmplify + 1)^4`, the total gain of the
   exe's four blur passes. The glow energy is the same as the exe's; only its spread changes.

All levels are `HalfVector4`, so the sums do not clip.

Check, map 1595 night boat pose: the mean brightness differs by 0.05/255 from the exe chain. The exe's tight halos
around bright spots become wider and fainter.

## Code

`GrPhysicalBloom` (`Shaders/GrPhysicalBloom.fx`); `GrPostProcessCombine.PhysicalBloom` switches it on.
