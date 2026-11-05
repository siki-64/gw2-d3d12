# Environment particle fields: new client

How the new client implements [Environment particle fields](../../../gw2-research/docs/rendering/particle-fields.md). The retail behaviour and its evidence are in that note.

## Per-frame constants (`EnvParticleField_UpdateConstants`)

The remake treats it like clouds: each environment's own fields at its zone weight, and the global environment at `1 −
total`.

## Remake

`Gw2ParticleFieldMesh` and `Gw2MinimalStandardRandom` (Gw2.Dat.Content) build the mesh as the exe does.
`GrParticleField.fx` ports the shader. `Gw2ParticleFieldFrame.BlendFlags` applies the converter's blend default.
`Gw2ParticleFieldScene` draws the zone-blended fields after the clouds with slot 12 = the linear depth copy. The
missing-texture fallback is the shared 1×1 `0xFFFF0000`.

First render against shipwreck-strand-night: sparkles over the water and motes in the air match in kind. The density
matches after the depth fix below. Still open: `fieldDirection` is not rotated by the map matrix, the
graphics metric 13 large-particle cut is not applied, and fields are drawn after the clouds, not in bucket 13.

Live check (x64dbg at the `fldprtg` call `EnvParticleField_UpdateConstants+0x5A0`, map 1595 at night): the exe has the same 20
runtime fields (10–19 repeat 0–9 in the content) and draws the same eight (2, 3, 6, 8, 12, 13, 16, 18) at weight 0.996.
`fldprta`–`fldprtg` for fields 2 and 3 match `Gw2ParticleFieldFrame` exactly (field 3: depth 61.86, fade 50, 2×2
flipbook). The day weight the fields read is `EnvContext+0x1050` (setter `EnvContext_SetTime`, 0 at night). Starting
the field clock at the live `t` (2270 s) does not change the density. The gap was the depth test: the remake drew
without one and its water did not write depth. With both as live, the density matches the shipwreck-strand-night shot.

### Variants

All eight AMATs share one pixel shader. The vertex shaders differ per `type`; flag `0x10` adds the depth slab.

| Type | Motion | Billboard | Alpha |
| --- | --- | --- | --- |
| 0 | drift `speed * t * 2000/E` along the direction, no deviation | axial: `cross(toEye, direction)` and the direction | `min(wave(spread) * w * 4, 1) * opacity` |
| 1 | drift by `frac(spread)`, xy wobble at 2.223 and 3.223 | view-facing | `min(wave(spread) * w * 4, 1) * opacity` |
| 2 | drift `speed * t * 0.025`, xyz wander | view-facing | `twinkle⁴ * opacity * w` |
| 3 | sideways sway along the xy of the direction | axial, quad hangs from the anchor (`v * −2`) | `twinkle⁴ * opacity * w`, × `1 − (dist/E)⁴` |

Flag `0x10` (slab): world z → `mod(z, depth) − altitude`, alpha × `saturate((depth/2 − |z' − depth/2|) / fade)`.
The billboard's eye direction and the 100-unit height fade use the position before the slab.

`GrParticleField.fx` ports all eight (techniques `Streak`, `Bubble`, `Sparkle`, `Ray` and their `…Slab` forms).
