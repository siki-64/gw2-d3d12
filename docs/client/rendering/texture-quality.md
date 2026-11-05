# Texture quality and residency: new client

How the new client implements [Texture quality and residency](../../../gw2-research/docs/rendering/texture-quality.md). The retail behaviour and its evidence are in that note.

## Texture quality and residency

The remake mirrors them in `GrTexResidency` for prop textures.<br> **Unresolved:** whether terrain pages and
ground-cover scatter register texture distances (they do not go through the `GrModel` texLod arrays read here), and
the meaning of the `0x20`/`0x40` bits in the size word.

## Remake

`GrTexResidency` (`Gw2.Client.Graphics`) holds the threshold, the hysteresis and the per-texture state. `Gw2PropLoader`
reports the distance of every loaded prop to its world bounds each frame. It applies the promote and demote lists by
swapping the texture in the prop's texture slots. `--textures high|medium|low` picks the level (default High, as live).
