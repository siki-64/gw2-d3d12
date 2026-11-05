# Hidden global water

Every map has the global water plane at z=0, gated only by a visibility box of eye ± far clip
([water.md](../../gw2-research/docs/rendering/water.md) "Reflection gate"). With the camera level or pointing down that
box passes the frustum test, so the exe runs the reflection pass even over dry land.

The client adds a mask: at load, `Gw2GlobalWaterCells` splits the map bounds into terrain-chunk-sized
cells (3072 units). A cell counts as showing water when any height sample in it lies below the
plane and the terrain cut leaves depth above 0. Chunks with both the opaque-water and the clear-water
flags never show water. Everything outside the map bounds counts as showing water. The global plane
only counts as visible when one of those boxes, within reach of the far clip, passes the frustum. If
none does, the plane is neither drawn nor reflected.

The test cannot see hills hiding the water; occlusion is not handled. `--vanilla-water-gate` turns
the mask off for live comparisons.
